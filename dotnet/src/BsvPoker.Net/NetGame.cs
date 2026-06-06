using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using BsvPoker.Core;

namespace BsvPoker.Net;

/// <summary>
/// A networked 2-player Texas Hold'em over the P2P table channel — NO server, NO dealer. Both peers run
/// this identically: they exchange commit→reveal entropy (dealerless mental-poker deck), assign seats
/// deterministically by sorted session pubkey, then exchange betting actions; each peer applies every
/// action through the SAME audited engine, so both converge to identical state. Raises OnUpdate on the
/// node thread (the UI marshals to the dispatcher).
/// </summary>
public sealed class NetGame
{
    public enum Phase { WaitingForPlayer, Dealing, Playing, Done }

    private readonly P2PNode _node;
    private readonly string _table;
    private readonly string _myPubHex;
    private Action? _unsub;
    private System.Threading.Timer? _ticker;

    private readonly ConcurrentDictionary<string, byte> _players = new(); // pubHex set
    private readonly ConcurrentDictionary<int, byte[]> _commits = new();   // seat -> commit
    private readonly ConcurrentDictionary<int, byte[]> _reveals = new();   // seat -> entropy
    private readonly byte[] _myEntropy = MentalPoker.FreshEntropy();
    private readonly object _gate = new();

    public Phase State { get; private set; } = Phase.WaitingForPlayer;
    public int MySeat { get; private set; } = -1;
    public string[] SeatPubs { get; private set; } = Array.Empty<string>();
    public HoldemState? Hand { get; private set; }
    public string Status { get; private set; } = "Waiting for an opponent to join…";
    public event Action? OnUpdate;

    public Variant Variant { get; }

    public NetGame(P2PNode node, string tableId, byte[] myPub)
    {
        _node = node; _table = tableId; _myPubHex = Convert.ToHexString(myPub).ToLowerInvariant();
        _players[_myPubHex] = 1;
        // the variant is encoded in the table id ("t-<hex>~<Variant>") so both peers agree without extra messages
        var parts = tableId.Split('~');
        Variant = parts.Length > 1 ? Variants.Parse(parts[1]) : Variant.TexasHoldem;
    }

    public void Start()
    {
        _unsub = _node.Subscribe(_table, OnMessage);
        _ticker = new System.Threading.Timer(_ => Tick(), null, 0, 500);
    }

    public void Stop() { _ticker?.Dispose(); _unsub?.Invoke(); }

    private void Send(object o) => _ = _node.PublishAsync(_table, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(o)));

    private void Tick()
    {
        try
        {
            lock (_gate)
            {
                Send(new { t = "hello", pub = _myPubHex });
                if (SeatPubs.Length == 2)
                {
                    if (!_commits.ContainsKey(MySeat)) { } // wait
                    Send(new { t = "commit", seat = MySeat, c = Convert.ToHexString(MentalPoker.Commit(_myEntropy)).ToLowerInvariant() });
                    if (_commits.Count == 2) Send(new { t = "reveal", seat = MySeat, r = Convert.ToHexString(_myEntropy).ToLowerInvariant() });
                    TryAssignSeats();
                    TryDeal();
                }
                else TryAssignSeats();
            }
        }
        catch { }
    }

    private void TryAssignSeats()
    {
        if (SeatPubs.Length == 2) return;
        if (_players.Count < 2) { Status = "Waiting for an opponent to join…"; return; }
        var two = _players.Keys.OrderBy(x => x, StringComparer.Ordinal).Take(2).ToArray();
        SeatPubs = two;
        MySeat = Array.IndexOf(two, _myPubHex);
        _commits[MySeat] = MentalPoker.Commit(_myEntropy);
        State = Phase.Dealing;
        Status = $"Opponent found — you are seat {MySeat}. Agreeing the deck (commit/reveal)…";
        Raise();
    }

    private void TryDeal()
    {
        if (State != Phase.Dealing || Hand != null) return;
        if (_reveals.Count < 2) return;
        // verify both reveals match their commits
        for (int s = 0; s < 2; s++)
        {
            if (!_reveals.TryGetValue(s, out var e) || !_commits.TryGetValue(s, out var c)) return;
            if (!MentalPoker.VerifyCommit(c, e)) { Status = $"seat {s} reveal did not match its commit — refusing"; State = Phase.Done; Raise(); return; }
        }
        var deck = MentalPoker.ShuffledFrom(new[] { _reveals[0], _reveals[1] }, Variants.CardSet(Variant));
        Hand = HoldemState.Create(new long[] { 100, 100 }, button: 0, sb: 1, bb: 2, deck, Variant);
        State = Phase.Playing;
        Status = "Dealt. " + Hand.Message;
        Raise();
    }

    private int _applied;
    private void OnMessage(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            var t = root.GetProperty("t").GetString();
            switch (t)
            {
                case "hello":
                    var pub = root.GetProperty("pub").GetString();
                    if (pub != null && _players.TryAdd(pub, 1)) { lock (_gate) TryAssignSeats(); }
                    break;
                case "commit":
                    lock (_gate) { _commits[root.GetProperty("seat").GetInt32()] = Convert.FromHexString(root.GetProperty("c").GetString()!); }
                    break;
                case "reveal":
                    lock (_gate) { _reveals[root.GetProperty("seat").GetInt32()] = Convert.FromHexString(root.GetProperty("r").GetString()!); TryDeal(); }
                    break;
                case "act":
                    lock (_gate) ApplyRemote(root);
                    break;
            }
        }
        catch { }
    }

    private void ApplyRemote(JsonElement root)
    {
        if (Hand == null || Hand.Complete) return;
        int seq = root.GetProperty("seq").GetInt32();
        if (seq != _applied) return; // out of order / duplicate
        int seat = root.GetProperty("seat").GetInt32();
        if (seat != Hand.ToAct) return;
        var kind = Enum.Parse<ActionKind>(root.GetProperty("kind").GetString()!);
        long amt = root.GetProperty("amount").GetInt64();
        try { Hand.Apply(new GameAction(kind, seat, amt)); _applied++; Status = Hand.Message; if (Hand.Complete) State = Phase.Done; Raise(); }
        catch { }
    }

    /// <summary>Called by the UI when it is MY turn. Applies locally and broadcasts to the peer.</summary>
    public void Act(ActionKind kind, long amount)
    {
        lock (_gate)
        {
            if (Hand == null || Hand.Complete || Hand.ToAct != MySeat) return;
            int seq = _applied;
            try { Hand.Apply(new GameAction(kind, MySeat, amount)); _applied++; }
            catch (Exception ex) { Status = ex.Message; Raise(); return; }
            Send(new { t = "act", seat = MySeat, seq, kind = kind.ToString(), amount });
            Status = Hand.Message;
            if (Hand.Complete) State = Phase.Done;
            Raise();
        }
    }

    private void Raise() { try { OnUpdate?.Invoke(); } catch { } }
}

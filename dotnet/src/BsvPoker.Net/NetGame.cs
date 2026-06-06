using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using BsvPoker.Core;
using BsvPoker.Crypto;

namespace BsvPoker.Net;

/// <summary>
/// A networked 2-player poker hand over the P2P table channel — NO server, NO dealer, and with TRUE
/// per-card privacy. Both peers run this identically. They cooperatively build a commutative-encryption
/// deck (<see cref="MentalPokerEC"/>): each masks+shuffles, then re-masks with independent per-card
/// scalars. A card is dealt by the holder of the other masks revealing only that position's scalar, so a
/// player learns ONLY their own hole cards; the board is revealed per street by both peers, and opponent
/// hole cards only at showdown. The shared <see cref="HoldemState"/> (deferred-showdown mode) adjudicates
/// betting identically on both peers. Raises OnUpdate on the node thread (UI marshals to the dispatcher).
/// </summary>
public sealed class NetGame
{
    public enum Phase { WaitingForPlayer, Dealing, Playing, Done }

    private readonly P2PNode _node;
    private readonly string _table;
    private readonly string _myPubHex;
    private Action? _unsub;
    private System.Threading.Timer? _ticker;
    private readonly object _gate = new();

    private readonly ConcurrentDictionary<string, byte> _players = new();

    // --- commutative-encryption deal state ---
    private readonly IReadOnlyList<Card> _cardSet;
    private readonly int _n;
    private readonly byte[] _ecGlobal;          // my global mask c
    private readonly byte[][] _ecPerCard;       // my per-card masks d[0..n)
    private readonly int[] _ecPerm;             // my secret shuffle permutation
    private byte[][]? _s1, _s2, _r1, _final;    // the four deck-passing stages
    private readonly Dictionary<int, byte[]> _oppHoleD = new();   // opponent d at MY hole positions (deal)
    private readonly Dictionary<int, byte[]> _oppShowD = new();   // opponent d at THEIR hole positions (showdown)
    private readonly Dictionary<int, Dictionary<int, byte[]>> _boardD = new(); // seat -> (pos -> d) for streets
    private readonly HashSet<int> _boardStreetsSupplied = new();

    public Phase State { get; private set; } = Phase.WaitingForPlayer;
    public int MySeat { get; private set; } = -1;
    public string[] SeatPubs { get; private set; } = Array.Empty<string>();
    public HoldemState? Hand { get; private set; }
    public string Status { get; private set; } = "Waiting for an opponent to join…";
    public event Action? OnUpdate;
    public Variant Variant { get; }

    private int HoleCount => Variants.HoleCards(Variant);
    private int BoardStart => HoleCount * 2;
    private int OppSeat => 1 - MySeat;
    private IEnumerable<int> HolePositions(int seat) => Enumerable.Range(seat * HoleCount, HoleCount);

    public NetGame(P2PNode node, string tableId, byte[] myPub)
    {
        _node = node; _table = tableId; _myPubHex = Convert.ToHexString(myPub).ToLowerInvariant();
        _players[_myPubHex] = 1;
        var parts = tableId.Split('~');
        Variant = parts.Length > 1 ? Variants.Parse(parts[1]) : Variant.TexasHoldem;
        _cardSet = Variants.CardSet(Variant);
        _n = _cardSet.Count;
        _ecGlobal = MentalPokerEC.NewScalar();
        _ecPerCard = MentalPokerEC.NewPerCardScalars(_n);
        _ecPerm = RandomPerm(_n);
    }

    private static int[] RandomPerm(int n)
    {
        var p = Enumerable.Range(0, n).ToArray();
        for (int i = n - 1; i > 0; i--) { int j = (int)System.Security.Cryptography.RandomNumberGenerator.GetInt32(i + 1); (p[i], p[j]) = (p[j], p[i]); }
        return p;
    }

    public void Start()
    {
        _unsub = _node.Subscribe(_table, OnMessage);
        _ticker = new System.Threading.Timer(_ => Tick(), null, 0, 500);
    }

    public void Stop() { _ticker?.Dispose(); _unsub?.Invoke(); }

    private void Send(object o) => _ = _node.PublishAsync(_table, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(o)));

    // ---- serialization helpers ----
    private static string[] PtsHex(byte[][] pts) => pts.Select(p => Convert.ToHexString(p).ToLowerInvariant()).ToArray();
    private static byte[][] PtsFrom(JsonElement arr) => arr.EnumerateArray().Select(e => Convert.FromHexString(e.GetString()!)).ToArray();
    private static Dictionary<string, string> ScalarMap(IEnumerable<int> positions, byte[][] d)
        => positions.ToDictionary(p => p.ToString(), p => Convert.ToHexString(d[p]).ToLowerInvariant());

    private void Tick()
    {
        try
        {
            lock (_gate)
            {
                Send(new { t = "hello", pub = _myPubHex });
                if (SeatPubs.Length != 2) { TryAssignSeats(); return; }
                DriveDeal();
                DriveStreet();
                Broadcast();   // all periodic sending happens here (once per tick) to avoid a message storm
            }
        }
        catch { }
    }

    // Send my current outstanding artifacts once per tick. Deck stages (the large messages) stop once the
    // hand exists; the small holeD keeps flowing so a peer that is slightly behind can still finish its deal.
    private void Broadcast()
    {
        if (Hand == null)
        {
            if (MySeat == 0) { if (_s1 != null) Send(new { t = "s1", pts = PtsHex(_s1) }); if (_r1 != null) Send(new { t = "r1", pts = PtsHex(_r1) }); }
            else { if (_s2 != null) Send(new { t = "s2", pts = PtsHex(_s2) }); if (_final != null) Send(new { t = "final", pts = PtsHex(_final) }); }
        }
        if (_final != null) Send(new { t = "holeD", seat = MySeat, d = ScalarMap(HolePositions(OppSeat), _ecPerCard) });
        if (Hand is { AwaitingBoard: true })
        {
            var positions = Enumerable.Range(BoardStart + Hand.Board.Count, Hand.PendingBoardCount).ToList();
            Send(new { t = "boardD", seat = MySeat, key = Hand.Board.Count, d = ScalarMap(positions, _ecPerCard) });
        }
        if (Hand is { AwaitingShowdown: true }) Send(new { t = "showD", seat = MySeat, d = ScalarMap(HolePositions(MySeat), _ecPerCard) });
    }

    private void TryAssignSeats()
    {
        if (SeatPubs.Length == 2) return;
        if (_players.Count < 2) { Status = "Waiting for an opponent to join…"; return; }
        var two = _players.Keys.OrderBy(x => x, StringComparer.Ordinal).Take(2).ToArray();
        SeatPubs = two;
        MySeat = Array.IndexOf(two, _myPubHex);
        State = Phase.Dealing;
        Status = $"Opponent found — you are seat {MySeat}. Shuffling the deck (encrypted)…";
        Raise();
    }

    // Seat 0 = A (produces s1, r1); seat 1 = B (produces s2, final). Each step fires once its input exists;
    // messages are re-sent each tick until the next stage appears, so a dropped frame self-heals.
    private void DriveDeal()
    {
        if (Hand != null) return;
        if (MySeat == 0) // produces stage 1 (shuffle) and stage 3 (re-mask)
        {
            _s1 ??= MentalPokerEC.ShuffleMask(MentalPokerEC.BaseDeck(_n), _ecGlobal, _ecPerm);
            if (_r1 == null && _s2 != null) _r1 = MentalPokerEC.Remask(_s2, _ecGlobal, _ecPerCard);
        }
        else // seat 1 produces stage 2 (shuffle) and stage 4 (final re-mask)
        {
            if (_s2 == null && _s1 != null) _s2 = MentalPokerEC.ShuffleMask(_s1, _ecGlobal, _ecPerm);
            if (_final == null && _r1 != null) _final = MentalPokerEC.Remask(_r1, _ecGlobal, _ecPerCard);
        }
        TryCreateHand();
    }

    private void TryCreateHand()
    {
        if (Hand != null || _final == null) return;
        // need the opponent's masks for MY hole positions to unmask my own cards
        if (!HolePositions(MySeat).All(_oppHoleD.ContainsKey)) return;

        var deck = new Card[HoleCount * 2];
        for (int i = 0; i < deck.Length; i++) deck[i] = Card.FaceDown; // opponent holes stay face-down
        foreach (var p in HolePositions(MySeat))
        {
            var m = MentalPokerEC.Unmask(_final[p], new[] { _oppHoleD[p], _ecPerCard[p] });
            int idx = MentalPokerEC.Identify(m, _n);
            if (idx < 0) { Status = "deal verification failed (a hole card did not decode) — refusing"; State = Phase.Done; Raise(); return; }
            deck[p] = _cardSet[idx];
        }
        Hand = HoldemState.Create(new long[] { 100, 100 }, button: 0, sb: 1, bb: 2, deck, Variant, deferShowdown: true);
        State = Phase.Playing;
        Status = "Dealt. " + Hand.Message;
        Raise();
    }

    // Consume the per-street board reveal and the showdown hole reveal as the engine pauses for them.
    // (Sending the reveals is done in Broadcast; this only unmasks once both peers' masks are in.)
    private void DriveStreet()
    {
        if (Hand == null) return;
        if (Hand.AwaitingBoard)
        {
            int already = Hand.Board.Count;
            var positions = Enumerable.Range(BoardStart + already, Hand.PendingBoardCount).ToList();
            int key = already; // distinct per street by how many board cards already dealt (0,3,4)
            if (_boardD.TryGetValue(MySeat, out var mine) && _boardD.TryGetValue(OppSeat, out var theirs)
                && positions.All(p => mine.ContainsKey(p) && theirs.ContainsKey(p)) && _boardStreetsSupplied.Add(key))
            {
                var cards = new List<Card>();
                foreach (var p in positions)
                {
                    var m = MentalPokerEC.Unmask(_final![p], new[] { mine[p], theirs[p] });
                    cards.Add(_cardSet[MentalPokerEC.Identify(m, _n)]);
                }
                Hand.SupplyBoard(cards);
                Status = Hand.Message;
                Raise();
                DriveStreet(); // a forced all-in runout may immediately need the next street/showdown
            }
        }
        else if (Hand.AwaitingShowdown)
        {
            // unmask the opponent's hole cards once they reveal their masks at their own positions
            if (HolePositions(OppSeat).All(_oppShowD.ContainsKey))
            {
                var cards = new List<Card>();
                foreach (var p in HolePositions(OppSeat))
                {
                    var m = MentalPokerEC.Unmask(_final![p], new[] { _oppShowD[p], _ecPerCard[p] });
                    cards.Add(_cardSet[MentalPokerEC.Identify(m, _n)]);
                }
                Hand.SetRevealedHole(OppSeat, cards);
                Hand.CompleteShowdown();
                State = Phase.Done;
                Status = Hand.Message;
                Raise();
            }
        }
    }

    private void OnMessage(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            switch (root.GetProperty("t").GetString())
            {
                case "hello":
                    var pub = root.GetProperty("pub").GetString();
                    if (pub != null && _players.TryAdd(pub, 1)) { lock (_gate) TryAssignSeats(); }
                    break;
                case "s1": lock (_gate) { _s1 ??= PtsFrom(root.GetProperty("pts")); DriveDeal(); } break;
                case "s2": lock (_gate) { _s2 ??= PtsFrom(root.GetProperty("pts")); DriveDeal(); } break;
                case "r1": lock (_gate) { _r1 ??= PtsFrom(root.GetProperty("pts")); DriveDeal(); } break;
                case "final": lock (_gate) { _final ??= PtsFrom(root.GetProperty("pts")); DriveDeal(); } break;
                case "holeD": lock (_gate) { if (root.GetProperty("seat").GetInt32() == OppSeat) { MergeScalars(_oppHoleD, root.GetProperty("d")); DriveDeal(); } } break;
                case "showD": lock (_gate) { if (root.GetProperty("seat").GetInt32() == OppSeat) { MergeScalars(_oppShowD, root.GetProperty("d")); DriveStreet(); } } break;
                case "boardD":
                    lock (_gate)
                    {
                        int seat = root.GetProperty("seat").GetInt32();
                        if (!_boardD.TryGetValue(seat, out var m)) { m = new(); _boardD[seat] = m; }
                        MergeScalars(m, root.GetProperty("d"));
                        DriveStreet();
                    }
                    break;
                case "act": lock (_gate) ApplyRemote(root); break;
            }
        }
        catch { }
    }

    private static void MergeScalars(Dictionary<int, byte[]> into, JsonElement obj)
    {
        foreach (var kv in obj.EnumerateObject()) into[int.Parse(kv.Name)] = Convert.FromHexString(kv.Value.GetString()!);
    }

    private int _applied;
    private void ApplyRemote(JsonElement root)
    {
        if (Hand == null || Hand.Complete) return;
        if (root.GetProperty("seq").GetInt32() != _applied) return;
        int seat = root.GetProperty("seat").GetInt32();
        if (seat != Hand.ToAct) return;
        var kind = Enum.Parse<ActionKind>(root.GetProperty("kind").GetString()!);
        long amt = root.GetProperty("amount").GetInt64();
        try { Hand.Apply(new GameAction(kind, seat, amt)); _applied++; Status = Hand.Message; if (Hand.Complete) State = Phase.Done; Raise(); DriveStreet(); }
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
            DriveStreet();
        }
    }

    private void Raise() { try { OnUpdate?.Invoke(); } catch { } }
}

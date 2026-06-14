using BsvPoker.Core;
using BsvPoker.Crypto;
using BsvPoker.Net;
using BsvPoker.Net.Bsv;

namespace BsvPoker.App;

/// <summary>
/// A bot is a SEPARATE automated player — its OWN identity (derived from its owner via Type-42), OWN wallet,
/// OWN always-online SPV node (TxLink), and OWN entry on the poker gossip overlay/chat. It is not a hot-seat
/// clone and not a second copy of the app. When the owner plays it, the bot takes a REAL second seat in a
/// <c>NetGame</c> — the same secure dealerless mental-poker protocol as any networked table — driven by the
/// host app (see <c>MainWindow.StartBotNetGame</c>), auto-acting on its turn. The bot is funded with real coins
/// (an SPV envelope) and on close it refunds everything to its funder.
/// </summary>
public sealed class BotPlayer : IDisposable
{
    public const long LiveStake = 20_000;

    private readonly NetworkParams _net;
    private readonly byte[] _seed;
    private readonly OnChainWallet _wallet;
    private readonly TxLink _link;
    private readonly object _lock = new();
    private long _balance;                 // confirmed coin held by the bot (SPV-verified envelopes only)
    private string? _funderAddress;        // where to refund on close

    public byte[] Priv { get; }
    public byte[] Pub { get; }
    public PokerGossip Gossip { get; }
    public string PubHex => Convert.ToHexString(Pub).ToLowerInvariant();
    public string Endpoint { get; }
    public long Balance { get { lock (_lock) return _balance; } }
    public event Action<string>? OnLog;
    /// <summary>The bot has NO miner connection of its own; when it must settle a tx (e.g. the close-out refund)
    /// it raises this so the HOST (the owner's app) broadcasts it to miners + peers. Wired in PlayBot.</summary>
    public event Action<byte[]>? OnBroadcast;

    private readonly byte[] _ownerPub;     // the bot belongs to this identity and plays ONLY this identity
    public string Name { get; }            // e.g. "Alice-Bot-001"

    /// <param name="ownerPriv">the owner's identity private key — the bot's key is DERIVED from it (Type-42), so
    /// only the owner can create/control this bot.</param>
    /// <param name="ownerPub">the owner's identity public key — the bot will ONLY play this identity.</param>
    /// <param name="index">the bot number for this owner (Alice-Bot-001, -002, …).</param>
    /// <param name="ownerHandle">the owner's handle for naming.</param>
    public BotPlayer(NetworkParams net, string localIp, byte[] ownerPriv, byte[] ownerPub, int index, string ownerHandle)
    {
        _net = net;
        _ownerPub = ownerPub;
        Name = $"{(string.IsNullOrWhiteSpace(ownerHandle) ? "Owner" : ownerHandle)}-Bot-{index:D3}";
        // the bot's seed is DERIVED from the owner's identity via Type-42 — provably the owner's bot, and only the
        // owner (who holds ownerPriv) can ever derive/run it. A different owner cannot reproduce this key.
        _seed = Type42.UniqueKey(ownerPriv, $"bsvpoker/bot/{index}");
        var k = WalletKeys.Account(_seed, 0, 0);
        Priv = k.Priv; Pub = k.Pub;
        _wallet = new OnChainWallet(_seed);
        _link = new TxLink(net, 0);
        _link.OnTransaction += Ingest;
        _link.Start();
        Endpoint = $"{localIp}:{_link.Port}";
        Gossip = new PokerGossip(PubHex, Endpoint, (peerPub, endpoint, msg) => SendChat(peerPub, endpoint, "GOSSIP:" + msg));
        Gossip.OnPeersChanged += () => OnLog?.Invoke($"discovered {Gossip.Peers.Count} peer(s)");
        Log($"{Name} online — plays ONLY its owner {Convert.ToHexString(_ownerPub).ToLowerInvariant()[..12]}…  @ {Endpoint}");
    }

    public string ReceiveAddress()
    {
        var payload = new byte[21]; payload[0] = _net.AddressVersion; Hashes.Hash160(Pub).CopyTo(payload, 1);
        return Base58.CheckEncode(payload);
    }

    /// <summary>Fund the bot with a real coin via an SPV envelope (verified before crediting). Records the funder for refund.</summary>
    public bool ImportFunding(SpvEnvelope env, string funderAddress)
    {
        if (!env.Verify()) { Log("funding rejected — SPV envelope did not verify"); return false; }
        var tx = env.ParseTx(); if (tx == null) return false;
        var lockMe = Chain.P2pkhLock(Hashes.Hash160(Pub));
        bool credited = false;
        for (int v = 0; v < tx.Outs.Count; v++)
            if (tx.Outs[v].Script.AsSpan().SequenceEqual(lockMe))
            {
                lock (_lock) { _wallet.Add(new OnChainWallet.Utxo(Chain.Txid(tx), (uint)v, tx.Outs[v].Value, 0, 0)); _balance += tx.Outs[v].Value; }
                credited = true;
            }
        if (credited) { _funderAddress = funderAddress; Log($"funded — balance {Balance:N0} sat (will refund {funderAddress} on close)"); }
        else Log("funding envelope does not pay the bot");
        return credited;
    }

    /// <summary>ONE-CLICK funding for the owner's OWN bot (same process): credit the bot directly from the raw
    /// funding transaction the owner's wallet just built and broadcast — no SPV-envelope cut-and-paste, no waiting
    /// for confirmation. Trust is implicit (the bot is derived from the owner's key and runs in the owner's app).
    /// On close the whole balance is refunded to <paramref name="funderAddress"/>, so the money is never lost.</summary>
    public bool CreditRaw(Chain.Tx tx, string funderAddress)
    {
        var lockMe = Chain.P2pkhLock(Hashes.Hash160(Pub));
        bool credited = false;
        for (int v = 0; v < tx.Outs.Count; v++)
            if (tx.Outs[v].Script.AsSpan().SequenceEqual(lockMe))
            {
                lock (_lock) { _wallet.Add(new OnChainWallet.Utxo(Chain.Txid(tx), (uint)v, tx.Outs[v].Value, 0, 0)); _balance += tx.Outs[v].Value; }
                credited = true;
            }
        if (credited) { _funderAddress = funderAddress; Log($"funded (one-click) — balance {Balance:N0} sat (refunds {funderAddress} on close)"); }
        return credited;
    }

    /// <summary>Announce on the overlay (so the human finds the bot) and pull peers it knows.</summary>
    public void Announce() { Gossip.Announce(); Gossip.Query(); }
    public void AddPeer(string pubHex, string endpoint) => Gossip.AddSeed(pubHex, endpoint);

    private void Ingest(Chain.Tx tx)
    {
        // GROUP message (broadcast encryption): the bot reads it iff it was selected as a member. Show it so
        // group chat is visibly delivered to the bot; skip its own messages.
        var grp = OnChainChat.TryReadGroupTx(tx, Priv, Pub);
        if (grp != null)
        {
            if (!grp.SenderPub.AsSpan().SequenceEqual(Pub))
                Log($"[group] {Convert.ToHexString(grp.SenderPub).ToLowerInvariant()[..12]}…: {grp.Text}");
            return;
        }
        var msg = OnChainChat.TryReadTx(tx, Priv, Pub);
        if (msg == null) return;
        var senderHex = Convert.ToHexString(msg.SenderPub).ToLowerInvariant();
        if (msg.Text.StartsWith("GOSSIP:", StringComparison.Ordinal)) { Gossip.Receive(msg.Text["GOSSIP:".Length..]); return; }
        // the bot only converses with its OWNER; it replies (a real funded chat tx) so chat is visibly two-way
        if (!msg.SenderPub.AsSpan().SequenceEqual(_ownerPub)) { Log($"ignored a chat from a non-owner {senderHex[..12]}…"); return; }
        Log($"owner: {msg.Text}");
        var peer = Gossip.Peers.FirstOrDefault(p => p.PubHex == senderHex);
        if (peer != null)
        {
            var reply = $"🤖 {Name}: got \"{(msg.Text.Length > 40 ? msg.Text[..40] + "…" : msg.Text)}\"" + (Balance < 1500 ? " (fund me to chat/play)" : "");
            var err = SendChat(senderHex, peer.Endpoint, reply);
            if (err.Length > 0) Log("reply not sent: " + err);
        }
    }

    // The bot plays as a real second NetGame seat (driven by the host app in MainWindow.StartBotNetGame) — the
    // same secure dealerless protocol as any networked table. The old LiveHand/LiveDeal two-party path was removed.

    private string SendChat(string recipientPubHex, string endpoint, string text)
    {
        try
        {
            var rpub = Convert.FromHexString(recipientPubHex);
            var script = OnChainChat.BuildScript(rpub, Priv, Pub, (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), text);
            byte[] raw;
            lock (_lock)
            {
                if (_wallet.Balance < 2) return "bot has no sats to send a message";   // ~1-sat per-tx mandate
                var spend = _wallet.SpendAction(script, 1, 1);   // 1-sat message output + 1-sat fee
                raw = Chain.Serialize(spend.Tx);
                var (host, port) = ParseHostPort(endpoint);
                if (host != null) _ = TxLink.SendTxAsync(_net, host, port, raw);
            }
            // EVERYTHING ON-CHAIN: a real chat reply or game move (DEAL:) must be MINED, not just pushed IP-to-IP —
            // the bot has no miner connection, so the host broadcasts it. Gossip is overlay plumbing → not mined.
            if (!text.StartsWith("GOSSIP:", StringComparison.Ordinal)) { try { OnBroadcast?.Invoke(raw); } catch { } }
            return "";
        }
        catch (Exception ex) { return ex.Message; }
    }

    /// <summary>Refund every remaining sat to the funder (bots leave no money behind).</summary>
    public void RefundFunder()
    {
        try
        {
            if (_funderAddress == null) return;
            var payload = Base58.CheckDecode(_funderAddress);
            byte[]? raw = null; long amount = 0;
            lock (_lock)
            {
                if (_wallet.Balance <= 1000) return;
                amount = _wallet.Balance - 1000;
                var spend = _wallet.SpendAction(Chain.P2pkhLock(payload[1..]), amount, 1000);   // marks the coins spent
                raw = Chain.Serialize(spend.Tx);
                _balance = _wallet.Balance;
            }
            // The bot has no miner connection — the host (owner's app) broadcasts to miners + peers, AND we push it
            // IP-to-IP to the owner so it settles even if the host event isn't wired. The money goes back to the user.
            Log($"refunding {amount:N0} sat to the funder {_funderAddress}");
            try { OnBroadcast?.Invoke(raw); } catch { }
            var ownerHex = Convert.ToHexString(_ownerPub).ToLowerInvariant();
            var ownerPeer = Gossip.Peers.FirstOrDefault(p => string.Equals(p.PubHex, ownerHex, StringComparison.OrdinalIgnoreCase));
            if (ownerPeer != null) { var (host, port) = ParseHostPort(ownerPeer.Endpoint); if (host != null) _ = TxLink.SendTxAsync(_net, host, port, raw); }
        }
        catch (Exception ex) { Log("refund failed: " + ex.Message); }
    }

    private static (string? Host, int Port) ParseHostPort(string s)
    {
        var i = s.LastIndexOf(':');
        if (i <= 0 || !int.TryParse(s[(i + 1)..], out var p) || p <= 0) return (null, 0);
        return (s[..i], p);
    }

    private void Log(string m) => OnLog?.Invoke(m);
    public void Dispose() { try { RefundFunder(); } catch { } try { _link.Dispose(); } catch { } }
}

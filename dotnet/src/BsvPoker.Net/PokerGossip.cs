namespace BsvPoker.Net;

/// <summary>
/// The POKER gossip + query overlay (on top of the Bitcoin network) by which poker nodes find each other —
/// other players, chat partners, and tables — without any central directory. It is a poker-specific overlay
/// (namespace tag "PKRGOSSIP1"), NOT the Bitcoin gossip protocol. Each always-online node:
///   • ANNOUNCEs itself (pubkey + IP endpoint) to the peers it knows,
///   • FORWARDS announcements it has not seen before (flood with de-duplication) so they propagate across the
///     whole overlay — a node two hops away still learns of you,
///   • answers a QUERY with the peers it knows (PEERS), and merges peers learned from others.
/// The transport is supplied by the host (each message is delivered as a Bitcoin transaction IP-to-IP); this
/// class is the gossip LOGIC and is transport-agnostic and unit-testable.
/// </summary>
public sealed class PokerGossip
{
    public const string Tag = "PKRGOSSIP1";
    public sealed record Peer(string PubHex, string Endpoint, long LastSeenUnix);

    private readonly string _myPub;
    private readonly string _myEndpoint;
    private readonly Action<string, string, string> _send;   // (peerPubHex, peerEndpoint, message)
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Peer> _peers = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _seen = new();

    /// <summary>Raised when the known-peer set changes (so the UI can refresh the player list).</summary>
    public event Action? OnPeersChanged;

    public PokerGossip(string myPubHex, string myEndpoint, Action<string, string, string> send)
    { _myPub = myPubHex; _myEndpoint = myEndpoint; _send = send; }

    public IReadOnlyList<Peer> Peers => _peers.Values.ToList();

    /// <summary>Seed the overlay with a peer we already know (e.g. learned earlier or entered once).</summary>
    public void AddSeed(string pubHex, string endpoint) => Upsert(pubHex, endpoint);

    /// <summary>Announce ourselves to every peer we know; they forward it onward across the overlay.</summary>
    public void Announce()
    {
        var nonce = Guid.NewGuid().ToString("N");
        _seen.TryAdd(nonce, 1);
        var msg = $"{Tag}|ANNOUNCE|{_myPub}|{_myEndpoint}|{nonce}";
        foreach (var p in _peers.Values) _send(p.PubHex, p.Endpoint, msg);
    }

    /// <summary>Ask every known peer for the peers THEY know (overlay discovery pull).</summary>
    public void Query()
    {
        var msg = $"{Tag}|QUERY|{_myPub}|{_myEndpoint}";
        foreach (var p in _peers.Values) _send(p.PubHex, p.Endpoint, msg);
    }

    /// <summary>Process an inbound gossip message (the host calls this when a gossip-bearing tx arrives).</summary>
    public void Receive(string msg)
    {
        var p = msg.Split('|');
        if (p.Length < 2 || p[0] != Tag) return;
        switch (p[1])
        {
            case "ANNOUNCE" when p.Length >= 5:
            {
                string pub = p[2], ep = p[3], nonce = p[4];
                if (pub == _myPub) return;
                Upsert(pub, ep);
                if (_seen.TryAdd(nonce, 1))                       // first time we've seen this announce → forward it
                    foreach (var peer in _peers.Values)
                        if (peer.PubHex != pub) _send(peer.PubHex, peer.Endpoint, msg);
                break;
            }
            case "QUERY" when p.Length >= 4:
            {
                string pub = p[2], ep = p[3];
                Upsert(pub, ep);
                var list = string.Join(";", _peers.Values.Where(x => x.PubHex != pub).Select(x => x.PubHex + "," + x.Endpoint));
                _send(pub, ep, $"{Tag}|PEERS|{list}");
                break;
            }
            case "PEERS" when p.Length >= 3:
            {
                foreach (var e in p[2].Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    var kv = e.Split(',');
                    if (kv.Length == 2) Upsert(kv[0], kv[1]);
                }
                break;
            }
        }
    }

    private void Upsert(string pub, string endpoint)
    {
        if (pub == _myPub || string.IsNullOrEmpty(pub) || string.IsNullOrEmpty(endpoint)) return;
        bool isNew = !_peers.ContainsKey(pub);
        _peers[pub] = new Peer(pub, endpoint, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        if (isNew) OnPeersChanged?.Invoke();
    }
}

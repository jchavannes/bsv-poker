namespace BsvPoker.Net;

/// <summary>
/// REDUNDANT DUAL-PATH delivery for every on-chain move (directive 20260612-102704): when a player produces or
/// relays a move transaction, it is sent BOTH (1) IP-to-IP directly to every known peer AND (2) to the BSV
/// nodes/miners. EVERY participant does this for every move, so delivery is fully redundant — there is no
/// single relay, no server, and a conflicting (double-spend) move cannot win a race because the agreed move is
/// pushed by everyone on both paths at once. The two sinks are injected so this is testable without sockets and
/// so the app can wire the real <c>TxLink.SendTxAsync</c> (peer IP-to-IP) and node <c>Broadcast</c> (miners).
/// </summary>
public sealed class RedundantMoveBroadcast
{
    private readonly Func<IReadOnlyList<(string Host, int Port)>> _peers;
    private readonly Func<string, int, byte[], Task> _sendToPeer;   // path 1: IP-to-IP, straight to each peer
    private readonly Action<byte[]> _sendToNodes;                   // path 2: to the BSV nodes/miners

    public RedundantMoveBroadcast(
        Func<IReadOnlyList<(string Host, int Port)>> peers,
        Func<string, int, byte[], Task> sendToPeer,
        Action<byte[]> sendToNodes)
    {
        _peers = peers; _sendToPeer = sendToPeer; _sendToNodes = sendToNodes;
    }

    /// <summary>How many deliveries the last broadcast attempted (peers + 1 node push).</summary>
    public int LastFanout { get; private set; }

    /// <summary>Send a raw move transaction on BOTH paths to ALL peers + the nodes. Returns the fan-out count.
    /// A failure to reach one peer never blocks the others or the node push (redundancy is the point).</summary>
    public async Task<int> Broadcast(byte[] rawTx)
    {
        if (rawTx == null || rawTx.Length == 0) throw new ArgumentException("empty transaction");
        int fan = 0;
        // path 1 — IP-to-IP to every known peer (each peer also re-broadcasts to nodes on receipt)
        foreach (var (host, port) in _peers())
        {
            try { await _sendToPeer(host, port, rawTx); fan++; } catch { /* one dead peer never blocks redundancy */ }
        }
        // path 2 — to the BSV nodes/miners
        try { _sendToNodes(rawTx); fan++; } catch { /* node push is best-effort; peers carry it too */ }
        LastFanout = fan;
        return fan;
    }
}

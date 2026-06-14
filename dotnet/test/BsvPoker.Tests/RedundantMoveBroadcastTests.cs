using System.Collections.Concurrent;
using BsvPoker.Net;

namespace BsvPoker.Tests;

/// <summary>
/// Redundant dual-path delivery: every move goes IP-to-IP to EVERY peer AND to the nodes (fully redundant).
/// A dead peer never blocks the rest or the node push.
/// </summary>
public static class RedundantMoveBroadcastTests
{
    public static void All()
    {
        Console.WriteLine("redundant dual-path move delivery (IP-to-IP to all peers AND to nodes):");

        T.Run("a move reaches EVERY peer IP-to-IP AND the nodes (both paths, fully redundant)", () =>
        {
            var peers = new List<(string, int)> { ("10.0.0.1", 9), ("10.0.0.2", 9), ("10.0.0.3", 9) };
            var peerHits = new ConcurrentBag<string>();
            int nodeHits = 0;
            var b = new RedundantMoveBroadcast(() => peers,
                (h, p, tx) => { peerHits.Add($"{h}:{p}"); return Task.CompletedTask; },
                tx => Interlocked.Increment(ref nodeHits));
            var fan = b.Broadcast(new byte[] { 1, 2, 3 }).GetAwaiter().GetResult();
            T.Eq(peerHits.Count, 3, "delivered IP-to-IP to all 3 peers");
            T.Eq(nodeHits, 1, "also broadcast to the nodes");
            T.Eq(fan, 4, "fan-out = peers + nodes (redundant)");
        });

        T.Run("a dead peer never blocks the others or the node push (redundancy holds)", () =>
        {
            var peers = new List<(string, int)> { ("good1", 9), ("dead", 9), ("good2", 9) };
            int good = 0, node = 0;
            var b = new RedundantMoveBroadcast(() => peers,
                (h, p, tx) => { if (h == "dead") throw new Exception("peer offline"); Interlocked.Increment(ref good); return Task.CompletedTask; },
                tx => Interlocked.Increment(ref node));
            var fan = b.Broadcast(new byte[] { 9 }).GetAwaiter().GetResult();
            T.Eq(good, 2, "both reachable peers still got it");
            T.Eq(node, 1, "the node push still happened despite the dead peer");
            T.Eq(fan, 3, "fan-out counts the 2 live peers + nodes");
        });

        T.Run("an empty transaction is rejected", () =>
        {
            var b = new RedundantMoveBroadcast(() => new List<(string, int)>(), (_, _, _) => Task.CompletedTask, _ => { });
            bool threw = false;
            try { b.Broadcast(Array.Empty<byte>()).GetAwaiter().GetResult(); } catch { threw = true; }
            T.True(threw, "empty tx throws");
        });
    }
}

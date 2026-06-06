using BsvPoker.Core;
using BsvPoker.Crypto;
using BsvPoker.Net;

namespace BsvPoker.Tests;

public static class NetGameTests
{
    private static bool Until(Func<bool> c, int ms = 8000)
    {
        var dl = Environment.TickCount64 + ms;
        while (Environment.TickCount64 < dl) { if (c()) return true; Thread.Sleep(30); }
        return c();
    }

    public static void All()
    {
        Console.WriteLine("networked 2-player game (over the P2P mesh, no server):");

        T.Run("two peers deal the SAME dealerless deck and play a hand to the same result (chips conserved)", () =>
        {
            using var nodeA = new P2PNode(0, "127.0.0.1");
            using var nodeB = new P2PNode(0, "127.0.0.1");
            nodeA.StartAsync().Wait();
            nodeB.StartAsync(new[] { new P2PNode.PeerAddr("127.0.0.1", nodeA.BoundPort) }).Wait();
            T.True(Until(() => nodeA.PeerCount >= 1 && nodeB.PeerCount >= 1), "nodes connected");

            var pa = Secp256k1.GenerateKeyPair();
            var pb = Secp256k1.GenerateKeyPair();
            var g1 = new NetGame(nodeA, "table-1", pa.Pub);
            var g2 = new NetGame(nodeB, "table-1", pb.Pub);
            g1.Start(); g2.Start();

            T.True(Until(() => g1.Hand != null && g2.Hand != null), "both dealt");
            // both derived the IDENTICAL deck/deal
            T.Eq(string.Join(",", g1.Hand!.Board.Select(c => c.Index)) + "|" + g1.Hand!.Seats[0].Hole[0].Index,
                 string.Join(",", g2.Hand!.Board.Select(c => c.Index)) + "|" + g2.Hand!.Seats[0].Hole[0].Index,
                 "same deal on both peers");

            // play check/call down to showdown — each peer acts only on its own turn
            int guard = 0;
            while (!(g1.Hand!.Complete) && guard++ < 400)
            {
                foreach (var (g, me) in new[] { (g1, g1.MySeat), (g2, g2.MySeat) })
                {
                    var h = g.Hand!;
                    if (h.Complete || h.ToAct != me) continue;
                    var la = h.Legal();
                    if (la.CanCheck) g.Act(ActionKind.Check, 0);
                    else if (la.CanCall) g.Act(ActionKind.Call, 0);
                    else g.Act(ActionKind.Fold, 0);
                }
                Thread.Sleep(20);
            }
            T.True(Until(() => g1.Hand!.Complete && g2.Hand!.Complete), "both completed");
            T.Eq(g1.Hand!.Seats.Sum(s => s.Stack), 200L, "chips conserved on peer A");
            T.Eq(g2.Hand!.Seats.Sum(s => s.Stack), 200L, "chips conserved on peer B");
            T.Eq(g1.Hand!.Seats[0].Stack, g2.Hand!.Seats[0].Stack, "both peers agree on seat 0 final stack");
            g1.Stop(); g2.Stop();
        });
    }
}

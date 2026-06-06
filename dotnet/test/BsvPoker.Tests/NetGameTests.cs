using BsvPoker.Core;
using BsvPoker.Crypto;
using BsvPoker.Net;

namespace BsvPoker.Tests;

public static class NetGameTests
{
    private static bool Until(Func<bool> c, int ms = 20000)
    {
        var dl = Environment.TickCount64 + ms;
        while (Environment.TickCount64 < dl) { if (c()) return true; Thread.Sleep(30); }
        return c();
    }

    public static void All()
    {
        Console.WriteLine("networked 2-player game (commutative-encryption deal, true hole-card privacy):");

        T.Run("each peer sees ONLY its own hole cards; the board agrees; showdown reveals; chips conserved", () =>
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

            // PRIVACY: each peer knows its own 2 hole cards but sees the opponent's as FACE-DOWN
            foreach (var g in new[] { g1, g2 })
            {
                var mine = g.Hand!.Seats[g.MySeat].Hole;
                var opp = g.Hand!.Seats[1 - g.MySeat].Hole;
                T.True(mine.All(c => !c.IsFaceDown), "my own hole cards are known to me");
                T.True(opp.All(c => c.IsFaceDown), "the opponent's hole cards are hidden from me during play");
            }
            // the two peers do NOT agree on each other's holes yet (each only knows its own)
            T.False(g1.Hand!.Seats[0].Hole[0].IsFaceDown && g2.Hand!.Seats[0].Hole[0].IsFaceDown,
                    "seat 0's holes are known to exactly one peer pre-showdown");

            // play check/call down to showdown — each peer acts only on its own turn; reveals happen automatically
            int guard = 0;
            while (!g1.Hand!.Complete && guard++ < 1500)
            {
                foreach (var g in new[] { g1, g2 })
                {
                    var h = g.Hand!;
                    if (h.Complete || h.ToAct != g.MySeat) continue;
                    var la = h.Legal();
                    if (la.CanCheck) g.Act(ActionKind.Check, 0);
                    else if (la.CanCall) g.Act(ActionKind.Call, 0);
                    else g.Act(ActionKind.Fold, 0);
                }
                Thread.Sleep(15);
            }

            T.True(Until(() => g1.Hand!.Complete && g2.Hand!.Complete), "both completed");
            // after showdown, every hole card is revealed on both peers and they AGREE on them and the board
            T.True(g1.Hand!.Seats.SelectMany(s => s.Hole).All(c => !c.IsFaceDown), "all holes revealed at showdown on A");
            T.True(g2.Hand!.Seats.SelectMany(s => s.Hole).All(c => !c.IsFaceDown), "all holes revealed at showdown on B");
            T.Eq(string.Join(",", g1.Hand!.Board.Select(c => c.Index)),
                 string.Join(",", g2.Hand!.Board.Select(c => c.Index)), "both peers agree on the board");
            T.Eq(string.Join(",", g1.Hand!.Seats[0].Hole.Select(c => c.Index)),
                 string.Join(",", g2.Hand!.Seats[0].Hole.Select(c => c.Index)), "both peers agree on seat 0's revealed holes");
            T.Eq(g1.Hand!.Seats.Sum(s => s.Stack), 200L, "chips conserved on peer A");
            T.Eq(g2.Hand!.Seats.Sum(s => s.Stack), 200L, "chips conserved on peer B");
            T.Eq(g1.Hand!.Seats[0].Stack, g2.Hand!.Seats[0].Stack, "both peers agree on seat 0 final stack");
            g1.Stop(); g2.Stop();
        });
    }
}

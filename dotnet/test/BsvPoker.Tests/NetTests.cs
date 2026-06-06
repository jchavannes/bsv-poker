using System.Text;
using BsvPoker.Net;

namespace BsvPoker.Tests;

public static class NetTests
{
    private static bool Until(Func<bool> cond, int ms = 4000)
    {
        var dl = Environment.TickCount64 + ms;
        while (Environment.TickCount64 < dl) { if (cond()) return true; Thread.Sleep(20); }
        return cond();
    }

    public static void All()
    {
        Console.WriteLine("P2P gossip transport (no server):");

        T.Run("a frame gossips A—B—C to the far peer exactly once", () =>
        {
            using var a = new P2PNode(0, "127.0.0.1");
            using var b = new P2PNode(0, "127.0.0.1");
            using var c = new P2PNode(0, "127.0.0.1");
            a.StartAsync().Wait();
            b.StartAsync(new[] { new P2PNode.PeerAddr("127.0.0.1", a.BoundPort) }).Wait();
            c.StartAsync(new[] { new P2PNode.PeerAddr("127.0.0.1", b.BoundPort) }).Wait();
            T.True(Until(() => a.PeerCount >= 1 && b.PeerCount >= 2 && c.PeerCount >= 1), "mesh A-B-C formed");
            var atC = new List<string>();
            c.Subscribe("tbl", t => { lock (atC) atC.Add(t); });
            Thread.Sleep(100);
            a.PublishAsync("tbl", Encoding.UTF8.GetBytes("hello")).Wait();
            T.True(Until(() => atC.Count > 0), "C received");
            Thread.Sleep(100);
            lock (atC) { T.Eq(atC.Count, 1, "exactly once (dedup)"); T.Eq(atC[0], "hello"); }
        });

        T.Run("a hosted table is discovered across the mesh (serverless directory)", () =>
        {
            using var a = new P2PNode(0, "127.0.0.1");
            using var b = new P2PNode(0, "127.0.0.1");
            a.StartAsync().Wait();
            b.StartAsync(new[] { new P2PNode.PeerAddr("127.0.0.1", a.BoundPort) }).Wait();
            T.True(Until(() => a.PeerCount >= 1 && b.PeerCount >= 1), "connected");
            a.CreateTableAsync("t1", "Friday").Wait();
            T.True(Until(() => b.ListTables().Any(x => x.id == "t1")), "B discovered A's table");
        });
    }
}

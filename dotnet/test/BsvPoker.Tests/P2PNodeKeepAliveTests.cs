using System.Text;
using BsvPoker.Crypto;
using BsvPoker.Net;

namespace BsvPoker.Tests;

/// <summary>
/// CONNECTION LIVENESS on the gossip mesh — the robustness that makes cross-device play survive the real world.
/// A TCP link through a NAT/firewall, or to a laptop that sleeps or is unplugged, can go HALF-OPEN: still
/// "connected" but never delivering another byte. Without liveness the read loop blocks forever, the peer slot
/// is never freed, and the dual-path redundancy silently has a dead path. These tests prove (1) the periodic
/// bare-newline keepalive is a true no-op (never surfaces as a spurious message and never corrupts framing),
/// and (2) a stale/half-open peer is reaped, freeing its slot so discovery can reconnect.
/// </summary>
public static class P2PNodeKeepAliveTests
{
    private static bool Until(Func<bool> c, int ms) { var dl = Environment.TickCount64 + ms; while (Environment.TickCount64 < dl) { if (c()) return true; Thread.Sleep(20); } return c(); }

    public static void All()
    {
        Console.WriteLine("P2P connection liveness (keepalive no-op + half-open reaping):");

        T.Run("keepalive newlines are a NO-OP: no spurious message, framing stays intact for real publishes", () =>
        {
            var a = new P2PNode(0, "127.0.0.1"); a.StartAsync().Wait();
            var b = new P2PNode(0, "127.0.0.1"); b.StartAsync(new[] { new P2PNode.PeerAddr("127.0.0.1", a.BoundPort) }).Wait();
            try
            {
                T.True(Until(() => a.PeerCount >= 1 && b.PeerCount >= 1, 10000), "the two nodes connect");

                int got = 0; string? last = null;
                b.Subscribe("t/live", m => { Interlocked.Increment(ref got); last = m; });

                // hammer keepalives on BOTH directions, interleaved with a real publish
                for (int i = 0; i < 5; i++) { a.SendKeepAlives(); b.SendKeepAlives(); Thread.Sleep(10); }
                a.PublishAsync("t/live", Encoding.UTF8.GetBytes("hello-after-keepalive")).Wait();
                for (int i = 0; i < 5; i++) { a.SendKeepAlives(); b.SendKeepAlives(); Thread.Sleep(10); }

                T.True(Until(() => got >= 1, 5000), "the real publish is delivered despite the keepalive traffic");
                T.Eq(last, "hello-after-keepalive", "the message arrives intact (keepalive newlines did not corrupt framing)");
                T.Eq(got, 1, "exactly ONE message delivered — the bare-newline keepalives produced no spurious messages");
                T.True(a.PeerCount >= 1 && b.PeerCount >= 1, "the connection survived the keepalives (live peers are not dropped)");
            }
            finally { a.Dispose(); b.Dispose(); }
        });

        T.Run("a half-open (silent) peer is reaped, freeing the slot; a live peer is never reaped", () =>
        {
            var a = new P2PNode(0, "127.0.0.1"); a.StartAsync().Wait();
            var b = new P2PNode(0, "127.0.0.1"); b.StartAsync(new[] { new P2PNode.PeerAddr("127.0.0.1", a.BoundPort) }).Wait();
            try
            {
                T.True(Until(() => a.PeerCount >= 1, 10000), "the peer connects");

                // a fresh, live peer must NOT be reaped by the idle reaper
                T.Eq(a.ReapIdle(30_000), 0, "a live (just-active) peer is not reaped");
                T.True(a.PeerCount >= 1, "the live peer is still connected");

                // simulate a half-open link: no inbound bytes for a long time, then reap
                a.ForcePeersStale(60_000);
                int reaped = a.ReapIdle(45_000);
                T.True(reaped >= 1, "the stale half-open peer is reaped");
                T.True(Until(() => a.PeerCount == 0, 3000), "the peer slot is freed (so discovery can reconnect)");
            }
            finally { a.Dispose(); b.Dispose(); }
        });
    }
}

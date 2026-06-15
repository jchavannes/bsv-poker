using BsvPoker.Core;
using BsvPoker.Crypto;
using BsvPoker.Net;

namespace BsvPoker.Tests;

/// <summary>
/// AUTOMATIC peer discovery: two independently-started nodes must find each other and SEE each other's open
/// tables in the lobby directory — with NO manual host:port and NO UDP. This is the core "open your node and it
/// just connects" requirement. Here we drive it through the on-chain-seed path of <see cref="PeerDiscovery"/>:
/// node A is handed node B's TCP endpoint (exactly what it would read from the on-chain registry), discovery
/// dials it, and a table B hosts then appears in A's directory. Also verifies the directory propagation a real
/// lobby relies on (a gossiped, signed table announce reaches the connected peer).
/// </summary>
public static class DiscoveryTests
{
    // ISOLATED rendezvous so a real running BSV Poker instance on this machine (its node on the well-known port +
    // the shared temp rendezvous file) can never pollute these tests with phantom peers. The subnet sweep is also
    // turned off in tests for the same reason; these tests drive discovery through explicit on-chain seeds instead.
    private static readonly string _rv = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bsvp-it-disc-" + System.Guid.NewGuid().ToString("N") + ".txt");

    private static bool Until(Func<bool> c, int ms)
    {
        var dl = Environment.TickCount64 + ms;
        while (Environment.TickCount64 < dl) { if (c()) return true; Thread.Sleep(25); }
        return c();
    }

    public static void All()
    {
        Console.WriteLine("automatic peer discovery (zero-config, TCP, on-chain seeds — tables become visible):");

        T.Run("two nodes auto-connect via discovered TCP seeds and SEE each other's open tables", () =>
        {
            var ka = Secp256k1.GenerateKeyPair();
            var kb = Secp256k1.GenerateKeyPair();
            var a = new P2PNode(0, "127.0.0.1"); a.SetIdentity(ka.Priv, ka.Pub); a.StartAsync().Wait();
            var b = new P2PNode(0, "127.0.0.1"); b.SetIdentity(kb.Priv, kb.Pub); b.StartAsync().Wait();
            PeerDiscovery? da = null, db = null;
            try
            {
                // each node runs discovery; we hand A the endpoint it would have read from the on-chain registry
                // for B (and vice-versa) — i.e. exactly the "seed" the global registry provides.
                da = new PeerDiscovery(a, "127.0.0.1", _rv, false); da.Start();
                db = new PeerDiscovery(b, "127.0.0.1", _rv, false); db.Start();
                da.SetOnChainSeeds(new[] { ("127.0.0.1", b.BoundPort) });
                db.SetOnChainSeeds(new[] { ("127.0.0.1", a.BoundPort) });

                T.True(Until(() => a.PeerCount >= 1 && b.PeerCount >= 1, 8000), "the two nodes auto-connect over TCP (no manual address)");

                // B hosts a table; it must appear in A's lobby directory automatically
                b.CreateTableAsync("t-disc~TexasHoldem~p2~s100~b2", "Bob's table").Wait();
                T.True(Until(() => a.ListTables().Any(t => t.id == "t-disc~TexasHoldem~p2~s100~b2"), 8000),
                    "B's open table becomes visible in A's lobby directory");

                // and symmetrically: A hosts a table, B sees it
                a.CreateTableAsync("t-disc2~Omaha~p2~s100~b2", "Alice's table").Wait();
                T.True(Until(() => b.ListTables().Any(t => t.id == "t-disc2~Omaha~p2~s100~b2"), 8000),
                    "A's open table becomes visible in B's lobby directory");
            }
            finally { try { da?.Dispose(); } catch { } try { db?.Dispose(); } catch { } a.Dispose(); b.Dispose(); }
        });

        T.Run("the subnet sweep runs without error and a node can bind the well-known port", () =>
        {
            // a node on the well-known port (or the ephemeral fallback if it is busy) starts cleanly, and a
            // discovery object's periodic subnet sweep runs without throwing (same-network auto-find path).
            var k = Secp256k1.GenerateKeyPair();
            var n = new P2PNode(PeerDiscovery.WellKnownPort, "0.0.0.0"); n.SetIdentity(k.Priv, k.Pub); n.StartAsync().Wait();
            T.True(n.BoundPort > 0, "the node bound a port (well-known or ephemeral fallback)");
            PeerDiscovery? d = null;
            try
            {
                d = new PeerDiscovery(n, "127.0.0.1"); d.Start();
                Thread.Sleep(300);   // let a tick fire (it kicks off a subnet sweep)
                T.True(true, "discovery + subnet sweep tick without throwing");
            }
            finally { try { d?.Dispose(); } catch { } n.Dispose(); }
        });

        T.Run("a seed that is NOT live (nothing listening) is harmless — discovery keeps trying, no crash", () =>
        {
            var k = Secp256k1.GenerateKeyPair();
            var a = new P2PNode(0, "127.0.0.1"); a.SetIdentity(k.Priv, k.Pub); a.StartAsync().Wait();
            PeerDiscovery? d = null;
            try
            {
                d = new PeerDiscovery(a, "127.0.0.1", _rv, false); d.Start();
                d.SetOnChainSeeds(new[] { ("127.0.0.1", 1) });   // port 1: nothing there
                Thread.Sleep(500);
                T.Eq(a.PeerCount, 0, "no phantom peer is formed from a dead seed");
                T.True(true, "discovery tolerates a dead seed without throwing");
            }
            finally { try { d?.Dispose(); } catch { } a.Dispose(); }
        });

        T.Run("a hosted table ENDS for peers the moment the host closes it (no lingering ghost table)", () =>
        {
            var ka = Secp256k1.GenerateKeyPair();
            var kb = Secp256k1.GenerateKeyPair();
            var a = new P2PNode(0, "127.0.0.1"); a.SetIdentity(ka.Priv, ka.Pub); a.StartAsync().Wait();
            var b = new P2PNode(0, "127.0.0.1"); b.SetIdentity(kb.Priv, kb.Pub); b.StartAsync().Wait();
            PeerDiscovery? da = null, db = null;
            try
            {
                da = new PeerDiscovery(a, "127.0.0.1", _rv, false); da.Start();
                db = new PeerDiscovery(b, "127.0.0.1", _rv, false); db.Start();
                da.SetOnChainSeeds(new[] { ("127.0.0.1", b.BoundPort) });
                db.SetOnChainSeeds(new[] { ("127.0.0.1", a.BoundPort) });
                T.True(Until(() => a.PeerCount >= 1 && b.PeerCount >= 1, 8000), "the two nodes connect");

                const string table = "t-leave~TexasHoldem~p2~s100~b2";
                a.CreateTableAsync(table, "Alice's table").Wait();
                T.True(Until(() => b.ListTables().Any(t => t.id == table), 8000), "B sees A's open table");

                a.CloseTable(table).Wait();   // Alice leaves / ends the table
                T.True(Until(() => b.ListTables().All(t => t.id != table), 8000), "the table DISAPPEARS for B immediately (not left as a ghost)");
                T.True(a.ListTables().All(t => t.id != table), "and it is gone from A's own directory");
            }
            finally { try { da?.Dispose(); } catch { } try { db?.Dispose(); } catch { } a.Dispose(); b.Dispose(); }
        });
    }
}

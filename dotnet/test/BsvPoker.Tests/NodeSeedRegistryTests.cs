using BsvPoker.Core;
using BsvPoker.Crypto;

namespace BsvPoker.Tests;

/// <summary>
/// The ON-CHAIN node-seed registry: a well-known BSV address is the global, serverless repository where nodes
/// publish their IP:port + timestamp + expiry as a typed PUSHDATA record (NOT OP_RETURN), with a 1-sat output
/// to the registry address so the record is discoverable by scanning that address. Readers parse the records,
/// drop expired ones, keep the newest per node, and dial the live endpoints over TCP. Positive + hostile.
/// </summary>
public static class NodeSeedRegistryTests
{
    public static void All()
    {
        Console.WriteLine("on-chain node-seed registry (well-known address, IP:port + expiry, no OP_RETURN):");

        T.Run("a record round-trips: build → parse → exact pub/endpoint/timestamp/ttl; live before expiry, dead after", () =>
        {
            var node = Secp256k1.GenerateKeyPair();
            long ts = 1_700_000_000;
            var script = NodeSeedRegistry.BuildRecordOutput(node.Pub, "203.0.113.7:9777", ttlSeconds: 3600, unixTime: ts);
            var seed = NodeSeedRegistry.Parse(script);
            T.True(seed != null, "the record parses");
            T.True(seed!.Pub.SequenceEqual(node.Pub), "node pub read back exactly");
            T.Eq(seed.Endpoint, "203.0.113.7:9777", "endpoint read back exactly");
            T.Eq(seed.UnixTime, ts, "timestamp read back exactly");
            T.Eq(seed.TtlSeconds, 3600, "ttl read back exactly");
            T.Eq(seed.ExpiresAtUnix, ts + 3600, "expiry = timestamp + ttl");
            T.True(seed.IsLiveAt(ts + 3599), "live one second before expiry");
            T.False(seed.IsLiveAt(ts + 3600), "expired at exactly the expiry instant");
            var hp = seed.HostPort();
            T.True(hp is { Host: "203.0.113.7", Port: 9777 }, "endpoint splits into host + port");
        });

        T.Run("NO OP_RETURN: the record output is pushdata-in-script ending in OP_CHECKSIG (owned/spendable)", () =>
        {
            var node = Secp256k1.GenerateKeyPair();
            var script = NodeSeedRegistry.BuildRecordOutput(node.Pub, "10.0.0.5:9700", 600);
            T.False(script[0] == 0x6a, "the record output does NOT start with OP_RETURN (0x6a)");
            T.Eq(script[^1], (byte)0xac, "the record output ends in OP_CHECKSIG (owned/spendable)");
        });

        T.Run("the registry marker is a real 1-SAT P2PKH output paying the well-known address (discoverable)", () =>
        {
            var lock1 = NodeSeedRegistry.RegistryMarkerLock(NodeSeedRegistry.RegistryAddressMainnet);
            T.True(lock1.Length == 25 && lock1[0] == 0x76 && lock1[1] == 0xa9, "a standard P2PKH lock to the registry address");
            T.Eq(NodeSeedRegistry.RegistryMarkerSats, 1L, "the marker output is 1 sat (BSV — not 'dust')");
            // the hash160 in the lock equals the address's hash160
            var h = NodeSeedRegistry.AddressHash160(NodeSeedRegistry.RegistryAddressMainnet);
            T.True(lock1.AsSpan(3, 20).SequenceEqual(h), "the lock pays the exact registry-address hash160");
        });

        T.Run("a full publish TX (1-sat to the registry + the record output) is read back from the transaction", () =>
        {
            var node = Secp256k1.GenerateKeyPair();
            long ts = 1_700_000_500;
            var recordOut = NodeSeedRegistry.BuildRecordOutput(node.Pub, "198.51.100.9:9800", 7200, ts);
            var markerOut = NodeSeedRegistry.RegistryMarkerLock(NodeSeedRegistry.RegistryAddressMainnet);
            var tx = new Chain.Tx(2,
                new() { new(new string('a', 64), 0, System.Array.Empty<byte>(), 0xffffffff) },
                new() { new(NodeSeedRegistry.RegistryMarkerSats, markerOut), new(900, recordOut) }, 0);
            // any scanner of the registry address sees the 1-sat output; reading the tx yields the seed record
            T.True(tx.Outs[0].Value == 1 && tx.Outs[0].Script.AsSpan().SequenceEqual(markerOut), "the tx pays 1 sat to the registry address");
            var seed = NodeSeedRegistry.TryReadTx(tx);
            T.True(seed != null && seed!.Endpoint == "198.51.100.9:9800", "the node seed is read straight from the on-chain tx");
        });

        T.Run("LiveEndpoints: newest record per node wins, expired ones drop, malformed endpoints excluded", () =>
        {
            var n1 = Secp256k1.GenerateKeyPair();
            var n2 = Secp256k1.GenerateKeyPair();
            var n3 = Secp256k1.GenerateKeyPair();
            long now = 2_000_000_000;
            var records = new List<NodeSeedRegistry.Seed>
            {
                Parse(n1.Pub, "1.1.1.1:9001", now - 100, 3600),   // n1 OLD
                Parse(n1.Pub, "2.2.2.2:9002", now - 10,  3600),   // n1 NEW (this one wins)
                Parse(n2.Pub, "3.3.3.3:9003", now - 5000, 3600),  // n2 EXPIRED (now-5000+3600 < now)
                Parse(n3.Pub, "bogus-no-port", now - 10, 3600),   // n3 malformed endpoint → excluded
            };
            var live = NodeSeedRegistry.LiveEndpoints(records, now);
            T.True(live.Any(e => e.Host == "2.2.2.2" && e.Port == 9002), "n1's NEWEST endpoint is live");
            T.False(live.Any(e => e.Host == "1.1.1.1"), "n1's OLDER endpoint is superseded");
            T.False(live.Any(e => e.Host == "3.3.3.3"), "n2's expired record is dropped");
            T.Eq(live.Count, 1, "only the one live, well-formed node remains");
        });

        T.Run("HOSTILE: tampered / non-record scripts return null, never throw", () =>
        {
            var node = Secp256k1.GenerateKeyPair();
            var good = NodeSeedRegistry.BuildRecordOutput(node.Pub, "10.0.0.1:9700", 600);
            // a plain P2PKH is not a record
            T.True(NodeSeedRegistry.Parse(Chain.P2pkhLockForPub(node.Pub)) == null, "a P2PKH output is not a node-seed record");
            // truncated record
            T.True(NodeSeedRegistry.Parse(good[..(good.Length / 2)]) == null, "a truncated record is rejected");
            // a wrong-marker but otherwise shaped output is not ours
            var wrong = (byte[])good.Clone(); wrong[2] ^= 0xff;   // corrupt a marker byte
            T.True(NodeSeedRegistry.Parse(wrong) == null, "a wrong-marker output is not a node-seed record");
            // empty / garbage never throws
            T.True(NodeSeedRegistry.Parse(System.Array.Empty<byte>()) == null, "empty script → null, no throw");
            T.True(NodeSeedRegistry.Parse(new byte[] { 0x6a, 0x01, 0x02 }) == null, "an OP_RETURN-ish script → null, no throw");
        });
    }

    private static NodeSeedRegistry.Seed Parse(byte[] pub, string endpoint, long ts, int ttl)
        => NodeSeedRegistry.Parse(NodeSeedRegistry.BuildRecordOutput(pub, endpoint, ttl, ts))!;
}

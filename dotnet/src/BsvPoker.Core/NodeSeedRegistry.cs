using System.Text;
using BsvPoker.Crypto;

namespace BsvPoker.Core;

/// <summary>
/// ON-CHAIN NODE-SEED REGISTRY — the global, serverless directory of live poker nodes, the way Bitcoin's own
/// network bootstraps from seeds, but recorded ON THE CHAIN so it works locally, on a LAN, and across the whole
/// internet identically. A well-known BSV ADDRESS is used purely as a shared, public REPOSITORY: a node that
/// wants to be reachable publishes a NodeSeed record in a transaction that ALSO pays a 1-SAT output to the
/// registry address; anyone can then scan that address's transaction history, read the records, drop the
/// expired ones, and dial the live node endpoints over TCP. No server, no API, no UDP. (On BSV the marker
/// output is a real 1-sat output — there is no "dust" notion as on BTC.)
///
/// THE STANDARD METHODOLOGY (so any client interoperates):
///  - Registry address (mainnet): <see cref="RegistryAddressMainnet"/>.
///  - A node publishes a transaction with:
///      (a) a 1-SAT output paying the registry address → makes the record discoverable by scanning that address;
///      (b) a TYPED PUSHDATA output (NOT OP_RETURN) carrying the record fields, owned/spendable by the node key:
///          &lt;marker "BSVP:NODE:1"&gt; OP_DROP &lt;pub&gt; OP_DROP &lt;endpoint&gt; OP_DROP
///          &lt;timestamp(8 LE)&gt; OP_DROP &lt;ttlSeconds(4 LE)&gt; OP_DROP &lt;ownerPub&gt; OP_CHECKSIG
///  - <c>endpoint</c> is "host:port" (an IPv4/host name + TCP port). <c>timestamp</c> is unix seconds when the
///    record was made; <c>ttlSeconds</c> is how long it stays valid. A reader treats the record as LIVE while
///    now &lt; timestamp + ttl, and as EXPIRED after that — so stale nodes drop out automatically.
///  - A node RE-PUBLISHES periodically (well before its ttl) to stay listed; readers keep the newest record per
///    node key. This gives "the latest months and how long and an expiry time" directly from the chain.
///
/// This type only BUILDS and PARSES the records (composing existing primitives — typed pushdata + P2PKH dust).
/// The app funds/broadcasts the publish tx and scans the registry address's history via its SPV/node layer.
/// </summary>
public static class NodeSeedRegistry
{
    private const byte OP_0 = 0x00, OP_1NEGATE = 0x4f, OP_DROP = 0x75, OP_PUSHDATA1 = 0x4c, OP_PUSHDATA2 = 0x4d, OP_CHECKSIG = 0xac;

    /// <summary>The marker (frame type) that identifies a node-seed record output.</summary>
    public const string Tag = "BSVP:NODE:1";

    /// <summary>The well-known registry ADDRESS — the shared on-chain repository of node seeds. (Provided by the
    /// operator; any client that uses this address inter-operates.) Mainnet Base58Check P2PKH address.</summary>
    public const string RegistryAddressMainnet = "1DPG6kWbyVaN9T9E6gM7uQCD9SFK8tV5yd";

    /// <summary>The value of the registry marker output: 1 satoshi (BSV — a real 1-sat output, not "dust").</summary>
    public const long RegistryMarkerSats = 1;

    /// <summary>A parsed node-seed record: who (pub), where (endpoint host:port), when (UnixTime), and how long
    /// it is valid (TtlSeconds). <see cref="ExpiresAtUnix"/> and <see cref="IsLiveAt"/> derive the expiry.</summary>
    public sealed record Seed(byte[] Pub, string Endpoint, long UnixTime, int TtlSeconds, byte[] OwnerPub)
    {
        public long ExpiresAtUnix => UnixTime + TtlSeconds;
        public bool IsLiveAt(long nowUnix) => nowUnix < ExpiresAtUnix;
        /// <summary>The endpoint split into (host, port), or null if malformed.</summary>
        public (string Host, int Port)? HostPort()
        {
            int i = Endpoint.LastIndexOf(':');
            if (i <= 0 || !int.TryParse(Endpoint[(i + 1)..], out var port) || port is < 1 or > 65535) return null;
            return (Endpoint[..i], port);
        }
    }

    /// <summary>The 20-byte hash160 inside a Base58Check P2PKH address (drops the 1-byte version prefix).</summary>
    public static byte[] AddressHash160(string address)
    {
        var payload = Base58.CheckDecode(address);
        if (payload.Length != 21) throw new ArgumentException("not a standard P2PKH address");
        return payload[1..];
    }

    /// <summary>The P2PKH locking script paying the registry address — the 1-sat marker output that makes a
    /// publish tx discoverable by scanning the registry address's history.</summary>
    public static byte[] RegistryMarkerLock(string registryAddress) => Chain.P2pkhLock(AddressHash160(registryAddress));

    /// <summary>
    /// Build the TYPED PUSHDATA record output (owned/spendable by the node key). <paramref name="endpoint"/> is
    /// "host:port"; <paramref name="ttlSeconds"/> is how long the record stays live. <paramref name="unixTime"/>
    /// defaults to now. NO OP_RETURN — the record rides pushdata-in-script, exactly like the rest of the protocol.
    /// </summary>
    public static byte[] BuildRecordOutput(byte[] nodePub33, string endpoint, int ttlSeconds, long? unixTime = null)
    {
        if (nodePub33.Length != 33) throw new ArgumentException("node pub must be 33-byte compressed");
        if (string.IsNullOrWhiteSpace(endpoint) || endpoint.Length > 255) throw new ArgumentException("bad endpoint");
        if (ttlSeconds <= 0) throw new ArgumentException("ttl must be positive");
        long ts = unixTime ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var b = new List<byte>();
        PushDrop(b, Encoding.ASCII.GetBytes(Tag));
        PushDrop(b, nodePub33);
        PushDrop(b, Encoding.UTF8.GetBytes(endpoint));
        PushDrop(b, U64(ts));
        PushDrop(b, U32((uint)ttlSeconds));
        Push(b, nodePub33); b.Add(OP_CHECKSIG);   // owned/spendable by the node key
        return b.ToArray();
    }

    /// <summary>Parse a node-seed record from an output script, or null if it is not one (wrong marker/shape).</summary>
    public static Seed? Parse(byte[] script)
    {
        try
        {
            int p = 0;
            var marker = ReadPush(script, ref p); if (marker == null || !Drop(script, ref p)) return null;
            if (Encoding.ASCII.GetString(marker) != Tag) return null;
            var pub = ReadPush(script, ref p); if (pub == null || pub.Length != 33 || !Drop(script, ref p)) return null;
            var ep = ReadPush(script, ref p); if (ep == null || !Drop(script, ref p)) return null;
            var ts = ReadPush(script, ref p); if (ts == null || ts.Length != 8 || !Drop(script, ref p)) return null;
            var ttl = ReadPush(script, ref p); if (ttl == null || ttl.Length != 4 || !Drop(script, ref p)) return null;
            var owner = ReadPush(script, ref p); if (owner == null || owner.Length != 33) return null;
            if (p >= script.Length || script[p++] != OP_CHECKSIG || p != script.Length) return null;
            if (!Secp256k1.IsValidPoint(pub)) return null;
            long t = ReadU64(ts); long ttlS = ReadU32(ttl);
            if (ttlS <= 0 || ttlS > int.MaxValue) return null;
            return new Seed(pub, Encoding.UTF8.GetString(ep), t, (int)ttlS, owner);
        }
        catch { return null; }
    }

    /// <summary>Scan a transaction for a node-seed record (the typed output), or null if it carries none.</summary>
    public static Seed? TryReadTx(Chain.Tx tx)
    {
        foreach (var o in tx.Outs) { var s = Parse(o.Script); if (s != null) return s; }
        return null;
    }

    /// <summary>
    /// Reduce a set of records (e.g. every record found at the registry address) to the CURRENT LIVE node
    /// endpoints: keep only the NEWEST record per node key, drop anything expired at <paramref name="nowUnix"/>,
    /// and return distinct, well-formed (host, port) endpoints to dial. This is the directory a client uses.
    /// </summary>
    public static IReadOnlyList<(string Host, int Port)> LiveEndpoints(IEnumerable<Seed> records, long nowUnix)
    {
        var newestPerNode = records
            .GroupBy(r => Convert.ToHexString(r.Pub))
            .Select(g => g.OrderByDescending(r => r.UnixTime).First())
            .Where(r => r.IsLiveAt(nowUnix));
        var outp = new List<(string, int)>();
        var seen = new HashSet<string>();
        foreach (var r in newestPerNode)
        {
            var hp = r.HostPort();
            if (hp == null) continue;
            var key = $"{hp.Value.Host}:{hp.Value.Port}";
            if (seen.Add(key)) outp.Add(hp.Value);
        }
        return outp;
    }

    // ---- minimal pushdata encode/decode (BSV MINIMALDATA), matching TxTemplates so these outputs spend ----
    private static byte[] U64(long v) { var b = new byte[8]; for (int i = 0; i < 8; i++) b[i] = (byte)(v >> (8 * i)); return b; }
    private static byte[] U32(uint v) { var b = new byte[4]; for (int i = 0; i < 4; i++) b[i] = (byte)(v >> (8 * i)); return b; }
    private static long ReadU64(byte[] b) { long v = 0; for (int i = 0; i < 8; i++) v |= (long)b[i] << (8 * i); return v; }
    private static long ReadU32(byte[] b) { long v = 0; for (int i = 0; i < 4; i++) v |= (long)b[i] << (8 * i); return v; }

    private static void Push(List<byte> b, byte[] d)
    {
        if (d.Length == 0) { b.Add(OP_0); return; }
        if (d.Length == 1 && d[0] >= 1 && d[0] <= 16) { b.Add((byte)(0x50 + d[0])); return; }
        if (d.Length == 1 && d[0] == 0x81) { b.Add(OP_1NEGATE); return; }
        if (d.Length < OP_PUSHDATA1) b.Add((byte)d.Length);
        else if (d.Length <= 0xff) { b.Add(OP_PUSHDATA1); b.Add((byte)d.Length); }
        else { b.Add(OP_PUSHDATA2); b.Add((byte)(d.Length & 0xff)); b.Add((byte)(d.Length >> 8)); }
        b.AddRange(d);
    }
    private static void PushDrop(List<byte> b, byte[] d) { Push(b, d); b.Add(OP_DROP); }
    private static bool Drop(byte[] s, ref int p) { if (p < s.Length && s[p] == OP_DROP) { p++; return true; } return false; }
    private static byte[]? ReadPush(byte[] s, ref int p)
    {
        if (p >= s.Length) return null;
        byte op = s[p++];
        if (op == OP_0) return Array.Empty<byte>();
        if (op == OP_1NEGATE) return new byte[] { 0x81 };
        if (op >= 0x51 && op <= 0x60) return new byte[] { (byte)(op - 0x50) };
        int len;
        if (op < OP_PUSHDATA1) len = op;
        else if (op == OP_PUSHDATA1) { if (p >= s.Length) return null; len = s[p++]; }
        else if (op == OP_PUSHDATA2) { if (p + 2 > s.Length) return null; len = s[p] | s[p + 1] << 8; p += 2; }
        else return null;
        if (len < 0 || p + len > s.Length) return null;
        var d = s[p..(p + len)]; p += len; return d;
    }
}

using System.Text;
using BsvPoker.Crypto;

namespace BsvPoker.Core;

/// <summary>
/// END-TO-END on-chain conveyance of the stealth counterparty point P for <see cref="StealthDistribution"/>.
///
/// The patent (US20240152913A1) broadcasts P (=e·G, group A's funding public key) in an <c>OP_RETURN</c> so
/// the withdrawing group B can derive the one-time secret c = H(d·P) and reconstruct the pool key. This
/// project BANS OP_RETURN, so here P rides in a TYPED PUSHDATA output exactly like every other on-chain datum:
/// <code>&lt;marker&gt; OP_DROP &lt;P&gt; OP_DROP &lt;ownerPub&gt; OP_CHECKSIG</code>
/// — a real, owned, spendable output (the owner is group B's public key Q, so the same coalition that can later
/// spend the pool is the recipient of the announcement). Any peer recognises the marker, reads P back out, and
/// with their own d-shares computes c and the pool key A_pool = Q + c·G with NO off-chain channel.
///
/// The marker is self-contained here (it does not touch the shared <see cref="TxTemplates"/> registry), but the
/// script SHAPE is identical: minimally-encoded pushes, OP_DROP after each datum, a 33-byte owner pubkey, then
/// OP_CHECKSIG. NO output this builds ever begins with OP_RETURN (0x6a) — it is a standard pushdata-in-script.
/// </summary>
public static class StealthConveyance
{
    private const byte OP_0 = 0x00, OP_1NEGATE = 0x4f, OP_DROP = 0x75, OP_PUSHDATA1 = 0x4c, OP_PUSHDATA2 = 0x4d, OP_CHECKSIG = 0xac;

    /// <summary>The frame type tag for a stealth-point announcement (its own type, version 1).</summary>
    public const string Tag = "BSVP:STEALTHP:1";

    /// <summary>The conveyed announcement, parsed back from an on-chain output: the funding point P and the
    /// output's owner (group B's withdrawal key Q, the intended recipient/spender).</summary>
    public sealed record Conveyed(byte[] P, byte[] OwnerPub);

    /// <summary>
    /// Build the typed PUSHDATA output that conveys P on-chain: <c>&lt;marker&gt; OP_DROP &lt;P&gt; OP_DROP
    /// &lt;ownerPub&gt; OP_CHECKSIG</c>. <paramref name="fundingPointP"/> is group A's public key e·G (=P);
    /// <paramref name="ownerPubQ"/> is group B's threshold public key Q (=d·G) — so the output is OWNED and
    /// SPENDABLE by the very coalition that will read P and later spend the pool. Both must be 33-byte
    /// compressed points ON the curve (a junk P would be unusable for ECDH, so it is rejected at build time).
    /// </summary>
    public static byte[] BuildPointOutput(byte[] fundingPointP, byte[] ownerPubQ)
    {
        if (!Secp256k1.IsValidPoint(fundingPointP)) throw new ArgumentException("P must be a 33-byte compressed point on the curve");
        if (!Secp256k1.IsValidPoint(ownerPubQ)) throw new ArgumentException("ownerPub must be a 33-byte compressed point on the curve");
        var b = new List<byte>();
        PushDrop(b, Encoding.ASCII.GetBytes(Tag));
        PushDrop(b, fundingPointP);
        Push(b, ownerPubQ); b.Add(OP_CHECKSIG);
        return b.ToArray();
    }

    /// <summary>
    /// Parse a stealth-point announcement back out of an output script, or null if it is not one (wrong marker,
    /// wrong shape, non-point P, trailing bytes, etc.). Strict: the marker must match, P must be a valid 33-byte
    /// compressed point on the curve, and the script must end exactly at <c>&lt;ownerPub&gt; OP_CHECKSIG</c>.
    /// </summary>
    public static Conveyed? Parse(byte[] script)
    {
        try
        {
            int p = 0;
            var marker = ReadPush(script, ref p); if (marker == null || p >= script.Length || script[p++] != OP_DROP) return null;
            if (Encoding.ASCII.GetString(marker) != Tag) return null;
            var point = ReadPush(script, ref p); if (point == null || p >= script.Length || script[p++] != OP_DROP) return null;
            if (!Secp256k1.IsValidPoint(point)) return null;                 // a tampered/junk P is rejected here
            var owner = ReadPush(script, ref p); if (owner == null) return null;
            if (owner.Length != 33 || p >= script.Length || script[p++] != OP_CHECKSIG || p != script.Length) return null;
            return new Conveyed(point, owner);
        }
        catch { return null; }
    }

    /// <summary>
    /// The recipient's end of the protocol: from an on-chain stealth-point output and group B's own threshold
    /// d-key, derive the one-time secret c = H(d·P), the pool key A_pool = Q + c·G, and the threshold SPEND
    /// handle for (d + c). No off-chain channel — P came entirely from the parsed output. Returns null if the
    /// output is not a valid stealth-point announcement.
    /// </summary>
    public static (byte[] C, byte[] PoolKey, ThresholdEcdsa.Shared SpendKey)? Recover(byte[] outputScript, ThresholdEcdsa.Shared dKey)
    {
        var conv = Parse(outputScript);
        if (conv == null) return null;
        var c = StealthDistribution.SharedSecret(dKey.Shares, conv.P);       // H(d·P), threshold ECDH off the on-chain P
        var poolKey = StealthDistribution.PoolKey(dKey.PublicKey, c);        // A_pool = Q + c·G
        var spendKey = StealthDistribution.PoolSpendKey(dKey, c);            // threshold handle for (d + c)
        return (c, poolKey, spendKey);
    }

    // ---- minimal pushdata encode/decode (BSV MINIMALDATA), matching TxTemplates so these outputs spend ----
    private static void Push(List<byte> b, byte[] d)
    {
        if (d.Length == 0) { b.Add(OP_0); return; }
        if (d.Length == 1 && d[0] >= 1 && d[0] <= 16) { b.Add((byte)(0x50 + d[0])); return; } // OP_1..OP_16
        if (d.Length == 1 && d[0] == 0x81) { b.Add(OP_1NEGATE); return; }
        if (d.Length < OP_PUSHDATA1) b.Add((byte)d.Length);
        else if (d.Length <= 0xff) { b.Add(OP_PUSHDATA1); b.Add((byte)d.Length); }
        else { b.Add(OP_PUSHDATA2); b.Add((byte)(d.Length & 0xff)); b.Add((byte)(d.Length >> 8)); }
        b.AddRange(d);
    }
    private static void PushDrop(List<byte> b, byte[] d) { Push(b, d); b.Add(OP_DROP); }
    private static byte[]? ReadPush(byte[] s, ref int p)
    {
        if (p >= s.Length) return null;
        byte op = s[p++];
        if (op == OP_0) return Array.Empty<byte>();
        if (op == OP_1NEGATE) return new byte[] { 0x81 };
        if (op >= 0x51 && op <= 0x60) return new byte[] { (byte)(op - 0x50) }; // OP_1..OP_16
        int len;
        if (op < OP_PUSHDATA1) len = op;
        else if (op == OP_PUSHDATA1) { if (p >= s.Length) return null; len = s[p++]; }
        else if (op == OP_PUSHDATA2) { if (p + 2 > s.Length) return null; len = s[p] | s[p + 1] << 8; p += 2; }
        else return null;
        if (len < 0 || p + len > s.Length) return null;
        var d = s[p..(p + len)]; p += len; return d;
    }
}

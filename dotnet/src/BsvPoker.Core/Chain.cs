using BsvPoker.Crypto;

namespace BsvPoker.Core;

/// <summary>
/// BSV transactions: model, wire serialization, txid, P2PKH scripts, the BSV FORKID sighash,
/// input signing (secp256k1, low-S, DER + 0x41 hashtype), and a pre-signed nLockTime RECOVERY builder
/// (future locktime + non-final sequence so the locktime binds) — so a player can always reclaim funds.
/// OP_RETURN is never produced. Same model on regtest/testnet/mainnet (network is only an address tag).
/// </summary>
public static class Chain
{
    public sealed record TxIn(string PrevTxid, uint Vout, byte[] ScriptSig, uint Sequence);
    public sealed record TxOut(long Value, byte[] Script);
    public sealed record Tx(uint Version, List<TxIn> Ins, List<TxOut> Outs, uint LockTime);

    private const byte SighashAllForkId = 0x41; // SIGHASH_ALL | SIGHASH_FORKID (BSV)

    // ---- encoding helpers ----
    private static void U32(List<byte> b, uint v) { b.Add((byte)v); b.Add((byte)(v >> 8)); b.Add((byte)(v >> 16)); b.Add((byte)(v >> 24)); }
    private static void U64(List<byte> b, long v) { for (int i = 0; i < 8; i++) b.Add((byte)(v >> (8 * i))); }
    private static void VarInt(List<byte> b, long n)
    {
        if (n < 0xfd) b.Add((byte)n);
        else if (n <= 0xffff) { b.Add(0xfd); b.Add((byte)n); b.Add((byte)(n >> 8)); }
        else if (n <= 0xffffffff) { b.Add(0xfe); U32(b, (uint)n); }
        else { b.Add(0xff); U64(b, n); }
    }
    private static byte[] RevHex(string hex) { var a = Convert.FromHexString(hex); Array.Reverse(a); return a; } // display→internal

    public static byte[] Serialize(Tx tx)
    {
        var b = new List<byte>();
        U32(b, tx.Version);
        VarInt(b, tx.Ins.Count);
        foreach (var i in tx.Ins) { b.AddRange(RevHex(i.PrevTxid)); U32(b, i.Vout); VarInt(b, i.ScriptSig.Length); b.AddRange(i.ScriptSig); U32(b, i.Sequence); }
        VarInt(b, tx.Outs.Count);
        foreach (var o in tx.Outs) { U64(b, o.Value); VarInt(b, o.Script.Length); b.AddRange(o.Script); }
        U32(b, tx.LockTime);
        return b.ToArray();
    }

    /// <summary>Display txid (big-endian hex of sha256d of the serialized tx).</summary>
    public static string Txid(Tx tx) { var h = Hashes.Sha256d(Serialize(tx)); Array.Reverse(h); return Convert.ToHexString(h).ToLowerInvariant(); }

    /// <summary>P2PKH locking script: OP_DUP OP_HASH160 &lt;20&gt; OP_EQUALVERIFY OP_CHECKSIG.</summary>
    public static byte[] P2pkhLock(byte[] hash160) { var b = new List<byte> { 0x76, 0xa9, 0x14 }; b.AddRange(hash160); b.Add(0x88); b.Add(0xac); return b.ToArray(); }
    public static byte[] P2pkhLockForPub(byte[] pub33) => P2pkhLock(Hashes.Hash160(pub33));
    private static byte[] Push(byte[] data) { var b = new List<byte> { (byte)data.Length }; b.AddRange(data); return b.ToArray(); } // small pushes only

    /// <summary>The BSV FORKID sighash digest for input <paramref name="index"/> (SIGHASH_ALL|FORKID).</summary>
    public static byte[] SighashForkId(Tx tx, int index, byte[] scriptCode, long amount)
    {
        var prevouts = new List<byte>(); var seqs = new List<byte>(); var outs = new List<byte>();
        foreach (var i in tx.Ins) { prevouts.AddRange(RevHex(i.PrevTxid)); U32(prevouts, i.Vout); U32(seqs, i.Sequence); }
        foreach (var o in tx.Outs) { U64(outs, o.Value); VarInt(outs, o.Script.Length); outs.AddRange(o.Script); }
        var hashPrevouts = Hashes.Sha256d(prevouts.ToArray());
        var hashSequence = Hashes.Sha256d(seqs.ToArray());
        var hashOutputs = Hashes.Sha256d(outs.ToArray());
        var p = new List<byte>();
        U32(p, tx.Version);
        p.AddRange(hashPrevouts);
        p.AddRange(hashSequence);
        var inp = tx.Ins[index];
        p.AddRange(RevHex(inp.PrevTxid)); U32(p, inp.Vout);
        VarInt(p, scriptCode.Length); p.AddRange(scriptCode);
        U64(p, amount);
        U32(p, inp.Sequence);
        p.AddRange(hashOutputs);
        U32(p, tx.LockTime);
        U32(p, SighashAllForkId);
        return Hashes.Sha256d(p.ToArray());
    }

    /// <summary>Sign input <paramref name="index"/> spending a P2PKH output of <paramref name="pub33"/>; sets the scriptSig.</summary>
    public static Tx SignP2pkhInput(Tx tx, int index, byte[] privSeed, byte[] pub33, long amount)
    {
        var scriptCode = P2pkhLockForPub(pub33);
        var digest = SighashForkId(tx, index, scriptCode, amount);
        var sig = Secp256k1.SignDigest(privSeed, digest);
        var der = Secp256k1.ToDer(sig);
        var sigWithType = der.Concat(new byte[] { SighashAllForkId }).ToArray();
        var scriptSig = Push(sigWithType).Concat(Push(pub33)).ToArray();
        var ins = tx.Ins.ToList();
        ins[index] = ins[index] with { ScriptSig = scriptSig };
        return tx with { Ins = ins };
    }

    /// <summary>Verify a signed P2PKH input's signature against its sighash (the core consensus check).</summary>
    public static bool VerifyP2pkhInput(Tx signed, int index, byte[] pub33, long amount)
    {
        var scriptSig = signed.Ins[index].ScriptSig;
        // parse <sig+type><pub>
        int p = 0; int sigLen = scriptSig[p++]; var sigType = scriptSig.Skip(p).Take(sigLen).ToArray(); p += sigLen;
        // recompute digest with empty scriptSig in the input (the FORKID sighash uses scriptCode, not scriptSig)
        var unsigned = signed with { Ins = signed.Ins.Select((i, k) => k == index ? i with { ScriptSig = Array.Empty<byte>() } : i).ToList() };
        var digest = SighashForkId(unsigned, index, P2pkhLockForPub(pub33), amount);
        var der = sigType[..^1]; // strip hashtype byte
        var compact = DerToCompact(der);
        return compact != null && Secp256k1.VerifyDigest(pub33, digest, compact);
    }

    private static byte[]? DerToCompact(byte[] der)
    {
        try
        {
            int p = 0; if (der[p++] != 0x30) return null; p++; // seq, len
            if (der[p++] != 0x02) return null; int rl = der[p++]; var r = der.Skip(p).Take(rl).ToArray(); p += rl;
            if (der[p++] != 0x02) return null; int sl = der[p++]; var s = der.Skip(p).Take(sl).ToArray();
            byte[] Fix(byte[] v) { v = v.SkipWhile((x, i) => x == 0 && i < v.Length - 1).ToArray(); var o = new byte[32]; Array.Copy(v, 0, o, 32 - v.Length, Math.Min(32, v.Length)); return o; }
            return Fix(r).Concat(Fix(s)).ToArray();
        }
        catch { return null; }
    }

    /// <summary>
    /// Build a pre-signed nLockTime RECOVERY: spend a funded P2PKH outpoint back to the owner, broadcastable
    /// only AFTER <paramref name="lockHeight"/> (non-final sequence so the locktime binds). The owner always
    /// holds this before risking funds, so no satoshi can be stranded.
    /// </summary>
    public static Tx BuildRecovery(string fundingTxid, uint vout, long amount, byte[] ownerSeed, byte[] ownerPub, long fee, uint lockHeight)
    {
        var outputs = new List<TxOut> { new(amount - fee, P2pkhLockForPub(ownerPub)) };
        var ins = new List<TxIn> { new(fundingTxid, vout, Array.Empty<byte>(), 0xfffffffe) }; // non-final → locktime active
        var tx = new Tx(2, ins, outputs, lockHeight);
        return SignP2pkhInput(tx, 0, ownerSeed, ownerPub, amount);
    }
}

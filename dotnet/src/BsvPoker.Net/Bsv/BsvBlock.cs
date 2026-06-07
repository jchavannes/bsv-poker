using BsvPoker.Core;
using BsvPoker.Crypto;

namespace BsvPoker.Net.Bsv;

/// <summary>
/// A full Bitcoin block off the wire: an 80-byte header followed by its transactions. Parsing a block lets a
/// payer recover the ordered txid list of the block that confirmed their funding transaction, so they can
/// build a compact <see cref="PartialMerkleTree"/> (merkleblock) proof for the recipient — the no-server
/// funding path. Parsing also fully validates the block: the recomputed merkle root must equal the header's
/// merkle root, or the block is rejected.
/// </summary>
public static class BsvBlock
{
    public sealed record Parsed(BlockHeader Header, List<Chain.Tx> Txs, List<byte[]> Txids);

    /// <summary>Parse and validate a block payload (header + txs). Throws on truncation or a merkle-root mismatch.</summary>
    public static Parsed Parse(byte[] payload)
    {
        if (payload.Length < 81) throw new ArgumentException("block too short");
        var header = BlockHeader.Parse(payload.AsSpan(0, 80));
        int o = 80;
        ulong nTx = ReadVarInt(payload, ref o);
        if (nTx == 0 || nTx > (ulong)int.MaxValue) throw new ArgumentException("absurd tx count");
        var txs = new List<Chain.Tx>((int)Math.Min(nTx, 4096));
        var txids = new List<byte[]>((int)Math.Min(nTx, 4096));
        for (ulong i = 0; i < nTx; i++)
        {
            var tx = Chain.Deserialize(payload, ref o);
            txs.Add(tx);
            txids.Add(Hashes.Sha256d(Chain.Serialize(tx))); // internal (little-endian) txid
        }
        if (o != payload.Length) throw new ArgumentException("trailing bytes after block");
        if (!MerkleProof.Root(txids).AsSpan().SequenceEqual(header.MerkleRoot))
            throw new ArgumentException("merkle root does not match the header — block rejected");
        return new Parsed(header, txs, txids);
    }

    /// <summary>Serialize a block (header + txs) — the inverse of <see cref="Parse"/>, used to produce test blocks.</summary>
    public static byte[] Serialize(BlockHeader header, IReadOnlyList<Chain.Tx> txs)
    {
        var b = new List<byte>();
        b.AddRange(header.Serialize());
        WriteVarInt(b, (ulong)txs.Count);
        foreach (var tx in txs) b.AddRange(Chain.Serialize(tx));
        return b.ToArray();
    }

    private static ulong ReadVarInt(byte[] b, ref int o) => BsvVersion.ReadVarInt(b, ref o);
    private static void WriteVarInt(List<byte> b, ulong n) => BsvVersion.WriteVarInt(b, n);
}

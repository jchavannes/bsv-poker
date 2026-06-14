using BsvPoker.Core;
using BsvPoker.Crypto;

namespace BsvPoker.Net.Bsv;

/// <summary>
/// REGTEST self-fund — the "find a way" so a brand-new, empty wallet can get its FIRST real coins on the local
/// regtest chain (it has no peers to receive from). It MINES a regtest block (trivial PoW — regtest difficulty
/// is deliberately easy) whose single transaction pays the wallet, and returns the funding transaction + the
/// mined block header + the merkle branch/index. That is a GENUINE SPV-provable coin: the same merkle-proof +
/// proof-of-work path the wallet validates for any mined coin (no fake/optimistic credit). The wallet adds the
/// header to its validated chain and credits the coin via its normal ConfirmFromBlock path. Regtest only —
/// never mainnet/testnet, which have real coins.
/// </summary>
public static class RegtestFunder
{
    /// <summary>A mined regtest funding: the tx paying the wallet, the block header, and the SPV proof pieces.</summary>
    public sealed record Funding(Chain.Tx Tx, uint Vout, long Value, BlockHeader Header, byte[][] Branch, int TxIndex, byte[] LeafTxid);

    /// <summary>Mine a regtest block paying <paramref name="amount"/> sats to <paramref name="payToPub33"/> at vout 0.
    /// <paramref name="prevHash"/> links it onto the wallet's current tip (use 32 zero bytes for the first block).</summary>
    public static Funding Fund(byte[] payToPub33, long amount, byte[] prevHash, uint bits = 0x207fffff)
    {
        if (payToPub33.Length != 33) throw new ArgumentException("pubkey must be 33-byte compressed");
        if (amount <= 0) throw new ArgumentException("amount must be positive");
        if (prevHash.Length != 32) throw new ArgumentException("prevHash must be 32 bytes");
        // A regtest funding tx: a synthetic input (this is the local chain's own issuance) paying the wallet at vout 0.
        var seed = Hashes.Sha256(Concat(payToPub33, BitConverter.GetBytes(amount)));
        var tx = new Chain.Tx(2,
            new() { new(Convert.ToHexString(seed).ToLowerInvariant(), 0xffffffff, Array.Empty<byte>(), 0xffffffff) },
            new() { new(amount, Chain.P2pkhLockForPub(payToPub33)) }, 0);
        var leaf = Hashes.Sha256d(Chain.Serialize(tx));                 // the tx's merkle leaf (=txid bytes)
        var leaves = new List<byte[]> { leaf };
        var root = MerkleProof.Root(leaves);
        var branch = MerkleProof.Branch(leaves, 0);
        var header = MineHeader(prevHash, root, bits);
        return new Funding(tx, 0, amount, header, branch, 0, leaf);
    }

    // Find a nonce whose header meets the (easy) regtest target. At bits=0x207fffff almost every hash qualifies,
    // so this returns within a handful of tries — never a CPU burn.
    private static BlockHeader MineHeader(byte[] prev, byte[] merkleRoot, uint bits)
    {
        for (uint nonce = 1; nonce != 0; nonce++)
        {
            var h = new BlockHeader(1, prev, merkleRoot, 1_700_000_000, bits, nonce);
            if (h.MeetsPow()) return h;
        }
        throw new InvalidOperationException("no valid nonce found");   // unreachable at regtest difficulty
    }

    private static byte[] Concat(byte[] a, byte[] b) { var o = new byte[a.Length + b.Length]; a.CopyTo(o, 0); b.CopyTo(o, a.Length); return o; }
}

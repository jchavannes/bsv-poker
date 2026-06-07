using BsvPoker.Core;
using BsvPoker.Crypto;
using BsvPoker.Net.Bsv;

namespace BsvPoker.Tests;

/// <summary>
/// Transaction wire codec (Serialize ⇄ Deserialize) and the full block parser. Round-trips, strict
/// rejection of truncated/trailing bytes, block merkle-root validation, and the end-to-end no-server
/// funding flow: parse a real block → build a merkleblock for the funding tx from its txids → verify.
/// </summary>
public static class TxCodecTests
{
    private const uint EasyBits = 0x207fffff;

    private static BlockHeader MineHeader(byte[] prev, byte[] root)
    {
        for (uint nonce = 1; ; nonce++)
        {
            var h = new BlockHeader(1, prev, root, 1_700_000_000, EasyBits, nonce);
            if (h.MeetsPow()) return h;
        }
    }

    private static Chain.Tx SampleTx(int seed, byte[] payPub)
        => new(2,
            new() { new(((byte)seed).ToString("x2").PadRight(64, 'a'), (uint)seed, new byte[] { 0x6a, (byte)seed }, 0xfffffffe) },
            new() { new(1000 + seed, Chain.P2pkhLockForPub(payPub)),
                    new(50, Chain.P2pkhLock(new byte[20])) }, (uint)seed);

    public static void All()
    {
        Console.WriteLine("Transaction wire codec + block parser:");
        var k = Secp256k1.GenerateKeyPair();

        T.Run("a transaction round-trips through serialize → deserialize (txid stable)", () =>
        {
            var tx = SampleTx(3, k.Pub);
            var bytes = Chain.Serialize(tx);
            var back = Chain.Deserialize(bytes);
            T.Eq(T.Hex(Chain.Serialize(back)), T.Hex(bytes), "bytes identical after re-serialize");
            T.Eq(Chain.Txid(back), Chain.Txid(tx), "txid stable");
            T.Eq(back.Ins.Count, 1, "inputs"); T.Eq(back.Outs.Count, 2, "outputs");
            T.Eq(back.Outs[0].Value, tx.Outs[0].Value, "value survives");
        });

        T.Run("a multi-input/multi-output transaction round-trips", () =>
        {
            var tx = new Chain.Tx(2,
                new() { new("11".PadRight(64, '0'), 0, new byte[] { 1, 2, 3 }, 0xffffffff),
                        new("22".PadRight(64, '0'), 7, Array.Empty<byte>(), 0xfffffffe) },
                new() { new(70000, Chain.P2pkhLockForPub(k.Pub)),
                        new(123, Chain.P2pkhLock(new byte[20])),
                        new(0, new byte[] { 0x51 }) }, 99);
            var back = Chain.Deserialize(Chain.Serialize(tx));
            T.Eq(T.Hex(Chain.Serialize(back)), T.Hex(Chain.Serialize(tx)), "round-trips exactly");
            T.Eq(back.Ins.Count, 2, "2 inputs"); T.Eq(back.Outs.Count, 3, "3 outputs");
        });

        T.Run("HOSTILE: trailing bytes and truncation are rejected", () =>
        {
            var bytes = Chain.Serialize(SampleTx(5, k.Pub));
            T.Throws(() => Chain.Deserialize(bytes.Append((byte)0).ToArray()), "trailing byte rejected");
            T.Throws(() => Chain.Deserialize(bytes[..^1]), "truncated tx rejected");
            T.Throws(() => Chain.Deserialize(new byte[] { 1, 2 }), "garbage rejected");
        });

        T.Run("a block serializes and parses, validating its merkle root", () =>
        {
            var txs = new List<Chain.Tx> { SampleTx(1, k.Pub), SampleTx(2, k.Pub), SampleTx(3, k.Pub) };
            var txids = txs.Select(t => Hashes.Sha256d(Chain.Serialize(t))).ToList();
            var header = MineHeader(new byte[32], MerkleProof.Root(txids));
            var payload = BsvBlock.Serialize(header, txs);
            var parsed = BsvBlock.Parse(payload);
            T.Eq(parsed.Txs.Count, 3, "3 txs parsed");
            T.Eq(parsed.Header.HashHex(), header.HashHex(), "header survives");
            T.Eq(Chain.Txid(parsed.Txs[2]), Chain.Txid(txs[2]), "tx order preserved");
        });

        T.Run("HOSTILE: a block whose txs don't match the header merkle root is rejected", () =>
        {
            var txs = new List<Chain.Tx> { SampleTx(1, k.Pub), SampleTx(2, k.Pub) };
            var header = MineHeader(new byte[32], new byte[32]); // wrong root
            T.Throws(() => BsvBlock.Parse(BsvBlock.Serialize(header, txs)), "merkle-root mismatch rejected");
        });

        T.Run("no-server flow: parse a block, build a merkleblock for the funding tx, verify funding", () =>
        {
            var me = Secp256k1.GenerateKeyPair();
            var fundTx = new Chain.Tx(2,
                new() { new("ff".PadRight(64, '1'), 0, Array.Empty<byte>(), 0xffffffff) },
                new() { new(33000, Chain.P2pkhLockForPub(me.Pub)) }, 0);
            // a block containing the funding tx among others
            var txs = new List<Chain.Tx> { SampleTx(8, k.Pub), fundTx, SampleTx(9, k.Pub), SampleTx(10, k.Pub) };
            var txids = txs.Select(t => Hashes.Sha256d(Chain.Serialize(t))).ToList();
            var header = MineHeader(new byte[32], MerkleProof.Root(txids));
            var blockBytes = BsvBlock.Serialize(header, txs);

            // payer side: parse the block, find the funding tx index, build the merkleblock envelope
            var parsed = BsvBlock.Parse(blockBytes);
            int idx = parsed.Txs.FindIndex(t => Chain.Txid(t) == Chain.Txid(fundTx));
            T.True(idx >= 0, "funding tx located in the block");
            var mb = PartialMerkleTree.BuildMerkleBlock(parsed.Header, parsed.Txids, new HashSet<int> { idx });

            // recipient side: validated header chain + verify funding from the merkleblock
            var chain = new HeadersChain(); chain.AddGenesis(header);
            var utxo = SpvFunding.VerifyFromMerkleBlock(fundTx, 0, mb, chain, me.Pub, 0, 0);
            T.True(utxo != null, "funding verified end-to-end from a parsed block");
            T.Eq(utxo!.Value, 33000L, "value correct");
        });
    }
}

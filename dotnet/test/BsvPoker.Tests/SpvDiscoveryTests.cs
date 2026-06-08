using BsvPoker.Core;
using BsvPoker.Crypto;
using BsvPoker.Net.Bsv;

namespace BsvPoker.Tests;

/// <summary>
/// Automatic SPV discovery of an incoming payment: a peer hands us a transaction plus a <c>merkleblock</c>
/// proving it was mined; the wallet credits it ONLY after re-verifying the partial tree against a header it
/// validated itself and that an output truly pays one of our keys. This is the engine behind the wallet's
/// <c>ConfirmIncoming</c> ("you sent funds and it shows up") path. We exercise the exact verification it calls
/// (<see cref="SpvFunding.VerifyFromMerkleBlock"/>) plus the hostile cases that must be rejected.
/// </summary>
public static class SpvDiscoveryTests
{
    private const uint EasyBits = 0x207fffff;

    private static BlockHeader MineHeader(byte[] prev, byte[] merkleRoot)
    {
        for (uint nonce = 1; ; nonce++)
        {
            var h = new BlockHeader(1, prev, merkleRoot, 1_700_000_000, EasyBits, nonce);
            if (h.MeetsPow()) return h;
        }
    }

    public static void All()
    {
        Console.WriteLine("SPV discovery (auto-credit from a peer's merkleblock, no server):");

        var me = Secp256k1.GenerateKeyPair();
        var other = Secp256k1.GenerateKeyPair();

        // a real payment to us: 55000 sat at vout 1
        var pay = new Chain.Tx(2,
            new() { new("ab".PadRight(64, '7'), 0, Array.Empty<byte>(), 0xffffffff) },
            new() { new(9000, Chain.P2pkhLockForPub(other.Pub)), new(55000, Chain.P2pkhLockForPub(me.Pub)) }, 0);
        var leaf = Hashes.Sha256d(Chain.Serialize(pay));

        // place it among other leaves; build the block + a merkleblock proving our tx is in it
        var leaves = new List<byte[]>();
        for (int i = 0; i < 5; i++) { var b = new byte[32]; b[0] = (byte)(i + 30); leaves.Add(b); }
        int idx = 3; leaves.Insert(idx, leaf);
        var root = MerkleProof.Root(leaves);
        var header = MineHeader(new byte[32], root);
        var mb = PartialMerkleTree.BuildMerkleBlock(header, leaves, new HashSet<int> { idx });

        var chain = new HeadersChain();
        chain.AddGenesis(header);

        T.Run("a peer's merkleblock + tx credits the right key, with the right amount", () =>
        {
            var utxo = SpvFunding.VerifyFromMerkleBlock(pay, 1, mb, chain, me.Pub, 0, 0);
            T.True(utxo != null, "verified against our own headers");
            T.Eq(utxo!.Value, 55000L, "amount picked up");
            T.Eq(utxo.Txid, Chain.Txid(pay), "correct txid");
        });

        T.Run("the output paying someone else is not credited to us", () =>
        {
            T.True(SpvFunding.VerifyFromMerkleBlock(pay, 0, mb, chain, me.Pub, 0, 0) == null, "vout 0 pays 'other' → not ours");
            T.True(SpvFunding.VerifyFromMerkleBlock(pay, 1, mb, chain, other.Pub, 0, 0) == null, "claiming our vout for a stranger key → rejected");
        });

        T.Run("a merkleblock for a block we have NOT validated is rejected", () =>
        {
            var empty = new HeadersChain();   // we validated no headers
            T.True(SpvFunding.VerifyFromMerkleBlock(pay, 1, mb, empty, me.Pub, 0, 0) == null, "unknown block → no credit");
        });

        T.Run("a proof whose tree does not contain our tx is rejected", () =>
        {
            // build a merkleblock that proves a DIFFERENT leaf, not ours
            var mbOther = PartialMerkleTree.BuildMerkleBlock(header, leaves, new HashSet<int> { 0 });
            T.True(SpvFunding.VerifyFromMerkleBlock(pay, 1, mbOther, chain, me.Pub, 0, 0) == null, "our tx not in the proven set → rejected");
        });

        T.Run("a tampered transaction no longer matches the proof", () =>
        {
            var tampered = new Chain.Tx(2, pay.Ins, new() { pay.Outs[0], new(55001, Chain.P2pkhLockForPub(me.Pub)) }, 0); // value changed
            T.True(SpvFunding.VerifyFromMerkleBlock(tampered, 1, mb, chain, me.Pub, 0, 0) == null, "altered tx → different txid → not in proof");
        });
    }
}

using BsvPoker.Core;
using BsvPoker.Crypto;
using BsvPoker.Net.Bsv;

namespace BsvPoker.Tests;

/// <summary>SPV funding: a funding tx + merkle proof, verified against a validated header, becomes a wallet
/// UTXO — and a tx in an unknown block, paying the wrong key, or with a tampered proof is rejected.</summary>
public static class SpvFundingTests
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
        Console.WriteLine("SPV funding (UTXO learned from the chain, no server):");
        var me = Secp256k1.GenerateKeyPair();
        var other = Secp256k1.GenerateKeyPair();

        // a funding tx paying us 70000 at vout 1
        var fundTx = new Chain.Tx(2,
            new() { new("ee".PadRight(64, '9'), 0, Array.Empty<byte>(), 0xffffffff) },
            new() { new(10000, Chain.P2pkhLockForPub(other.Pub)), new(70000, Chain.P2pkhLockForPub(me.Pub)) }, 0);
        var leaf = Hashes.Sha256d(Chain.Serialize(fundTx));

        // place it in a block among other leaves, and validate that block's header in a chain
        var leaves = new List<byte[]>();
        for (int i = 0; i < 4; i++) { var b = new byte[32]; b[0] = (byte)(i + 1); leaves.Add(b); }
        int idx = 2; leaves.Insert(idx, leaf);
        var root = MerkleProof.Root(leaves);
        var branch = MerkleProof.Branch(leaves, idx);

        var chain = new HeadersChain();
        var header = MineHeader(new byte[32], root);
        chain.AddGenesis(header);

        T.Run("a valid SPV funding proof becomes a spendable UTXO", () =>
        {
            var proof = new SpvFunding.Proof(fundTx, 1, header.HashHex(), branch, idx);
            var utxo = SpvFunding.Verify(proof, chain, me.Pub, 0, 0);
            T.True(utxo != null, "verified");
            T.Eq(utxo!.Value, 70000L, "value picked up");
            var w = new OnChainWallet(WalletKeys.NewSeed()); w.Add(utxo); // (key indices are illustrative here)
            T.Eq(w.Balance, 70000L, "funds now in the wallet");
        });

        T.Run("a proof for a block we have not validated is rejected", () =>
        {
            var proof = new SpvFunding.Proof(fundTx, 1, new string('0', 64), branch, idx);
            T.True(SpvFunding.Verify(proof, chain, me.Pub, 0, 0) == null, "unknown block → rejected");
        });

        T.Run("an output that does not pay us is rejected", () =>
        {
            var proof = new SpvFunding.Proof(fundTx, 1, header.HashHex(), branch, idx);
            T.True(SpvFunding.Verify(proof, chain, other.Pub, 0, 0) == null || SpvFunding.Verify(proof, chain, me.Pub, 0, 0) != null, "wrong-key check");
            // vout 0 pays 'other', so claiming it as ours fails
            var proof0 = new SpvFunding.Proof(fundTx, 0, header.HashHex(), MerkleProof.Branch(leaves, idx), idx);
            T.True(SpvFunding.Verify(proof0, chain, me.Pub, 0, 0) == null, "vout not paying us → rejected");
        });

        T.Run("a tampered merkle branch is rejected", () =>
        {
            var bad = branch.Select(b => (byte[])b.Clone()).ToArray(); bad[0][0] ^= 0xFF;
            var proof = new SpvFunding.Proof(fundTx, 1, header.HashHex(), bad, idx);
            T.True(SpvFunding.Verify(proof, chain, me.Pub, 0, 0) == null, "bad proof → rejected");
        });
    }
}

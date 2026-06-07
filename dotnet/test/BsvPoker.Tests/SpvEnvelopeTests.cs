using BsvPoker.Core;
using BsvPoker.Crypto;
using BsvPoker.Net.Bsv;

namespace BsvPoker.Tests;

/// <summary>
/// Standalone SPV per the whitepaper: a coin's envelope (tx + merkle proof + header) verifies LOCALLY —
/// merkle branch folds to the header's merkle root and the header meets proof-of-work — with NO node and NO
/// chain scan. Tampering with the tx, the branch, or the header's PoW is rejected. Round-trips over the wire.
/// </summary>
public static class SpvEnvelopeTests
{
    private const uint EasyBits = 0x207fffff;

    private static BlockHeader MineHeader(byte[] root)
    {
        for (uint nonce = 1; ; nonce++)
        {
            var h = new BlockHeader(1, new byte[32], root, 1_700_000_000, EasyBits, nonce);
            if (h.MeetsPow()) return h;
        }
    }

    public static void All()
    {
        Console.WriteLine("standalone SPV envelope (verify a mined coin with NO node):");
        var me = Secp256k1.GenerateKeyPair();

        // a real coin tx paying me, placed in a block among other leaves
        var tx = new Chain.Tx(2, new() { new("ab".PadRight(64, '7'), 0, Array.Empty<byte>(), 0xffffffff) },
            new() { new(123456, Chain.P2pkhLockForPub(me.Pub)) }, 0);
        var leaf = Hashes.Sha256d(Chain.Serialize(tx));
        var leaves = new List<byte[]>(); for (int i = 0; i < 4; i++) { var b = new byte[32]; b[0] = (byte)(i + 1); leaves.Add(b); }
        int idx = 2; leaves.Insert(idx, leaf);
        var header = MineHeader(MerkleProof.Root(leaves));
        var branch = MerkleProof.Branch(leaves, idx);

        T.Run("a valid envelope verifies locally (merkle proof + header PoW), no node", () =>
        {
            var env = new SpvEnvelope(Chain.Serialize(tx), header.Serialize(), branch, idx);
            T.True(env.Verify(), "valid SPV envelope verifies standalone");
            T.Eq(env.Txid(), Chain.Txid(tx), "txid matches");
        });

        T.Run("a tampered tx, branch, or header is rejected", () =>
        {
            var badTx = Chain.Serialize(tx); badTx[^1] ^= 0x01;
            T.False(new SpvEnvelope(badTx, header.Serialize(), branch, idx).Verify(), "tampered tx rejected");
            var bad = branch.Select(b => (byte[])b.Clone()).ToArray(); bad[0][0] ^= 0xFF;
            T.False(new SpvEnvelope(Chain.Serialize(tx), header.Serialize(), bad, idx).Verify(), "tampered branch rejected");
            // a header that does NOT meet PoW (hard target, random nonce) is rejected
            var weak = new BlockHeader(1, new byte[32], MerkleProof.Root(leaves), 1_700_000_000, 0x1d00ffff, 1);
            T.False(new SpvEnvelope(Chain.Serialize(tx), weak.Serialize(), branch, idx).Verify(), "header failing PoW rejected");
        });

        T.Run("envelope round-trips over the wire (handed IP-to-IP)", () =>
        {
            var env = new SpvEnvelope(Chain.Serialize(tx), header.Serialize(), branch, idx);
            var back = SpvEnvelope.FromWire(env.ToWire());
            T.True(back.Verify(), "wire round-trip still verifies");
            T.Eq(back.Txid(), env.Txid(), "same coin");
        });
    }
}

using System.Security.Cryptography;
using BsvPoker.Net.Bsv;

namespace BsvPoker.Tests;

/// <summary>SPV merkle proofs: root computation, per-leaf branch verification, odd rows, and rejection of
/// a wrong txid or a tampered branch — across several tree sizes.</summary>
public static class MerkleProofTests
{
    private static List<byte[]> Leaves(int n)
    {
        var list = new List<byte[]>();
        for (int i = 0; i < n; i++) { var b = new byte[32]; b[0] = (byte)i; b[1] = (byte)(i * 7 + 1); list.Add(b); }
        return list;
    }

    public static void All()
    {
        Console.WriteLine("SPV merkle proofs:");

        T.Run("single-leaf root is the leaf itself", () =>
        {
            var l = Leaves(1);
            T.Eq(T.Hex(MerkleProof.Root(l)), T.Hex(l[0]), "root of one tx = that tx");
        });

        T.Run("every leaf's branch verifies against the root (sizes 2..9, incl. odd rows)", () =>
        {
            foreach (int n in new[] { 2, 3, 4, 5, 8, 9 })
            {
                var leaves = Leaves(n);
                var root = MerkleProof.Root(leaves);
                for (int i = 0; i < n; i++)
                {
                    var branch = MerkleProof.Branch(leaves, i);
                    T.True(MerkleProof.Verify(leaves[i], i, branch, root), $"n={n} leaf {i} proof verifies");
                }
            }
        });

        T.Run("a wrong txid or a tampered branch is rejected", () =>
        {
            var leaves = Leaves(6);
            var root = MerkleProof.Root(leaves);
            var branch = MerkleProof.Branch(leaves, 2);
            T.True(MerkleProof.Verify(leaves[2], 2, branch, root), "genuine proof ok");
            var wrong = (byte[])leaves[2].Clone(); wrong[5] ^= 0xFF;
            T.False(MerkleProof.Verify(wrong, 2, branch, root), "wrong txid rejected");
            var tampered = branch.Select(b => (byte[])b.Clone()).ToArray(); tampered[0][0] ^= 0xFF;
            T.False(MerkleProof.Verify(leaves[2], 2, tampered, root), "tampered branch rejected");
            T.False(MerkleProof.Verify(leaves[2], 3, branch, root), "wrong index rejected");
        });

        T.Run("a proof verifies against a block header's merkle root", () =>
        {
            var leaves = Leaves(7);
            var root = MerkleProof.Root(leaves);
            // a header carrying that merkle root (other fields arbitrary)
            var header = new BlockHeader(1, new byte[32], root, 1_700_000_000, 0x207fffff, 0);
            T.Eq(T.Hex(header.MerkleRoot), T.Hex(root), "header carries the root");
            var branch = MerkleProof.Branch(leaves, 4);
            T.True(MerkleProof.Verify(leaves[4], 4, branch, header.MerkleRoot), "tx proven in the block via its header root");
        });
    }
}

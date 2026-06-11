using System.Linq;
using BsvPoker.Core;
using BsvPoker.Crypto;

namespace BsvPoker.Tests;

/// <summary>
/// Dealerless (k, j)-threshold secret distribution (Wright, "A Distribution Protocol for Dealerless Secret
/// Distribution"; nChain EP3669491B1). Verifies: any k of j shares reconstruct the shared secret d_A = Σ free
/// terms (and any OTHER k-subset gives the same secret), the public key is d_A·G computed without reconstruction,
/// fewer than k shares reveal nothing, and Feldman verification catches a tampered share.
/// </summary>
public static class ThresholdSharingTests
{
    private static byte[] SumSecrets(System.Collections.Generic.IReadOnlyList<ThresholdSharing.Polynomial> polys)
    {
        var d = polys[0].Secret;
        for (int h = 1; h < polys.Count; h++) d = Secp256k1.ScalarFieldAddModN(d, polys[h].Secret);
        return d;
    }

    public static void All()
    {
        Console.WriteLine("dealerless (k,j)-threshold secret distribution (Wright, EP3669491B1):");

        T.Run("any k of j shares reconstruct the shared secret; the public key is d_A·G", () =>
        {
            int j = 5, k = 3;
            var polys = Enumerable.Range(0, j).Select(_ => ThresholdSharing.Polynomial.Random(k)).ToList();
            var shares = ThresholdSharing.AllShares(polys, j);
            var dA = SumSecrets(polys);

            var subsetA = new[] { (shares[0].Index, shares[0].Share), (shares[2].Index, shares[2].Share), (shares[4].Index, shares[4].Share) };
            T.Eq(T.Hex(ThresholdSharing.Reconstruct(subsetA)), T.Hex(dA), "3 of 5 shares reconstruct the shared secret d_A");

            var subsetB = new[] { (shares[1].Index, shares[1].Share), (shares[2].Index, shares[2].Share), (shares[3].Index, shares[3].Share) };
            T.Eq(T.Hex(ThresholdSharing.Reconstruct(subsetB)), T.Hex(dA), "a DIFFERENT 3-of-5 subset reconstructs the SAME secret");

            T.Eq(T.Hex(ThresholdSharing.PublicKey(polys)), T.Hex(Secp256k1.PublicKeyCompressed(dA)), "Q_A = d_A·G, computed without reconstructing d_A");
        });

        T.Run("fewer than k shares do NOT reveal the secret (interpolate to a wrong value)", () =>
        {
            int j = 5, k = 3;
            var polys = Enumerable.Range(0, j).Select(_ => ThresholdSharing.Polynomial.Random(k)).ToList();
            var shares = ThresholdSharing.AllShares(polys, j);
            var dA = SumSecrets(polys);
            var twoOnly = new[] { (shares[0].Index, shares[0].Share), (shares[1].Index, shares[1].Share) };
            T.True(T.Hex(ThresholdSharing.Reconstruct(twoOnly)) != T.Hex(dA), "2 of 5 shares (below k=3) interpolate to a wrong value — no secret leak");
        });

        T.Run("Feldman verification: an honest share verifies; a tampered share is provably caught", () =>
        {
            int k = 3;
            var f = ThresholdSharing.Polynomial.Random(k);
            var commit = f.Commitments();
            for (int h = 1; h <= 5; h++)
                T.True(ThresholdSharing.VerifyShare(h, f.Eval(h), commit), $"honest share f({h}) verifies against the coefficient commitments");
            var bad = (byte[])f.Eval(2).Clone(); bad[31] ^= 1;
            T.True(!ThresholdSharing.VerifyShare(2, bad, commit), "a tampered share is rejected by Feldman verification");
        });
    }
}

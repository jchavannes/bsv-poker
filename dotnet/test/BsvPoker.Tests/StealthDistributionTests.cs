using BsvPoker.Core;
using BsvPoker.Crypto;

namespace BsvPoker.Tests;

/// <summary>
/// Stealth-address distribution (US20240152913A1): two dealerless threshold groups agree, by THRESHOLD ECDH,
/// on a one-time secret c = H(e·Q) = H(d·P) without either learning the other's key; the pool pays to a
/// stealth key A_pool = Q + c·G that only group B (as a t+1 coalition) can spend, via a threshold signature on
/// key (d + c). On-chain the pool is a plain P2PKH — nothing links funder to payee.
/// </summary>
public static class StealthDistributionTests
{
    public static void All()
    {
        Console.WriteLine("stealth distribution (US20240152913A1 — threshold-ECDH one-time pool key):");

        T.Run("THRESHOLD ECDH: both groups derive the SAME shared secret c = H(e·Q) = H(d·P), no key revealed", () =>
        {
            var groupA = ThresholdEcdsa.Jvrss(n: 3, degree: 1);   // e, P = e·G
            var groupB = ThresholdEcdsa.Jvrss(n: 3, degree: 1);   // d, Q = d·G
            var cFromA = StealthDistribution.SharedSecret(groupA.Shares, groupB.PublicKey); // H(e·Q)
            var cFromB = StealthDistribution.SharedSecret(groupB.Shares, groupA.PublicKey); // H(d·P)
            T.True(cFromA.SequenceEqual(cFromB), "e·Q and d·P are the same point ⇒ identical c (threshold ECDH agrees)");
        });

        T.Run("the pool key is A_pool = Q + c·G with private key (d + c) — and that is what signs", () =>
        {
            var groupA = ThresholdEcdsa.Jvrss(n: 3, degree: 1);
            var groupB = ThresholdEcdsa.Jvrss(n: 5, degree: 2);   // 3-of-5 withdrawing group
            var c = StealthDistribution.SharedSecret(groupB.Shares, groupA.PublicKey);
            var poolKey = StealthDistribution.PoolKey(groupB.PublicKey, c);

            // consistency: A_pool == (d + c)·G, where d = reconstructed group-B secret
            var d = ThresholdSharing.Reconstruct(groupB.Shares.Take(3).ToList());
            var dPlusC = Secp256k1.ScalarFieldAddModN(d, c);
            T.True(poolKey.SequenceEqual(Secp256k1.PublicKeyCompressed(dPlusC)), "A_pool == (d + c)·G");

            // the pool spend handle threshold-signs and verifies against A_pool
            var spendKey = StealthDistribution.PoolSpendKey(groupB, c);
            T.True(spendKey.PublicKey.SequenceEqual(poolKey), "spend handle's public key is A_pool");
            var digest = Hashes.Sha256(System.Text.Encoding.UTF8.GetBytes("withdraw the pooled output"));
            var sig = ThresholdEcdsa.SignDigest(digest, spendKey);
            T.True(Secp256k1.VerifyDigest(poolKey, digest, sig), "threshold signature with (d+c) verifies against A_pool");
        });

        T.Run("the pooled payout is an ordinary P2PKH spend (no linking script) that verifies on the standard path", () =>
        {
            var groupA = ThresholdEcdsa.Jvrss(n: 3, degree: 1);
            var groupB = ThresholdEcdsa.Jvrss(n: 3, degree: 1);
            var c = StealthDistribution.SharedSecret(groupB.Shares, groupA.PublicKey);
            var spendKey = StealthDistribution.PoolSpendKey(groupB, c);

            var poolLock = StealthDistribution.PoolLock(spendKey.PublicKey);
            T.True(poolLock.Length == 25 && poolLock[0] == 0x76 && poolLock[1] == 0xa9, "pool output is a plain P2PKH");

            const string poolTxid = "22" + "00000000000000000000000000000000000000000000000000000000000000";
            const long amount = 50_000, fee = 150;
            var payee = Secp256k1.GenerateKeyPair().Pub;
            var paid = ThresholdCustody.SettleToWinner(poolTxid, 0, amount, payee, fee, spendKey);
            T.True(Chain.VerifyP2pkhInput(paid, 0, spendKey.PublicKey, amount), "pooled payout verifies on the ordinary consensus path");
            T.Eq(paid.Outs[0].Value, amount - fee, "value conserved to the payee");
            // a stranger (e.g. group A, who knows c but not d) cannot spend A_pool
            T.False(Chain.VerifyP2pkhInput(paid, 0, groupA.PublicKey, amount), "only group B's (d+c) coalition can spend the pool");
        });
    }
}

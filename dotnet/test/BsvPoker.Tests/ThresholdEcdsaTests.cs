using BsvPoker.Core;
using BsvPoker.Crypto;

namespace BsvPoker.Tests;

/// <summary>
/// Threshold ECDSA (nChain TS on JVRSS): (t+1)-of-n parties jointly sign WITHOUT reconstructing the private
/// key, and the result is an ORDINARY secp256k1 signature — it verifies against the shared public key with the
/// normal verifier, so on-chain it looks like any single-key signature. Proven here end-to-end, plus the
/// threshold property (a product of shared secrets needs 2t+1 points) and integrity (wrong key/message fails).
/// </summary>
public static class ThresholdEcdsaTests
{
    public static void All()
    {
        Console.WriteLine("threshold ECDSA (dealerless (t+1)-of-n joint signing, EP4152683B1):");

        T.Run("2-of-3: parties jointly sign; the signature VERIFIES against the shared key with the standard verifier", () =>
        {
            var key = ThresholdEcdsa.Jvrss(n: 3, degree: 1);
            var digest = Hashes.Sha256(System.Text.Encoding.UTF8.GetBytes("settle the pot to the winner"));
            var sig = ThresholdEcdsa.SignDigest(digest, key);
            T.True(Secp256k1.VerifyDigest(key.PublicKey, digest, sig), "threshold signature verifies against a·G");
            T.Eq(sig.Length, 64, "compact 64-byte (r‖s) signature");
            // the shared public key is exactly (reconstructed key)·G — but the key itself was never used to sign
            var recovered = ThresholdSharing.Reconstruct(key.Shares.Take(2).ToList());
            T.True(Secp256k1.PublicKeyCompressed(recovered).SequenceEqual(key.PublicKey), "PublicKey == a·G for the dealerless key a");
        });

        T.Run("3-of-5: the protocol generalises to a higher threshold and still verifies", () =>
        {
            var key = ThresholdEcdsa.Jvrss(n: 5, degree: 2);
            var digest = Hashes.Sha256(System.Text.Encoding.UTF8.GetBytes("3-of-5 custody spend"));
            var sig = ThresholdEcdsa.SignDigest(digest, key);
            T.True(Secp256k1.VerifyDigest(key.PublicKey, digest, sig), "3-of-5 threshold signature verifies");
        });

        T.Run("integrity: the signature fails against a DIFFERENT key and a TAMPERED message", () =>
        {
            var key = ThresholdEcdsa.Jvrss(n: 3, degree: 1);
            var digest = Hashes.Sha256(System.Text.Encoding.UTF8.GetBytes("pay 500 to Alice"));
            var sig = ThresholdEcdsa.SignDigest(digest, key);
            var stranger = Secp256k1.GenerateKeyPair().Pub;
            T.False(Secp256k1.VerifyDigest(stranger, digest, sig), "a stranger's key does not verify the signature");
            var tampered = Hashes.Sha256(System.Text.Encoding.UTF8.GetBytes("pay 5000 to Mallory"));
            T.False(Secp256k1.VerifyDigest(key.PublicKey, tampered, sig), "a tampered message does not verify");
        });

        T.Run("threshold property: a product of shared secrets needs 2t+1 shares — fewer interpolate to the WRONG value", () =>
        {
            var a = ThresholdEcdsa.Jvrss(n: 3, degree: 1);   // t = 1 ⇒ products are degree-2, need 3 points
            var b = ThresholdEcdsa.Jvrss(n: 3, degree: 1);
            var aSecret = ThresholdSharing.Reconstruct(a.Shares.Take(2).ToList());   // t+1 = 2 for a degree-1 secret
            var bSecret = ThresholdSharing.Reconstruct(b.Shares.Take(2).ToList());
            var trueProduct = Secp256k1.ScalarMulModN(aSecret, bSecret);

            var full = ThresholdEcdsa.Pross(a.Shares, b.Shares);                      // all 3 = 2t+1 points
            T.True(full.SequenceEqual(trueProduct), "PROSS with 2t+1 shares recovers the true product a·b");

            // the element-wise product is a degree-2 polynomial; only 2 points (≤ 2t) cannot determine it
            var prodShares = Enumerable.Range(0, 3)
                .Select(i => (a.Shares[i].X, Secp256k1.ScalarMulModN(a.Shares[i].Y, b.Shares[i].Y))).ToList();
            var under = ThresholdSharing.Reconstruct(prodShares.Take(2).ToList());
            T.True(!under.AsSpan().SequenceEqual(trueProduct), "2t shares interpolate to a WRONG product (threshold holds)");
        });
    }
}

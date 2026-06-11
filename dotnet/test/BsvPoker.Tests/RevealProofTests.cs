using BsvPoker.Core;
using BsvPoker.Crypto;

namespace BsvPoker.Tests;

/// <summary>
/// "Prove what it is" (audit-critical): a card-scalar reveal is bound to the commitment C = d·G published
/// at remask. The headline test is the SUBSTITUTION ATTACK — because the card base points Mᵢ=(i+1)·G have
/// public ratios, a cheater can forge a scalar that unmasks their real card to a DIFFERENT valid card,
/// fooling the "is it a real card?" check. The commitment defeats it: the forged scalar's public point no
/// longer matches C. This is what an honest player relies on to show a card and prove it, against a table
/// assumed wholly dishonest.
/// </summary>
public static class RevealProofTests
{
    // big-endian 32-byte scalar for a small positive integer (matches Secp256k1's card-point encoding)
    private static byte[] Enc(int v)
    {
        var b = new byte[32];
        b[31] = (byte)(v & 0xff); b[30] = (byte)((v >> 8) & 0xff);
        b[29] = (byte)((v >> 16) & 0xff); b[28] = (byte)((v >> 24) & 0xff);
        return b;
    }

    public static void All()
    {
        Console.WriteLine("reveal proof — prove-what-it-is (bind every reveal to its commitment):");

        T.Run("an honest reveal opens its commitment (d·G == C) and a wrong scalar does not", () =>
        {
            var d = MentalPokerEC.NewScalar();
            var c = RevealProof.Commit(d);
            T.True(RevealProof.Verify(d, c), "honest scalar opens the commitment");
            var other = MentalPokerEC.NewScalar();
            T.False(RevealProof.Verify(other, c), "a different scalar does NOT open the commitment");
        });

        T.Run("SUBSTITUTION ATTACK: a forged scalar fakes a different VALID card — caught only by the commitment", () =>
        {
            const int n = 52, m = 3, mPrime = 17;     // true card index 3, the card the cheater wants to show: 17
            var d = MentalPokerEC.NewScalar();
            var commit = RevealProof.Commit(d);        // C = d·G, published at remask (before any open)

            // the final masked point the cheater "holds": P = d · M_m
            var P = Secp256k1.PointMul(Secp256k1.CardBasePoint(m), d);

            // honest open: strip d → M_m, identifies as card m, and the reveal opens the commitment
            T.Eq(MentalPokerEC.Identify(MentalPokerEC.Unmask(P, new[] { d }), n), m, "honest open yields the true card");
            T.True(RevealProof.Verify(d, commit), "honest open passes the commitment check");

            // forged scalar d' = d·(m+1)·(m'+1)⁻¹  ⇒  stripping d' turns P into M_{m'} (a DIFFERENT real card)
            var dPrime = Secp256k1.ScalarMulModN(Secp256k1.ScalarMulModN(d, Enc(m + 1)), Secp256k1.ScalarInverse(Enc(mPrime + 1)));
            var faked = MentalPokerEC.Unmask(P, new[] { dPrime });
            T.Eq(MentalPokerEC.Identify(faked, n), mPrime, "the forgery DOES unmask to a valid, different card (Identify alone is fooled)");

            // …but the forged scalar does NOT open the commitment — the substitution is provably rejected.
            T.False(RevealProof.Verify(dPrime, commit), "the commitment REJECTS the substituted reveal (prove-what-it-is holds)");
        });

        T.Run("fail-closed: malformed scalar or commitment is rejected, never throws", () =>
        {
            var d = MentalPokerEC.NewScalar();
            var c = RevealProof.Commit(d);
            T.False(RevealProof.Verify(new byte[32], c), "an all-zero (invalid) scalar is rejected");
            T.False(RevealProof.Verify(d, new byte[33]), "an all-zero commitment is rejected");
            T.False(RevealProof.Verify(new byte[5], c), "a wrong-length scalar is rejected");
            T.False(RevealProof.Verify(d, new byte[10]), "a wrong-length commitment is rejected");
        });
    }
}

using System.Security.Cryptography;
using BsvPoker.Crypto;

namespace BsvPoker.Core;

/// <summary>
/// "PROVE WHAT IT IS" — bind every card-scalar reveal to a commitment published during the deal.
///
/// In the commutative-encryption deal each position carries a product of per-card scalars; a player
/// reveals its scalar d for a position so the recipient can strip it. The commitment is simply the
/// scalar's public point C = d·G, broadcast at REMASK time (before any card is opened). It leaks nothing:
/// recovering d from d·G is the discrete-log problem, and on secp256k1 there is no pairing, so the
/// per-position commitments cannot be combined to reconstruct the product mask either. At reveal the
/// verifier checks d·G == C.
///
/// WHY THIS IS REQUIRED (the substitution attack it stops): the card base points are Mᵢ = (i+1)·G with
/// PUBLICLY KNOWN ratios. A dishonest player holding the true scalar d for a final point P = d·M_m can
/// compute a FORGED scalar d' = d·(m+1)·(m'+1)⁻¹ (mod n); revealing d' makes P unmask to a DIFFERENT but
/// perfectly valid card M_{m'} = (m'+1)·G — so the "is the result a real card?" check (Identify) is
/// fooled. Binding the reveal to the pre-published commitment defeats this: d' ≠ d ⇒ d'·G ≠ C, so the
/// substituted reveal is provably rejected. This is what lets an honest Alice show her card and PROVE it,
/// and stops a colluding table from misrepresenting the cards they hold.
/// </summary>
public static class RevealProof
{
    /// <summary>The commitment to a per-card scalar: C = d·G (33-byte compressed). Publish at remask time.</summary>
    public static byte[] Commit(ReadOnlySpan<byte> scalar) => Secp256k1.PublicKeyCompressed(scalar);

    /// <summary>True iff the revealed scalar is the exact one committed to (d·G == C). Fail-closed on any bad input.</summary>
    public static bool Verify(byte[] revealedScalar, byte[] commitment)
    {
        try
        {
            if (revealedScalar is not { Length: 32 } || commitment is not { Length: 33 }) return false;
            if (!Secp256k1.IsValidScalar(revealedScalar)) return false;
            return CryptographicOperations.FixedTimeEquals(Secp256k1.PublicKeyCompressed(revealedScalar), commitment);
        }
        catch { return false; }
    }
}

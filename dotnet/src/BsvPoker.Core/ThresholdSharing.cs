using BsvPoker.Crypto;

namespace BsvPoker.Core;

/// <summary>
/// Dealerless (k, j)-threshold secret distribution — a faithful implementation of C. S. Wright, "A Distribution
/// Protocol for Dealerless Secret Distribution" (ICICT 2019; nChain EP3669491B1 / EP4152683B1).
///
/// Algorithm 1: each of j participants picks a SECRET degree-(k-1) polynomial f_i over Z_n (n = the secp256k1
/// group order). The shared secret d_A = Σ a_0^(i) is the sum of every participant's free term and is NEVER
/// reconstructed in normal use. Participant i's key share is d_A(i) = Σ_h f_h(i) mod n. The secret is
/// recoverable ONLY from ≥ k shares by Lagrange interpolation at x = 0 — fewer than k reveal nothing. Feldman
/// EC commitments (a_κ·G) make every share verifiable: Σ_κ h^κ·(a_κ·G) = f_i(h)·G.
///
/// This is the foundation of the threshold mental-poker deal: each card's decryption key is shared this way;
/// the recipient holds one of the k required shares and never releases it, so no coalition below threshold —
/// and not the recipient alone — can open a card. To deal a card the OTHER players supply their shares; the
/// recipient's own share is mandatory. (No dealer, no trusted party — matches the project's threat model.)
/// </summary>
public static class ThresholdSharing
{
    /// <summary>A participant's secret degree-(k-1) polynomial f(x) = Σ a_γ x^γ mod n over Z_n; a_0 is its secret.</summary>
    public sealed class Polynomial
    {
        public byte[][] Coeffs { get; }   // a_0 .. a_{k-1}; a_0 (the free term) is this participant's secret
        public Polynomial(byte[][] coeffs)
        {
            if (coeffs == null || coeffs.Length < 1) throw new ArgumentException("need >= 1 coefficient (k >= 1)");
            Coeffs = coeffs;
        }
        public int Threshold => Coeffs.Length;   // k
        public byte[] Secret => Coeffs[0];        // a_0

        /// <summary>A fresh random degree-(k-1) polynomial. <paramref name="freeTerm"/> fixes a_0 (else random) —
        /// pass a 32-byte zero for the Algorithm 2 zero-sharing (share refresh that does not change the secret).</summary>
        public static Polynomial Random(int k, byte[]? freeTerm = null)
        {
            if (k < 1) throw new ArgumentException("threshold k >= 1");
            var c = new byte[k][];
            c[0] = freeTerm ?? MentalPokerEC.NewScalar();
            for (int i = 1; i < k; i++) c[i] = MentalPokerEC.NewScalar();
            return new Polynomial(c);
        }

        /// <summary>f(x) = Σ a_γ x^γ mod n by Horner's method. x is a participant index (≥ 1).</summary>
        public byte[] Eval(int x)
        {
            if (x < 0) throw new ArgumentException("index >= 0");
            var xs = ScalarOf(x);
            var acc = Coeffs[^1];
            for (int i = Coeffs.Length - 2; i >= 0; i--)
                acc = Secp256k1.ScalarFieldAddModN(Secp256k1.ScalarMulModN(acc, xs), Coeffs[i]);
            return acc;
        }

        /// <summary>The Feldman commitments a_γ·G to each coefficient (broadcast in step 4, for verification).</summary>
        public byte[][] Commitments()
        {
            var g = new byte[Coeffs.Length][];
            for (int i = 0; i < Coeffs.Length; i++) g[i] = Secp256k1.PublicKeyCompressed(Coeffs[i]);
            return g;
        }
    }

    /// <summary>Feldman verification (Algorithm 1, step 5): a received share f_i(h) is consistent with the
    /// dealer's broadcast coefficient commitments iff Σ_κ h^κ·(a_κ·G) == f_i(h)·G. Catches a lying participant.</summary>
    public static bool VerifyShare(int h, byte[] shareValue, IReadOnlyList<byte[]> coeffCommitments)
    {
        try
        {
            var hs = ScalarOf(h);
            byte[]? acc = null;
            var hpow = ScalarOf(1);                          // h^0 = 1
            foreach (var aG in coeffCommitments)
            {
                var term = Secp256k1.PointMul(aG, hpow);     // h^κ · (a_κ·G)
                acc = acc == null ? term : Secp256k1.PointAddCompressed(acc, term);
                hpow = Secp256k1.ScalarMulModN(hpow, hs);
            }
            return acc != null && acc.AsSpan().SequenceEqual(Secp256k1.PublicKeyCompressed(shareValue));
        }
        catch { return false; }
    }

    /// <summary>Algorithm 1, step 7: participant i's key share d_A(i) = Σ_{h=1..j} f_h(i) mod n — the sum of
    /// EVERY participant's polynomial evaluated at i. Participants are indexed 1..j.</summary>
    public static byte[] JointShare(int participantIndex, IReadOnlyList<Polynomial> allPolynomials)
    {
        byte[]? acc = null;
        foreach (var f in allPolynomials)
        {
            var v = f.Eval(participantIndex);
            acc = acc == null ? v : Secp256k1.ScalarFieldAddModN(acc, v);
        }
        return acc ?? new byte[32];
    }

    /// <summary>Every participant's key share (indices 1..j). Entry i-1 belongs to participant i.</summary>
    public static (int Index, byte[] Share)[] AllShares(IReadOnlyList<Polynomial> polys, int j)
    {
        var s = new (int, byte[])[j];
        for (int i = 1; i <= j; i++) s[i - 1] = (i, JointShare(i, polys));
        return s;
    }

    /// <summary>The shared public key Q_A = d_A·G = Σ_h (a_0^(h)·G) — the sum of every participant's free-term
    /// commitment, computed WITHOUT ever reconstructing d_A.</summary>
    public static byte[] PublicKey(IReadOnlyList<Polynomial> polys)
    {
        byte[]? acc = null;
        foreach (var f in polys)
        {
            var a0G = Secp256k1.PublicKeyCompressed(f.Secret);
            acc = acc == null ? a0G : Secp256k1.PointAddCompressed(acc, a0G);
        }
        return acc!;
    }

    /// <summary>Reconstruct a secret from ≥ k shares by Lagrange interpolation at x = 0:
    /// secret = Σ_i y_i·λ_i, where λ_i = Π_{m≠i} (0 − x_m)/(x_i − x_m) mod n. Fewer than k shares interpolate to
    /// a WRONG value — the security property: nothing about the true secret leaks below threshold.</summary>
    public static byte[] Reconstruct(IReadOnlyList<(int X, byte[] Y)> shares)
    {
        if (shares.Count == 0) throw new ArgumentException("no shares");
        var zero = new byte[32];
        byte[]? acc = null;
        foreach (var (xi, yi) in shares)
        {
            byte[] num = ScalarOf(1), den = ScalarOf(1);
            var xiS = ScalarOf(xi);
            foreach (var (xm, _) in shares)
            {
                if (xm == xi) continue;
                var xmS = ScalarOf(xm);
                num = Secp256k1.ScalarMulModN(num, Secp256k1.ScalarSubModN(zero, xmS));   // (0 − x_m)
                den = Secp256k1.ScalarMulModN(den, Secp256k1.ScalarSubModN(xiS, xmS));    // (x_i − x_m)
            }
            var lambda = Secp256k1.ScalarMulModN(num, Secp256k1.ScalarInverse(den));
            var term = Secp256k1.ScalarMulModN(yi, lambda);
            acc = acc == null ? term : Secp256k1.ScalarFieldAddModN(acc, term);
        }
        return acc!;
    }

    /// <summary>
    /// Lagrange interpolation AT ZERO in the EXPONENT. Given a point-sharing {(x_i, Y_i)} where Y_i = s(x_i)·B
    /// for some base point B and a degree-t polynomial s with s(0) = the shared secret, recover s·B = Σ_i λ_i·Y_i
    /// WITHOUT ever learning s (λ_i are the same Lagrange-at-0 coefficients as <see cref="Reconstruct"/>). This is
    /// THRESHOLD ECDH: with s = a shared private key and B = a counterparty's public point, ≥ t+1 parties jointly
    /// compute s·B (e.g. d·P) though no one holds s. Needs ≥ t+1 points.
    /// </summary>
    public static byte[] InterpolatePointAtZero(IReadOnlyList<(int X, byte[] Point)> shares)
    {
        if (shares.Count == 0) throw new ArgumentException("no shares");
        var zero = new byte[32];
        byte[]? acc = null;
        foreach (var (xi, yi) in shares)
        {
            byte[] num = ScalarOf(1), den = ScalarOf(1);
            var xiS = ScalarOf(xi);
            foreach (var (xm, _) in shares)
            {
                if (xm == xi) continue;
                var xmS = ScalarOf(xm);
                num = Secp256k1.ScalarMulModN(num, Secp256k1.ScalarSubModN(zero, xmS));   // (0 − x_m)
                den = Secp256k1.ScalarMulModN(den, Secp256k1.ScalarSubModN(xiS, xmS));    // (x_i − x_m)
            }
            var lambda = Secp256k1.ScalarMulModN(num, Secp256k1.ScalarInverse(den));
            var term = Secp256k1.PointMul(yi, lambda);                                    // λ_i · Y_i
            acc = acc == null ? term : Secp256k1.PointAddCompressed(acc, term);
        }
        return acc!;
    }

    /// <summary>A small participant index / x-coordinate as a 32-byte big-endian scalar in Z_n.</summary>
    private static byte[] ScalarOf(int v)
    {
        if (v < 0) throw new ArgumentException("index >= 0");
        var b = new byte[32];
        for (int i = 0; i < 4; i++) b[31 - i] = (byte)(v >> (8 * i));
        return b;
    }
}

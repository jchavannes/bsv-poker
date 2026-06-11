using BsvPoker.Crypto;

namespace BsvPoker.Core;

/// <summary>
/// Threshold ECDSA — (t+1)-of-n parties jointly produce a STANDARD secp256k1 signature WITHOUT any party ever
/// learning the private key or the ephemeral key. A faithful build of the nChain threshold-signature protocol
/// (M. Pettit), layered on the dealerless JVRSS in <see cref="ThresholdSharing"/> (Wright, EP3669491B1). The
/// output (r,s) verifies against the shared public key with the ordinary verifier, so on-chain it is
/// indistinguishable from a single-key signature — the basis for the EP4152683B1 threshold-custody /
/// reclamation flow and the US20240152913A1 stealth-spend, where no dealer and no trusted party may exist.
///
/// Protocol (t = polynomial degree; reconstruction threshold = t+1; requires n ≥ 2t+1 parties because the
/// signature is a PRODUCT of two shared secrets, a degree-2t polynomial needing 2t+1 points):
///   key a  : JVRSS — joint shares a_i = a(i), public key a·G, a(0)=a never reconstructed.
///   PROSS  : reconstruct the PUBLIC product a·b from the element-wise share products a_i·b_i (2t+1 points).
///   INVSS  : k⁻¹ shares = u⁻¹·b_i where b is a fresh blinding JVRSS and u = k·b is public (PROSS) — k stays secret.
///   SIGN(e): ephemeral k via JVRSS; r = (k·G).x mod n; s_i = (e + r·a_i)·k⁻¹_i; s = interpolate(2t+1 s_i) at 0
///            = k⁻¹(e + a·r) mod n; emit low-S (r‖s).
///
/// This module is pure math (no I/O) so it is fully simulated and proven in the test suite. In a real
/// deployment each party holds ONLY its own polynomial and shares; here we model all parties together so the
/// protocol can be exercised end-to-end.
/// </summary>
public static class ThresholdEcdsa
{
    /// <summary>A dealerless jointly-shared secret: every party's secret degree-t polynomial, the resulting
    /// joint shares (indices 1..n), the public key a·G, and the parameters. The secret a(0) is NEVER held.</summary>
    public sealed record Shared(
        IReadOnlyList<ThresholdSharing.Polynomial> Polys,
        (int X, byte[] Y)[] Shares,
        byte[] PublicKey,
        int N,
        int Degree);

    private static byte[] One() { var b = new byte[32]; b[31] = 1; return b; }
    private static bool IsZero(byte[] b) { foreach (var x in b) if (x != 0) return false; return true; }

    /// <summary>JVRSS — n parties each pick a fresh secret degree-t polynomial; returns the joint shares and the
    /// public key a·G. Requires n ≥ 2t+1 so products of shared secrets (PROSS) have enough points.</summary>
    public static Shared Jvrss(int n, int degree)
    {
        if (degree < 1) throw new ArgumentException("degree t >= 1 (threshold t+1 >= 2)");
        if (n < 2 * degree + 1) throw new ArgumentException($"need n >= 2t+1 = {2 * degree + 1} parties");
        var polys = new List<ThresholdSharing.Polynomial>(n);
        for (int i = 0; i < n; i++) polys.Add(ThresholdSharing.Polynomial.Random(degree + 1));
        var shares = ThresholdSharing.AllShares(polys, n);
        var pub = ThresholdSharing.PublicKey(polys);
        return new Shared(polys, shares, pub, n, degree);
    }

    /// <summary>PROSS — the PUBLIC product a·b of two shared secrets, from the element-wise products of their
    /// shares (a degree-2t polynomial reconstructed at 0 from 2t+1 ≤ n points). Shares must be index-aligned.</summary>
    public static byte[] Pross((int X, byte[] Y)[] a, (int X, byte[] Y)[] b)
    {
        if (a.Length != b.Length) throw new ArgumentException("share count mismatch");
        var prod = new (int X, byte[] Y)[a.Length];
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i].X != b[i].X) throw new ArgumentException("share indices not aligned");
            prod[i] = (a[i].X, Secp256k1.ScalarMulModN(a[i].Y, b[i].Y));
        }
        return ThresholdSharing.Reconstruct(prod);
    }

    /// <summary>INVSS — shares of k⁻¹ without revealing k. Blind with a fresh secret b (JVRSS), publish u = k·b
    /// (PROSS), then each k⁻¹ share = u⁻¹·b_i. The result is a degree-t sharing of k⁻¹ (value at 0 = k⁻¹).</summary>
    public static (int X, byte[] Y)[] Invss((int X, byte[] Y)[] kShares, int n, int degree)
    {
        var b = Jvrss(n, degree);
        var u = Pross(kShares, b.Shares);                 // public scalar k·b
        if (IsZero(u)) throw new InvalidOperationException("degenerate blinding (k·b = 0)");
        var uInv = Secp256k1.ScalarInverse(u);
        var inv = new (int X, byte[] Y)[kShares.Length];
        for (int i = 0; i < kShares.Length; i++)
            inv[i] = (kShares[i].X, Secp256k1.ScalarMulModN(uInv, b.Shares[i].Y));
        return inv;
    }

    /// <summary>
    /// Assemble a threshold signature from FULLY DISTRIBUTED shares — the key shares a_i, the ephemeral-key
    /// shares k_i with their joint public key k·G, and blinding shares b_i, each produced by a real DKG
    /// (<see cref="DistributedKeyGen"/>) rather than the local simulation in <see cref="Jvrss"/>. In the live
    /// protocol every party computes its own product-share (k_i·b_i) and signature-share locally and broadcasts
    /// only those; this is the public combination: u = k·b (PROSS), k⁻¹_i = u⁻¹·b_i, s_i = (e + r·a_i)·k⁻¹_i,
    /// s = interpolate(s_i) = k⁻¹(e + a·r). Returns null if r or s degenerate (caller retries with fresh
    /// ephemeral/blinding DKGs). The result verifies against a·G with the ordinary verifier.
    /// </summary>
    public static byte[]? AssembleSignature(byte[] digest32, (int X, byte[] Y)[] aShares, (int X, byte[] Y)[] kShares, byte[] kPublicKey, (int X, byte[] Y)[] bShares)
    {
        if (digest32 is not { Length: 32 }) throw new ArgumentException("digest must be 32 bytes");
        var e = Secp256k1.ScalarMulModN(digest32, One());
        var r = Secp256k1.ScalarMulModN(kPublicKey.AsSpan(1, 32).ToArray(), One());   // x(k·G) mod n
        if (IsZero(r)) return null;
        var u = Pross(kShares, bShares);                                              // public k·b
        if (IsZero(u)) return null;
        var uInv = Secp256k1.ScalarInverse(u);
        var sShares = new (int X, byte[] Y)[aShares.Length];
        for (int i = 0; i < aShares.Length; i++)
        {
            var kInv = Secp256k1.ScalarMulModN(uInv, bShares[i].Y);                    // k⁻¹ share
            var rai = Secp256k1.ScalarMulModN(r, aShares[i].Y);
            var era = Secp256k1.ScalarFieldAddModN(e, rai);                            // e + r·a_i
            sShares[i] = (aShares[i].X, Secp256k1.ScalarMulModN(era, kInv));
        }
        var s = ThresholdSharing.Reconstruct(sShares);
        if (IsZero(s)) return null;
        s = Secp256k1.LowS(s);
        var sig = new byte[64]; r.CopyTo(sig, 0); s.CopyTo(sig, 32);
        return sig;
    }

    /// <summary>Jointly sign a 32-byte digest with the shared key — a 64-byte (r‖s) low-S signature that
    /// verifies against <paramref name="key"/>.PublicKey with the standard verifier. No party sees a or k.</summary>
    public static byte[] SignDigest(byte[] digest32, Shared key)
    {
        if (digest32 is not { Length: 32 }) throw new ArgumentException("digest must be 32 bytes");
        var e = Secp256k1.ScalarMulModN(digest32, One());     // e mod n
        int n = key.N, t = key.Degree;
        for (int attempt = 0; attempt < 64; attempt++)
        {
            var k = Jvrss(n, t);                              // fresh ephemeral key per attempt
            var r = Secp256k1.ScalarMulModN(k.PublicKey.AsSpan(1, 32).ToArray(), One());  // x(k·G) mod n
            if (IsZero(r)) continue;
            (int X, byte[] Y)[] kInv;
            try { kInv = Invss(k.Shares, n, t); } catch { continue; }
            var sShares = new (int X, byte[] Y)[n];
            for (int i = 0; i < n; i++)
            {
                var rai = Secp256k1.ScalarMulModN(r, key.Shares[i].Y);          // r·a_i
                var era = Secp256k1.ScalarFieldAddModN(e, rai);                 // e + r·a_i
                sShares[i] = (key.Shares[i].X, Secp256k1.ScalarMulModN(era, kInv[i].Y));
            }
            var s = ThresholdSharing.Reconstruct(sShares);    // = k⁻¹(e + a·r)
            if (IsZero(s)) continue;
            s = Secp256k1.LowS(s);
            var sig = new byte[64];
            r.CopyTo(sig, 0); s.CopyTo(sig, 32);
            return sig;
        }
        throw new InvalidOperationException("threshold signing failed to find a valid (r,s)");
    }
}

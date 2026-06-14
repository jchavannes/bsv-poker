using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using BsvPoker.Crypto;

namespace BsvPoker.Tests;

/// <summary>
/// Hostile-edge hardening for the crypto primitives, written for a state-level adversary: every malformed
/// input must be REJECTED (throw or fail-closed), never silently mis-accepted and never throw an
/// unexpected exception type. Covers secp256k1 (point/scalar validation, low-S, ECDH symmetry, the
/// a·(b·G)==b·(a·G) commutativity at the heart of the deal, and the d=1/d=2 vectors already pinned in
/// Secp256k1Tests), AEAD (every-field tamper + wrong key + empty plaintext), the hash primitives (against
/// the standard pinned vectors and the code's own hash160 == ripemd160(sha256(x)) composition), and
/// Base58Check (round-trip + corrupt-checksum rejection). No digest or signature is hardcoded unless it is
/// a published standard vector already trusted by the existing suite.
/// </summary>
public static class CryptoHardeningTests
{
    // The curve order n and n/2 (low-S boundary). n is public (it appears, byte-for-byte, in Secp256k1).
    private static readonly BigInteger N =
        BigInteger.Parse("00FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFEBAAEDCE6AF48A03BBFD25E8CD0364141",
            System.Globalization.NumberStyles.HexNumber);
    private static readonly BigInteger HalfN = N / 2;

    private static byte[] To32(BigInteger v)
    {
        var raw = v.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (raw.Length == 32) return raw;
        var outb = new byte[32];
        Array.Copy(raw, 0, outb, 32 - raw.Length, raw.Length);
        return outb;
    }
    private static BigInteger SOf(byte[] sig64) => new(sig64[32..], isUnsigned: true, isBigEndian: true);

    public static void All()
    {
        Console.WriteLine("crypto hardening (hostile-edge rejection vs a state-level attacker):");

        // ---------------------------------------------------------------- secp256k1: point validation
        T.Run("point validation: infinity, off-curve x, wrong-length, bad prefix all rejected", () =>
        {
            var good = Secp256k1.GenerateKeyPair().Pub;
            T.True(Secp256k1.IsValidPoint(good), "a real compressed pubkey is on the curve");

            // The point at infinity has no SEC-1 compressed encoding: an all-zero/0x00 blob must fail.
            T.False(Secp256k1.IsValidPoint(new byte[33]), "all-zero blob (no valid prefix) rejected");
            var inf = new byte[33]; inf[0] = 0x02; // x = 0 — there is no curve point with x = 0
            T.False(Secp256k1.IsValidPoint(inf), "x=0 (would-be infinity) is not on the curve");

            // x = p (field prime) is out of range; x just below has (almost surely) no even/odd-y point used here.
            var xEqP = new byte[33]; xEqP[0] = 0x02;
            T.Bytes("FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFEFFFFFC2F").CopyTo(xEqP, 1);
            T.False(Secp256k1.IsValidPoint(xEqP), "x == p is out of field range");

            // Wrong length (32 and 65 and empty) and an invalid prefix (0x04 uncompressed, 0x05) rejected.
            T.False(Secp256k1.IsValidPoint(new byte[32]), "32-byte blob is not a 33-byte compressed point");
            T.False(Secp256k1.IsValidPoint(new byte[65]), "65-byte (uncompressed-length) blob rejected");
            T.False(Secp256k1.IsValidPoint(Array.Empty<byte>()), "empty blob rejected");
            var badPrefix = (byte[])good.Clone(); badPrefix[0] = 0x04;
            T.False(Secp256k1.IsValidPoint(badPrefix), "0x04 prefix on a 33-byte blob rejected");
            badPrefix[0] = 0x00;
            T.False(Secp256k1.IsValidPoint(badPrefix), "0x00 prefix rejected");

            // An x value whose y² is a non-residue (no point exists): flip the real x to an off-curve one.
            // Take a real point's x but assert the OTHER parity does still decode (both parities are valid),
            // then corrupt x by one bit until IsValidPoint reports a non-point at least once.
            var offCurveFound = false;
            for (byte bit = 0; bit < 8 && !offCurveFound; bit++)
            {
                var probe = (byte[])good.Clone();
                probe[1] ^= (byte)(1 << bit);
                if (!Secp256k1.IsValidPoint(probe)) offCurveFound = true;
            }
            T.True(offCurveFound, "at least one single-bit x corruption yields an off-curve (rejected) point");
        });

        // Hostile API surface: every method that decompresses a peer point must reject the bad ones too,
        // not just the IsValidPoint predicate. A state attacker will feed a forged point to Ecdh/PointMul.
        T.Run("Ecdh / PointMul / PointAddCompressed throw on a malformed peer point", () =>
        {
            var k = Secp256k1.GenerateKeyPair();
            var inf = new byte[33]; inf[0] = 0x02; // x=0, off curve
            T.Throws(() => Secp256k1.Ecdh(k.Priv, inf), "ECDH rejects an off-curve peer point");
            T.Throws(() => Secp256k1.Ecdh(k.Priv, new byte[33]), "ECDH rejects a bad-prefix peer point");
            T.Throws(() => Secp256k1.Ecdh(k.Priv, new byte[10]), "ECDH rejects a wrong-length peer point");
            T.Throws(() => Secp256k1.PointMul(inf, k.Priv), "PointMul rejects an off-curve point");
            T.Throws(() => Secp256k1.PointAddCompressed(inf, k.Pub), "PointAddCompressed rejects an off-curve A");
            T.Throws(() => Secp256k1.PointAddCompressed(k.Pub, new byte[33]), "PointAddCompressed rejects a bad B");
        });

        // ---------------------------------------------------------------- secp256k1: scalar validation
        T.Run("scalar validation: zero, >= n, wrong-length all rejected; [1,n-1] accepted", () =>
        {
            T.True(Secp256k1.IsValidScalar(To32(BigInteger.One)), "1 is a valid scalar");
            T.True(Secp256k1.IsValidScalar(To32(N - 1)), "n-1 is a valid scalar");
            T.False(Secp256k1.IsValidScalar(new byte[32]), "zero scalar rejected");
            T.False(Secp256k1.IsValidScalar(To32(N)), "scalar == n rejected (not in [1,n-1])");
            T.False(Secp256k1.IsValidScalar(To32(N + 1)), "scalar == n+1 rejected");
            T.False(Secp256k1.IsValidScalar(Enumerable.Repeat((byte)0xff, 32).ToArray()), "scalar = 2^256-1 (> n) rejected");
            T.False(Secp256k1.IsValidScalar(new byte[31]), "31-byte scalar rejected");
            T.False(Secp256k1.IsValidScalar(new byte[33]), "33-byte scalar rejected");

            // NormalizeScalar (via PublicKeyCompressed) rejects a zero seed and a seed that reduces to zero (== n).
            T.Throws(() => Secp256k1.PublicKeyCompressed(new byte[32]), "zero private seed rejected");
            T.Throws(() => Secp256k1.PublicKeyCompressed(To32(N)), "seed == n (reduces to 0) rejected");
            T.Throws(() => Secp256k1.PublicKeyCompressed(new byte[31]), "non-32-byte seed rejected");
        });

        // ---------------------------------------------------------------- secp256k1: pinned key vectors
        T.Run("known key vectors: d=1 -> G, d=2 -> 2G (compressed) — matches the pinned suite", () =>
        {
            T.Eq(T.Hex(Secp256k1.PublicKeyCompressed(T.Seed(1))),
                 "0279be667ef9dcbbac55a06295ce870b07029bfcdb2dce28d959f2815b16f81798", "d=1 is G");
            T.Eq(T.Hex(Secp256k1.PublicKeyCompressed(T.Seed(2))),
                 "02c6047f9441ed7d6d3045406e95c07cd85c778e4b8cef3ca7abac09b95c709ee5", "d=2 is 2G");
        });

        // ---------------------------------------------------------------- secp256k1: low-S canonical
        T.Run("emitted signatures are LOW-S; the LowS helper canonicalizes a high-S variant", () =>
        {
            var msg = Encoding.UTF8.GetBytes("low-s canonical");
            // Several signatures (fresh random nonce each) must ALL be low-S.
            for (var i = 0; i < 8; i++)
            {
                var sig = Secp256k1.Sign(T.Seed(2), msg);
                T.True(SOf(sig) <= HalfN, "every emitted s is in the lower half [1, n/2]");
            }
            // Construct the high-S sibling s' = n - s (the other valid ECDSA s) and confirm LowS maps it back.
            var one = Secp256k1.Sign(T.Seed(1), msg);
            var s = SOf(one);
            var highS = N - s;                          // the high-S variant of the same signature
            T.True(highS > HalfN, "n - (low s) is indeed high-S");
            var canon = Secp256k1.LowS(To32(highS));
            T.Eq(T.Hex(canon), T.Hex(one[32..]), "LowS(n - s) == the original low s");
            // LowS is idempotent on an already-low s.
            T.Eq(T.Hex(Secp256k1.LowS(one[32..])), T.Hex(one[32..]), "LowS(low s) == s (idempotent)");
        });

        T.Run("verify is fail-closed on malformed signatures (never throws, returns false)", () =>
        {
            var msg = Encoding.UTF8.GetBytes("verify edges");
            var pub = Secp256k1.PublicKeyCompressed(T.Seed(1));
            var sig = Secp256k1.Sign(T.Seed(1), msg);
            T.True(Secp256k1.Verify(pub, msg, sig), "the genuine signature verifies");

            T.False(Secp256k1.Verify(pub, msg, new byte[63]), "63-byte sig rejected (length)");
            T.False(Secp256k1.Verify(pub, msg, new byte[65]), "65-byte sig rejected (length)");
            T.False(Secp256k1.Verify(pub, msg, new byte[64]), "all-zero sig (r=s=0) rejected");
            // r = 0, s valid  and  s = 0, r valid  and  r = n, s = n  — every out-of-range branch.
            var rZero = (byte[])sig.Clone(); Array.Clear(rZero, 0, 32);
            T.False(Secp256k1.Verify(pub, msg, rZero), "r=0 rejected");
            var sZero = (byte[])sig.Clone(); Array.Clear(sZero, 32, 32);
            T.False(Secp256k1.Verify(pub, msg, sZero), "s=0 rejected");
            var rN = (byte[])sig.Clone(); To32(N).CopyTo(rN, 0);
            T.False(Secp256k1.Verify(pub, msg, rN), "r=n rejected (>= n)");
            var sN = (byte[])sig.Clone(); To32(N).CopyTo(sN, 32);
            T.False(Secp256k1.Verify(pub, msg, sN), "s=n rejected (>= n)");
            // Malformed pubkey: must NOT throw out of Verify (Decompress throws but Verify catches → false).
            T.False(Secp256k1.Verify(new byte[33], msg, sig), "bad pubkey → false, not an exception");
            T.False(Secp256k1.Verify(new byte[10], msg, sig), "short pubkey → false, not an exception");
        });

        // ---------------------------------------------------------------- secp256k1: ECDH symmetry/edges
        T.Run("ECDH is symmetric and rejects invalid peer points / scalars", () =>
        {
            var (privA, pubA) = Secp256k1.GenerateKeyPair();
            var (privB, pubB) = Secp256k1.GenerateKeyPair();
            T.Eq(T.Hex(Secp256k1.Ecdh(privA, pubB)), T.Hex(Secp256k1.Ecdh(privB, pubA)), "priv_a·pub_b == priv_b·pub_a");
            T.Eq(Secp256k1.Ecdh(privA, pubB).Length, 32, "shared secret is 32 bytes");

            // A different peer key gives a different secret (no collapse to a constant).
            var (_, pubC) = Secp256k1.GenerateKeyPair();
            T.False(T.Hex(Secp256k1.Ecdh(privA, pubB)) == T.Hex(Secp256k1.Ecdh(privA, pubC)), "distinct peers → distinct secrets");

            // Invalid private scalar rejected.
            T.Throws(() => Secp256k1.Ecdh(new byte[32], pubB), "zero private scalar in ECDH rejected");
            T.Throws(() => Secp256k1.Ecdh(new byte[31], pubB), "wrong-length private scalar rejected");
        });

        // ---------------------------------------------------------------- secp256k1: commutativity (the deal)
        T.Run("point mul/add consistency: a·(b·G) == b·(a·G) (commutative-mask invariant)", () =>
        {
            var a = Secp256k1.GenerateKeyPair().Priv;
            var b = Secp256k1.GenerateKeyPair().Priv;
            var g = Secp256k1.PublicKeyCompressed(T.Seed(1)); // G

            var aThenB = Secp256k1.PointMul(Secp256k1.PointMul(g, b), a); // a·(b·G)
            var bThenA = Secp256k1.PointMul(Secp256k1.PointMul(g, a), b); // b·(a·G)
            T.Eq(T.Hex(aThenB), T.Hex(bThenA), "scalar multiplication commutes — masks remove in any order");

            // A mask k can be removed via its inverse: k⁻¹·(k·P) == P.
            var p = Secp256k1.GenerateKeyPair().Pub;
            var k = Secp256k1.GenerateKeyPair().Priv;
            var kInv = Secp256k1.ScalarInverse(k);
            var unmasked = Secp256k1.PointMul(Secp256k1.PointMul(p, k), kInv);
            T.Eq(T.Hex(unmasked), T.Hex(p), "k⁻¹·(k·P) == P (a mask is exactly invertible)");

            // PointAddCompressed agrees with the scalar-side: (a·G) + (b·G) == (a+b)·G.
            var aG = Secp256k1.PointMul(g, a);
            var bG = Secp256k1.PointMul(g, b);
            var sumPts = Secp256k1.PointAddCompressed(aG, bG);
            var aPlusB = Secp256k1.ScalarAddModN(a, b);
            var abG = Secp256k1.PointMul(g, aPlusB);
            T.Eq(T.Hex(sumPts), T.Hex(abG), "(a·G)+(b·G) == (a+b)·G (homomorphism)");
        });

        // ---------------------------------------------------------------- AEAD: every-field tamper
        T.Run("AEAD: tamper of nonce / ciphertext / tag / AAD each fails; wrong key fails", () =>
        {
            var key = Aead.Hkdf(T.Bytes("00112233445566778899aabbccddeeff"),
                                Encoding.ASCII.GetBytes("salt"), Encoding.ASCII.GetBytes("info"));
            var pt = Encoding.UTF8.GetBytes("state-secret payload that must stay sealed");
            var aad = Encoding.ASCII.GetBytes("channel-context");
            var blob = Aead.Seal(key, pt, aad);
            // sanity: genuine open works.
            T.Eq(Encoding.UTF8.GetString(Aead.Open(key, blob, aad)), Encoding.UTF8.GetString(pt), "genuine open round-trips");

            // layout is nonce(12) ‖ ct ‖ tag(16). Flip one byte in each region; each must fail authentication.
            var tNonce = (byte[])blob.Clone(); tNonce[0] ^= 0x01;
            T.Throws(() => Aead.Open(key, tNonce, aad), "nonce tamper rejected");
            var tCt = (byte[])blob.Clone(); tCt[12] ^= 0x01;       // first ciphertext byte
            T.Throws(() => Aead.Open(key, tCt, aad), "ciphertext tamper rejected");
            var tTag = (byte[])blob.Clone(); tTag[^1] ^= 0x01;     // last tag byte
            T.Throws(() => Aead.Open(key, tTag, aad), "tag tamper rejected");

            // AAD tamper: open with a different AAD must fail (AAD is authenticated, not encrypted).
            T.Throws(() => Aead.Open(key, blob, Encoding.ASCII.GetBytes("WRONG-context")), "AAD mismatch rejected");
            T.Throws(() => Aead.Open(key, blob), "missing AAD (was sealed with one) rejected");

            // Wrong key fails.
            var wrong = Aead.Hkdf(T.Bytes("ffeeddccbbaa99887766554433221100"),
                                  Encoding.ASCII.GetBytes("salt"), Encoding.ASCII.GetBytes("info"));
            T.Throws(() => Aead.Open(wrong, blob, aad), "wrong key rejected");

            // Truncated blob (shorter than nonce+tag) and bad key length are argument-rejected.
            T.Throws(() => Aead.Open(key, new byte[NonceTagFloor() - 1], aad), "too-short blob rejected");
            T.Throws(() => Aead.Seal(new byte[16], pt, aad), "16-byte key rejected (must be 32)");
            T.Throws(() => Aead.Open(new byte[31], blob, aad), "31-byte key rejected");
        });

        T.Run("AEAD: empty plaintext round-trips and is still authenticated", () =>
        {
            var key = Aead.Hkdf(T.Bytes("abcdef"), Encoding.ASCII.GetBytes("s"), Encoding.ASCII.GetBytes("i"));
            var blob = Aead.Seal(key, ReadOnlySpan<byte>.Empty);
            T.Eq(Aead.Open(key, blob).Length, 0, "empty plaintext seals and opens to empty");
            // even an empty-plaintext blob is integrity-protected: flip the tag → fail.
            var bad = (byte[])blob.Clone(); bad[^1] ^= 0x01;
            T.Throws(() => Aead.Open(key, bad), "empty-plaintext blob is still tag-protected");
            // empty plaintext + AAD: AAD still binds.
            var aad = Encoding.ASCII.GetBytes("bind");
            var blob2 = Aead.Seal(key, ReadOnlySpan<byte>.Empty, aad);
            T.Throws(() => Aead.Open(key, blob2, Encoding.ASCII.GetBytes("nope")), "empty-pt AAD still authenticated");
        });

        // ---------------------------------------------------------------- Hashes: known + composition
        T.Run("RIPEMD160 against the standard pinned vectors", () =>
        {
            T.Eq(T.Hex(Ripemd160.Hash(Encoding.ASCII.GetBytes(""))), "9c1185a5c5e9fc54612808977ee8f548b2258d31", "RIPEMD160(\"\")");
            T.Eq(T.Hex(Ripemd160.Hash(Encoding.ASCII.GetBytes("abc"))), "8eb208f7e05d987a9b044a8e98c6b087f15a0bfc", "RIPEMD160(\"abc\")");
            T.Eq(T.Hex(Ripemd160.Hash(Encoding.ASCII.GetBytes("message digest"))), "5d0689ef49d2fae572b881b123a85ffa21595f36", "RIPEMD160(\"message digest\")");
        });

        T.Run("SHA-256 against the NIST empty-string vector; sha256d == sha256(sha256)", () =>
        {
            // The single universally-pinned SHA-256 vector: SHA256("") (FIPS-180-4 / NIST).
            T.Eq(T.Hex(Hashes.Sha256(ReadOnlySpan<byte>.Empty)),
                 "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", "SHA256(\"\")");
            // sha256d is asserted by COMPOSITION against the code's own SHA-256 (no fabricated digest).
            var x = Encoding.UTF8.GetBytes("compose me");
            T.Eq(T.Hex(Hashes.Sha256d(x)), T.Hex(Hashes.Sha256(Hashes.Sha256(x))), "sha256d(x) == sha256(sha256(x))");
        });

        T.Run("HASH160 == RIPEMD160(SHA256(x)) by composition AND a pinned P2PKH vector", () =>
        {
            // Composition: derive the expected via the code's own primitives, no hardcoded digest.
            var x = Encoding.UTF8.GetBytes("address-me");
            T.Eq(T.Hex(Hashes.Hash160(x)), T.Hex(Ripemd160.Hash(Hashes.Sha256(x))), "hash160(x) == ripemd160(sha256(x))");
            // Pinned vector already trusted by CryptoPrimitivesTests: a known pubkey → known h160.
            var pub = T.Bytes("0250863ad64a87ae8a2fe83c1af1a8403cb53f53e486d8511dad8a04887e5b2352");
            T.Eq(T.Hex(Hashes.Hash160(pub)), "f54a5851e9372b87810a8e60cdd2e7cfd80b6e31", "known pubkey HASH160");
        });

        // ---------------------------------------------------------------- Base58Check: round-trip + corrupt
        T.Run("Base58Check round-trips and rejects a corrupted checksum / corrupted body / bad char", () =>
        {
            var payload = T.Bytes("00f54a5851e9372b87810a8e60cdd2e7cfd80b6e31"); // mainnet P2PKH payload
            var addr = Base58.CheckEncode(payload);
            T.Eq(T.Hex(Base58.CheckDecode(addr)), T.Hex(payload), "round-trips to the same payload");
            T.Eq(addr, "1PMycacnJaSqwwJqjawXBErnLsZ7RkXUAs", "matches the canonical pinned address");

            // Flip the LAST char (checksum) → must throw.
            var corruptChk = addr[..^1] + (addr[^1] == 's' ? "t" : "s");
            T.Throws(() => Base58.CheckDecode(corruptChk), "corrupt checksum rejected");
            // Flip an INTERIOR char (body) → checksum no longer matches → must throw.
            var mid = addr.Length / 2;
            var c = addr[mid];
            var repl = c == 'a' ? 'b' : 'a';
            var corruptBody = addr[..mid] + repl + addr[(mid + 1)..];
            T.Throws(() => Base58.CheckDecode(corruptBody), "corrupt body (checksum mismatch) rejected");
            // A non-alphabet character (0, O, I, l are excluded from base58) → FormatException.
            T.Throws(() => Base58.CheckDecode(addr[..^1] + "0"), "non-base58 char '0' rejected");
            // A too-short string (< 5 bytes after decode) → rejected.
            T.Throws(() => Base58.CheckDecode("1"), "too-short base58check rejected");

            // Plain Base58 (no checksum) still round-trips, including leading-zero preservation.
            var lz = T.Bytes("0000deadbeef");
            T.Eq(T.Hex(Base58.Decode(Base58.Encode(lz))), T.Hex(lz), "plain base58 preserves leading zero bytes");
        });
    }

    // nonce(12) + tag(16) = 28 — the minimum blob length Open will accept.
    private static int NonceTagFloor() => 12 + 16;
}

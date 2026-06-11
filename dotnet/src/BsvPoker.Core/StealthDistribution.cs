using BsvPoker.Crypto;

namespace BsvPoker.Core;

/// <summary>
/// Stealth-address distribution (US20240152913A1) — untraceable pooled payout. Two dealerless groups hold
/// threshold keys: group A's e (public P = e·G) funds the pool, group B's d (public Q = d·G) withdraws it.
/// Both derive the SAME shared secret c = H(e·Q) = H(d·P) by THRESHOLD ECDH (no one learns e or d), so the
/// pool is paid to a one-time stealth key A_pool = Q + c·G whose private key is (d + c) — recoverable only by
/// group B, and only as a (t+1)-of-n coalition. Spending A_pool is a THRESHOLD ECDSA signature with key
/// (d + c): the shares of (d + c) are just the shares of d with the PUBLIC scalar c added to each (same
/// degree, value-at-0 = d + c). On-chain the pool is an ordinary P2PKH — no linking script.
///
/// NOTE on data conveyance: the patent broadcasts P in an OP_RETURN so group B can derive c. This project
/// BANS OP_RETURN, so in deployment P rides in a typed PUSHDATA output (as the rest of the protocol does);
/// here we model the cryptographic core (the parties already know the counterparty public key).
/// </summary>
public static class StealthDistribution
{
    /// <summary>THRESHOLD ECDH then hash: c = H( the shared point s·P ), where s is held as threshold shares and
    /// P is the counterparty's public point. Group B calls this with d-shares and P (=e·G); group A with
    /// e-shares and Q (=d·G) — both get c = H(e·d·G), the agreed one-time secret. Hashes the 33-byte point.</summary>
    public static byte[] SharedSecret((int X, byte[] Y)[] shares, byte[] counterpartyPub)
    {
        var pts = shares.Select(sh => (sh.X, Secp256k1.PointMul(counterpartyPub, sh.Y))).ToList(); // s_i · P
        var sp = ThresholdSharing.InterpolatePointAtZero(pts);                                      // s · P
        return Hashes.Sha256(sp);                                                                   // c
    }

    /// <summary>The one-time pool stealth public key A_pool = Q + c·G (its private key is d + c).</summary>
    public static byte[] PoolKey(byte[] Q, byte[] c) => Secp256k1.PointAddCompressed(Q, Secp256k1.PublicKeyCompressed(c));

    /// <summary>The pool's locking script — an ordinary P2PKH to A_pool (no linkable stealth script on-chain).</summary>
    public static byte[] PoolLock(byte[] poolKey) => Chain.P2pkhLockForPub(poolKey);

    /// <summary>The threshold-signing handle for the pool spend key (d + c): group B's d-key with the public
    /// scalar c added to every share and to the public key (Q → Q + c·G). Feed this to
    /// <see cref="ThresholdEcdsa.SignDigest"/> to pay out the pool — no party ever holds (d + c).</summary>
    public static ThresholdEcdsa.Shared PoolSpendKey(ThresholdEcdsa.Shared dKey, byte[] c)
    {
        var shares = dKey.Shares.Select(sh => (sh.X, Secp256k1.ScalarFieldAddModN(sh.Y, c))).ToArray();
        return dKey with { Shares = shares, PublicKey = PoolKey(dKey.PublicKey, c) };
    }
}

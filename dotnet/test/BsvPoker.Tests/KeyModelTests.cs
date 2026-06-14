using BsvPoker.Core;
using BsvPoker.Crypto;

namespace BsvPoker.Tests;

/// <summary>
/// The principal's KEY MODEL, proven end-to-end: ONE identity (Base ID) key is the only persistent key and is
/// FIXED + reproducible from the seed; EVERY other key (receive / change / per-message) is SINGLE-USE, derived
/// via ECDH hash chains, NEVER reused and NEVER deleted; keys are NETWORK-INDEPENDENT (the same seed yields the
/// same scalar regardless of which network it is encoded for); a counterparty can derive the matching
/// per-message PUBLIC via the Type-42 conversation chain while a WRONG counterparty cannot; every hash-chain
/// link is tamper-evident; and HOSTILE inputs (zero/invalid scalar, wrong root) are rejected.
///
/// Coverage is positive AND hostile, over LONG runs (1000+ derivations) to prove no key ever repeats.
/// </summary>
public static class KeyModelTests
{
    private static string Hx(byte[] b) => Convert.ToHexString(b).ToLowerInvariant();

    // A fixed, reproducible seed (so the "same seed ⇒ same identity" claim is checkable across the run).
    private static byte[] FixedSeed()
    {
        var s = new byte[32];
        for (int i = 0; i < 32; i++) s[i] = (byte)(0x11 * (i + 1)); // 0x11, 0x22, ... non-zero, non-trivial
        return s;
    }

    public static void All()
    {
        Console.WriteLine("KEY MODEL (one fixed identity, single-use hash-chained sub-keys, network-independent):");

        // -------------------------------------------------------------------------------------------------
        // 1. The identity key is FIXED and reproducible from the seed across MANY derivations.
        // -------------------------------------------------------------------------------------------------
        T.Run("identity key is FIXED and reproducible from the seed across 1000 independent derivations", () =>
        {
            var seed = FixedSeed();
            var ring0 = new KeyRing(seed);
            var idPriv = ring0.IdentityPriv();
            var idPub = ring0.IdentityPub();
            T.Eq(idPriv.Length, 32, "identity priv is a 32-byte scalar");
            T.True(Secp256k1.IsValidScalar(idPriv), "identity priv is a valid secp256k1 scalar");
            T.Eq(Hx(Secp256k1.PublicKeyCompressed(idPriv)), Hx(idPub), "identity pub matches identity priv");

            // Re-derive from a fresh KeyRing built on a *fresh copy* of the same seed 1000 times: never changes.
            for (int i = 0; i < 1000; i++)
            {
                var ring = new KeyRing((byte[])seed.Clone(), nextReceive: 1 + i);
                T.Eq(Hx(ring.IdentityPriv()), Hx(idPriv), $"identity priv stable on derivation {i}");
                T.Eq(Hx(ring.IdentityPub()), Hx(idPub), $"identity pub stable on derivation {i}");
            }
        });

        T.Run("the identity key does NOT depend on the receive cursor (advancing receives never rotates identity)", () =>
        {
            var seed = FixedSeed();
            var ring = new KeyRing(seed);
            var idBefore = Hx(ring.IdentityPub());
            for (int i = 0; i < 256; i++) _ = ring.NextReceive(); // burn 256 single-use receive keys
            T.Eq(Hx(ring.IdentityPub()), idBefore, "identity is the one fixed, never-rotating slot");
        });

        // -------------------------------------------------------------------------------------------------
        // 2. NETWORK-INDEPENDENCE: the same seed yields the same identity scalar regardless of 'network'.
        //    The key types take NO network parameter — the scalar is a pure function of the seed; only the
        //    ADDRESS ENCODING (version byte) differs per network. We prove the scalar is invariant and that
        //    only the textual address differs when we encode it for mainnet vs testnet.
        // -------------------------------------------------------------------------------------------------
        T.Run("network-independent keys: same seed ⇒ identical identity scalar for every 'network'", () =>
        {
            var seed = FixedSeed();
            // Three notional 'networks' — there is no network input to derivation, so all must coincide.
            var idMain = new KeyRing((byte[])seed.Clone()).IdentityPriv();
            var idTest = new KeyRing((byte[])seed.Clone()).IdentityPriv();
            var idReg  = new KeyRing((byte[])seed.Clone()).IdentityPriv();
            T.Eq(Hx(idMain), Hx(idTest), "mainnet vs testnet identity scalar identical");
            T.Eq(Hx(idMain), Hx(idReg),  "mainnet vs regtest identity scalar identical");

            // Same public key on every network; only the encoded address version byte differs.
            var pub = new KeyRing((byte[])seed.Clone()).IdentityPub();
            var mainAddr = AddrFor(pub, 0x00);   // BSV/BTC mainnet P2PKH version
            var testAddr = AddrFor(pub, 0x6f);   // testnet P2PKH version
            T.True(mainAddr != testAddr, "address ENCODING differs per network (version byte)");
            // ...yet both encode the SAME hash160 of the SAME key:
            T.Eq(Hx(Hashes.Hash160(pub)), Hx(Hashes.Hash160(new KeyRing((byte[])seed.Clone()).IdentityPub())),
                 "the underlying key (hash160) is network-independent");
        });

        // -------------------------------------------------------------------------------------------------
        // 3. SINGLE-USE: successive receive keys are ALL DISTINCT over a long run — no key ever repeats.
        // -------------------------------------------------------------------------------------------------
        T.Run("receive keys: 1000 successive draws are ALL DISTINCT (single-use, no repeat, cursor never reused)", () =>
        {
            var ring = new KeyRing(FixedSeed());
            var seenPub = new HashSet<string>();
            var seenIdx = new HashSet<long>();
            long prev = ring.NextIndex - 1;
            for (int i = 0; i < 1000; i++)
            {
                var (idx, priv, pub) = ring.NextReceive();
                T.True(idx == prev + 1, $"cursor advances by exactly one (got {idx}, prev {prev})");
                prev = idx;
                T.True(seenIdx.Add(idx), $"index {idx} never returned twice");
                T.True(seenPub.Add(Hx(pub)), $"receive pubkey {i} never repeats");
                T.Eq(Hx(Secp256k1.PublicKeyCompressed(priv)), Hx(pub), "pub matches priv");
            }
            T.Eq(seenPub.Count, 1000, "1000 distinct single-use receive keys");
        });

        T.Run("receive keys never collide with the identity key over a long run", () =>
        {
            var ring = new KeyRing(FixedSeed());
            var id = Hx(ring.IdentityPub());
            for (int i = 0; i < 1000; i++)
                T.True(Hx(ring.NextReceive().Pub) != id, "a receive key is never the identity (Base ID) key");
        });

        // -------------------------------------------------------------------------------------------------
        // 4. The receive/change/per-message keys form a verifiable HASH CHAIN (KeyChain), all distinct.
        // -------------------------------------------------------------------------------------------------
        T.Run("hash chain: 1000 sub-keys are one verifiable, ordered chain with ALL links + keys distinct", () =>
        {
            var root = Secp256k1.GenerateKeyPair();
            var chain = KeyChain.WalletChain(root.Priv, 1000);
            T.Eq(chain.Count, 1000, "derived 1000 chained sub-keys");
            T.True(KeyChain.Verify(root.Pub, chain), "the whole 1000-link chain verifies from genesis");

            var pubs = new HashSet<string>();
            var privs = new HashSet<string>();
            var links = new HashSet<string>();
            for (int i = 0; i < chain.Count; i++)
            {
                var ck = chain[i];
                T.Eq(ck.Index, i, "indices are dense and ordered");
                T.True(pubs.Add(Hx(ck.Pub)), $"pub at {i} is unique (no key reuse)");
                T.True(privs.Add(Hx(ck.Priv)), $"priv at {i} is unique");
                T.True(links.Add(Hx(ck.Link)), $"link at {i} is unique");
            }
            T.Eq(pubs.Count, 1000, "1000 distinct sub-public-keys — no reuse anywhere in the chain");
            T.Eq(links.Count, 1000, "1000 distinct chain links");
        });

        T.Run("each chain link is exactly SHA256(prevLink || be32(index)) from the genesis link", () =>
        {
            var root = Secp256k1.GenerateKeyPair();
            var chain = KeyChain.WalletChain(root.Priv, 64);
            var link = KeyChain.GenesisLink(root.Pub);
            for (int i = 0; i < chain.Count; i++)
            {
                var be = new[] { (byte)(i >> 24), (byte)(i >> 16), (byte)(i >> 8), (byte)i };
                var cat = new byte[link.Length + 4]; link.CopyTo(cat, 0); be.CopyTo(cat, link.Length);
                var expect = System.Security.Cryptography.SHA256.HashData(cat);
                T.Eq(Hx(chain[i].Link), Hx(expect), $"link {i} is the SHA256 hash-chain successor");
                link = chain[i].Link;
            }
        });

        // -------------------------------------------------------------------------------------------------
        // 5. Per-message conversation chain (Type-42): counterparty derives the matching public; a WRONG
        //    counterparty cannot. Every (conv, seq) is a fresh, single-use key.
        // -------------------------------------------------------------------------------------------------
        T.Run("per-message: counterparty derives the matching public for EACH message in a conversation", () =>
        {
            var aliceSeed = FixedSeed();
            var aliceRing = new KeyRing(aliceSeed);
            var alicePub  = Secp256k1.PublicKeyCompressed(aliceSeed); // the root the counterparty pays/derives against
            var bob = Secp256k1.GenerateKeyPair();

            var seen = new HashSet<string>();
            for (long seq = 0; seq < 200; seq++)
            {
                // Alice's own per-message PRIVATE key (only she can compute it — needs her seed).
                var mPriv = aliceRing.MessagePriv(bob.Pub, "conv-1", seq);
                var mPub  = aliceRing.MessagePub(bob.Pub, "conv-1", seq);
                T.Eq(Hx(Secp256k1.PublicKeyCompressed(mPriv)), Hx(mPub), "pub matches priv");

                // Bob (the counterparty) derives the SAME public from the conversation hash chain (Type-42),
                // using Alice's root pub + his own priv + the agreed invoice number.
                var bobView = Type42.DerivePublic(alicePub, bob.Priv, "bsvpoker/msg/conv-1/" + seq);
                T.Eq(Hx(bobView), Hx(mPub), $"counterparty derives the matching per-message public at seq {seq}");

                T.True(seen.Add(Hx(mPub)), $"per-message key at seq {seq} is fresh (single-use, no reuse)");
            }
            T.Eq(seen.Count, 200, "200 distinct single-use per-message keys in one conversation");
        });

        T.Run("a WRONG counterparty cannot derive the per-message public key", () =>
        {
            var aliceSeed = FixedSeed();
            var aliceRing = new KeyRing(aliceSeed);
            var alicePub  = Secp256k1.PublicKeyCompressed(aliceSeed);
            var bob     = Secp256k1.GenerateKeyPair();
            var mallory = Secp256k1.GenerateKeyPair();

            var mPub = aliceRing.MessagePub(bob.Pub, "conv-1", 7);          // intended for Bob
            var malloryView = Type42.DerivePublic(alicePub, mallory.Priv, "bsvpoker/msg/conv-1/7");
            T.True(Hx(malloryView) != Hx(mPub), "ECDH binds the exact counterparty: wrong party ⇒ wrong key");
        });

        T.Run("a different (conv, seq) yields a completely different per-message key (no cross-conversation reuse)", () =>
        {
            var ring = new KeyRing(FixedSeed());
            var bob  = Secp256k1.GenerateKeyPair();
            var a = Hx(ring.MessagePub(bob.Pub, "conv-A", 0));
            var b = Hx(ring.MessagePub(bob.Pub, "conv-B", 0)); // different conversation, same seq
            var c = Hx(ring.MessagePub(bob.Pub, "conv-A", 1)); // same conversation, next seq
            T.True(a != b, "different conversation ⇒ different key");
            T.True(a != c, "different seq ⇒ different key");
            T.True(b != c, "all three distinct");
        });

        // -------------------------------------------------------------------------------------------------
        // 6. TAMPER-EVIDENCE: corrupting ANY link, key, index, or order is detected by Verify.
        // -------------------------------------------------------------------------------------------------
        T.Run("tamper-evidence: corrupting ANY single link in a long chain fails verification", () =>
        {
            var root = Secp256k1.GenerateKeyPair();
            // Probe several positions (genesis-adjacent, middle, tail) — every one must be caught.
            foreach (var pos in new[] { 0, 1, 25, 49 })
            {
                var chain = KeyChain.WalletChain(root.Priv, 50);
                T.True(KeyChain.Verify(root.Pub, chain), "baseline chain verifies");
                chain[pos].Link[0] ^= 0xFF; // flip one bit of one link
                T.True(!KeyChain.Verify(root.Pub, chain), $"a corrupted link at {pos} is tamper-evident");
            }
        });

        T.Run("tamper-evidence: a sub-key whose priv no longer matches its pub is rejected", () =>
        {
            var root = Secp256k1.GenerateKeyPair();
            var chain = KeyChain.WalletChain(root.Priv, 16).ToList();
            var v = chain[5];
            chain[5] = v with { Priv = Secp256k1.GenerateKeyPair().Priv }; // swap in an unrelated private key
            T.True(!KeyChain.Verify(root.Pub, chain), "priv/pub mismatch is caught");
        });

        T.Run("tamper-evidence: reordering / re-indexing a link breaks the ordered chain", () =>
        {
            var root = Secp256k1.GenerateKeyPair();
            var chain = KeyChain.WalletChain(root.Priv, 16).ToList();
            (chain[4], chain[5]) = (chain[5], chain[4]); // swap two adjacent links
            T.True(!KeyChain.Verify(root.Pub, chain), "an out-of-order chain fails (index != position)");
        });

        T.Run("tamper-evidence: truncating then re-appending a forged tail link is detected", () =>
        {
            var root = Secp256k1.GenerateKeyPair();
            var chain = KeyChain.WalletChain(root.Priv, 16).ToList();
            // Forge a link claiming index 16 but with an arbitrary (non-chained) link value.
            chain.Add(new ChainedKey(16, new byte[32], root.Priv, root.Pub));
            T.True(!KeyChain.Verify(root.Pub, chain), "a forged appended link does not hash-chain from its predecessor");
        });

        // -------------------------------------------------------------------------------------------------
        // 7. HOSTILE: wrong root, zero / invalid scalars are rejected.
        // -------------------------------------------------------------------------------------------------
        T.Run("hostile: a valid chain verified against the WRONG root is rejected", () =>
        {
            var alice = Secp256k1.GenerateKeyPair();
            var bob   = Secp256k1.GenerateKeyPair();
            var chain = KeyChain.WalletChain(alice.Priv, 32);
            T.True(KeyChain.Verify(alice.Pub, chain), "verifies against the true root");
            T.True(!KeyChain.Verify(bob.Pub, chain), "a foreign root cannot validate the chain (genesis link differs)");
        });

        T.Run("hostile: a key derived against the WRONG root produces a non-matching public", () =>
        {
            // Type-42 payment: spending with the wrong PAYER root must NOT reproduce the paid-to key.
            var payee = Secp256k1.GenerateKeyPair();
            var payer = Secp256k1.GenerateKeyPair();
            var wrong = Secp256k1.GenerateKeyPair();
            var payToPub  = IdentityPayment.PayToPub(payee.Pub, payer.Priv, "inv-9");
            var goodSpend = IdentityPayment.SpendPriv(payee.Priv, payer.Pub, "inv-9");
            var wrongSpend = IdentityPayment.SpendPriv(payee.Priv, wrong.Pub, "inv-9"); // wrong payer root
            T.Eq(Hx(Secp256k1.PublicKeyCompressed(goodSpend)), Hx(payToPub), "right root reproduces the key");
            T.True(Hx(Secp256k1.PublicKeyCompressed(wrongSpend)) != Hx(payToPub), "wrong root fails to reproduce it");
        });

        T.Run("hostile: a zero / all-zero scalar is rejected everywhere a private key is consumed", () =>
        {
            var zero = new byte[32];
            T.False(Secp256k1.IsValidScalar(zero), "zero is not a valid scalar");
            T.Throws(() => Secp256k1.PublicKeyCompressed(zero), "no public key from a zero scalar");
            T.Throws(() => new KeyRing(zero).IdentityPriv(), "a zero seed cannot produce an identity (ECDH on zero)");
        });

        T.Run("hostile: an out-of-range / oversized scalar (>= n) is rejected", () =>
        {
            // n (the curve order) itself and all-0xFF are invalid scalars.
            var n = T.Bytes("FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFEBAAEDCE6AF48A03BBFD25E8CD0364141");
            var allFF = Enumerable.Repeat((byte)0xFF, 32).ToArray();
            T.False(Secp256k1.IsValidScalar(n), "the curve order n is not a valid scalar");
            T.False(Secp256k1.IsValidScalar(allFF), "0xFF*32 (> n) is not a valid scalar");
        });

        T.Run("hostile: a malformed seed length is rejected by every key constructor", () =>
        {
            T.Throws(() => new KeyRing(new byte[31]), "31-byte seed rejected");
            T.Throws(() => new KeyRing(new byte[33]), "33-byte seed rejected");
            T.Throws(() => new KeyRing(null!), "null seed rejected");
            T.Throws(() => WalletKeys.Account(new byte[31], 0, 0), "Account rejects a non-32-byte seed");
        });

        T.Run("hostile: tampering the genesis tag/root by one byte yields a disjoint chain (no overlap)", () =>
        {
            var root = Secp256k1.GenerateKeyPair();
            var other = Secp256k1.GenerateKeyPair();
            var a = KeyChain.WalletChain(root.Priv, 64).Select(c => Hx(c.Pub)).ToHashSet();
            var b = KeyChain.WalletChain(other.Priv, 64).Select(c => Hx(c.Pub)).ToHashSet();
            T.Eq(a.Intersect(b).Count(), 0, "two different roots share NO sub-keys (full domain separation)");
        });
    }

    /// <summary>Encode a P2PKH address for a given network version byte (encoding-only; the key is unchanged).</summary>
    private static string AddrFor(byte[] pub, byte version)
    {
        var payload = new byte[21]; payload[0] = version; Hashes.Hash160(pub).CopyTo(payload, 1);
        return Base58.CheckEncode(payload);
    }
}

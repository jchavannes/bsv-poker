using BsvPoker.Core;
using BsvPoker.Crypto;

namespace BsvPoker.Tests;

/// <summary>
/// Distributed dealerless key generation (the real-peer DKG): n players each deal a secret polynomial,
/// broadcast Feldman commitments, and send every peer a SEALED share. Each finalizes by opening + verifying
/// the shares dealt to it and summing them into a joint key share. Proven: all players derive the SAME joint
/// public key with no dealer; any t+1 joint shares reconstruct the joint secret (and a·G == that key); a peer
/// who cannot open a share learns nothing; and a LYING dealer (share inconsistent with its commitments) is
/// provably caught and blamed by name.
/// </summary>
public static class DistributedKeyGenTests
{
    public static void All()
    {
        Console.WriteLine("distributed dealerless key generation (real-peer DKG over sealed shares):");

        T.Run("3 parties run the DKG: all agree on ONE joint public key, no dealer; t+1 shares reconstruct it", () =>
        {
            const int n = 3, t = 1;
            var kp = Enumerable.Range(1, n).ToDictionary(i => i, _ => Secp256k1.GenerateKeyPair());
            var pubs = kp.ToDictionary(k => k.Key, k => k.Value.Pub);

            var dealt = kp.ToDictionary(k => k.Key, k => DistributedKeyGen.Deal(k.Key, k.Value.Pub, t, pubs));
            var round1 = dealt.Values.Select(d => d.Msg).ToList();

            var results = kp.ToDictionary(k => k.Key, k => DistributedKeyGen.Finalize(k.Key, k.Value.Priv, round1));
            foreach (var (idx, r) in results) T.True(r.Blame == null, $"party {idx} finalized without blame");

            var pub1 = results[1].JointPublicKey;
            T.True(results.Values.All(r => r.JointPublicKey.SequenceEqual(pub1)), "every party derives the SAME joint public key");

            // joint pubkey equals Σ a_0·G computed independently from the dealers' polynomials
            var expectedPub = ThresholdSharing.PublicKey(dealt.Values.Select(d => d.Poly).ToList());
            T.True(pub1.SequenceEqual(expectedPub), "joint key == Σ (each dealer's free term)·G");

            // any t+1 = 2 joint shares reconstruct the joint secret, and secret·G == the joint key
            var secret = ThresholdSharing.Reconstruct(new[] { (1, results[1].JointShare), (2, results[2].JointShare) });
            T.True(Secp256k1.PublicKeyCompressed(secret).SequenceEqual(pub1), "t+1 joint shares reconstruct the secret a, and a·G == joint key");
            // a different pair reconstructs the SAME secret (consistency)
            var secret2 = ThresholdSharing.Reconstruct(new[] { (2, results[2].JointShare), (3, results[3].JointShare) });
            T.True(secret.SequenceEqual(secret2), "every t+1 subset reconstructs the same secret");
        });

        T.Run("the joint key signs with the assembled shares (threshold ECDSA over a DKG'd key) and verifies", () =>
        {
            const int n = 3, t = 1;
            var kp = Enumerable.Range(1, n).ToDictionary(i => i, _ => Secp256k1.GenerateKeyPair());
            var pubs = kp.ToDictionary(k => k.Key, k => k.Value.Pub);
            var dealt = kp.ToDictionary(k => k.Key, k => DistributedKeyGen.Deal(k.Key, k.Value.Pub, t, pubs));
            var round1 = dealt.Values.Select(d => d.Msg).ToList();
            var results = kp.ToDictionary(k => k.Key, k => DistributedKeyGen.Finalize(k.Key, k.Value.Priv, round1));

            // assemble the DKG'd key into a ThresholdEcdsa handle and threshold-sign
            var shares = results.OrderBy(r => r.Key).Select(r => (r.Key, r.Value.JointShare)).ToArray();
            var key = new ThresholdEcdsa.Shared(dealt.Values.Select(d => d.Poly).ToList(), shares, results[1].JointPublicKey, n, t);
            var digest = Hashes.Sha256(System.Text.Encoding.UTF8.GetBytes("spend the DKG-controlled pot"));
            var sig = ThresholdEcdsa.SignDigest(digest, key);
            T.True(Secp256k1.VerifyDigest(key.PublicKey, digest, sig), "a signature over the DKG'd key verifies against it");
        });

        T.Run("a LYING dealer (share inconsistent with its commitments) is caught and BLAMED by name", () =>
        {
            const int n = 3, t = 1;
            var kp = Enumerable.Range(1, n).ToDictionary(i => i, _ => Secp256k1.GenerateKeyPair());
            var pubs = kp.ToDictionary(k => k.Key, k => k.Value.Pub);
            var honest = kp.ToDictionary(k => k.Key, k => DistributedKeyGen.Deal(k.Key, k.Value.Pub, t, pubs));

            // dealer 2 cheats: it publishes honest commitments but seals a BOGUS share to party 1
            var cheatMsg = honest[2].Msg;
            var bogus = cheatMsg.SealedShares.ToDictionary(x => x.Key, x => x.Value);
            bogus[1] = DistributedKeyGen.SealScalar(MentalPokerEC.NewScalar(), pubs[1]); // wrong share for party 1
            var tampered = cheatMsg with { SealedShares = bogus };

            var round1 = new[] { honest[1].Msg, tampered, honest[3].Msg };
            var r1 = DistributedKeyGen.Finalize(1, kp[1].Priv, round1);
            T.Eq(r1.Blame ?? -99, 2, "party 1 blames dealer 2 (Feldman verification fails on the bogus share)");
            // party 3 got an honest share from dealer 2, so it finalizes fine
            var r3 = DistributedKeyGen.Finalize(3, kp[3].Priv, round1);
            T.True(r3.Blame == null, "party 3 (honest share from dealer 2) finalizes without blame");
        });

        T.Run("a non-recipient cannot open a share sealed to someone else", () =>
        {
            var alice = Secp256k1.GenerateKeyPair();
            var eve = Secp256k1.GenerateKeyPair();
            var sealed_ = DistributedKeyGen.SealScalar(MentalPokerEC.NewScalar(), alice.Pub);
            bool eveOpened; try { DistributedKeyGen.OpenScalar(sealed_, eve.Priv); eveOpened = true; } catch { eveOpened = false; }
            T.False(eveOpened, "Eve cannot open a share sealed to Alice");
            T.Eq(DistributedKeyGen.OpenScalar(sealed_, alice.Priv).Length, 32, "Alice opens her own share (32-byte scalar)");
        });
    }
}

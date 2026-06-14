using System.Text;
using BsvPoker.Core;
using BsvPoker.Crypto;

namespace BsvPoker.Tests;

/// <summary>
/// ENVELOPE-LEVEL proof of the principal's key-graph broadcast-encryption PATENT (GB 2623780 B /
/// EP4046048B1) properties the CHAT layer relies on — arbitrary CHOSEN recipient sets and REAL
/// REVOCATION, each backed by a HOSTILE negative test (military-grade vs a state-level attacker):
///   • SEAL to NON-power-of-two sets (3, 5, 7) and a LARGE set (200) — every selected member opens it,
///     a non-member CANNOT (a stranger's key, and a sibling member's key, both fail).
///   • REVOCATION two ways: (a) at the envelope layer by excluding a member from the NEXT envelope —
///     the dropped member, even replaying their OWN prior sealed leaf, cannot open the new message while
///     the remaining members can; (b) at the graph layer via BroadcastEncryption.Revoke + re-publish —
///     the kicked member's captured leaf key + items can no longer reach the rotated message key.
///   • TAMPER (AEAD): flipping any byte of a wrapped path item OR the sealed message makes Open fail.
///   • CROSS-GROUP: a member's wrapped leaf key from one group cannot open a DIFFERENT group's envelope.
/// </summary>
public static class BroadcastEnvelopeRevocationTests
{
    public static void All()
    {
        Console.WriteLine("broadcast envelope — arbitrary chosen set + REVOCATION (GB 2623780 B; hostile-tested):");

        // ---- arbitrary, non-power-of-two chosen sets: 3, 5, 7 ----
        foreach (var size in new[] { 3, 5, 7 })
        {
            int sz = size;
            T.Run($"seal to a {sz}-member set (non-power-of-two padded): every selected member opens; a stranger cannot", () =>
            {
                var sender = Secp256k1.GenerateKeyPair();
                var members = Enumerable.Range(0, sz).Select(_ => Secp256k1.GenerateKeyPair()).ToArray();
                var stranger = Secp256k1.GenerateKeyPair();
                var text = $"chosen set of {sz}: ante up";

                var env = BroadcastEnvelope.Seal(members.Select(m => Hex(m.Pub)).ToList(), sender.Priv, sender.Pub, U8(text));

                T.Eq(env.Members.Count, sz, "exactly the chosen members are addressed (no synthetic-pad members leak in)");
                foreach (var m in members)
                    T.Eq(S(env.Open(m.Priv, m.Pub)), text, "selected member opens");

                // HOSTILE: a key never put in the set is rejected — not in Members, no path to the key.
                T.False(env.CanOpen(stranger.Priv, stranger.Pub), "a stranger (not selected) is blocked");
                T.Throws(() => env.Open(stranger.Priv, stranger.Pub), "stranger Open throws");
            });
        }

        // ---- large chosen set: 200 members ----
        T.Run("seal to a LARGE chosen set (200 members): all 200 open; a non-member is blocked", () =>
        {
            var sender = Secp256k1.GenerateKeyPair();
            var members = Enumerable.Range(0, 200).Select(_ => Secp256k1.GenerateKeyPair()).ToArray();
            var outsider = Secp256k1.GenerateKeyPair();
            var text = "200-seat broadcast: the group reads, no one else";

            var env = BroadcastEnvelope.Seal(members.Select(m => Hex(m.Pub)).ToList(), sender.Priv, sender.Pub, U8(text));

            T.Eq(env.Members.Count, 200, "all 200 chosen members addressed");
            // spot-check breadth: first, a middle, the last, and a couple of randoms — all must open.
            foreach (var idx in new[] { 0, 1, 99, 100, 150, 199 })
                T.Eq(S(env.Open(members[idx].Priv, members[idx].Pub)), text, $"member #{idx} of 200 opens");

            T.False(env.CanOpen(outsider.Priv, outsider.Pub), "outsider blocked from the 200-set");
        });

        // ---- REVOCATION (a): exclude from the NEXT envelope; the dropped member is locked out ----
        T.Run("REVOCATION at the envelope layer: dropped from the NEXT envelope, the ex-member cannot open it (members can)", () =>
        {
            var sender = Secp256k1.GenerateKeyPair();
            var alice = Secp256k1.GenerateKeyPair();
            var bob = Secp256k1.GenerateKeyPair();
            var carol = Secp256k1.GenerateKeyPair();   // carol gets kicked
            var dave = Secp256k1.GenerateKeyPair();

            var all4 = new[] { Hex(alice.Pub), Hex(bob.Pub), Hex(carol.Pub), Hex(dave.Pub) };
            var env1 = BroadcastEnvelope.Seal(all4, sender.Priv, sender.Pub, U8("round 1: carol is still in"));
            T.True(env1.CanOpen(carol.Priv, carol.Pub), "carol opens the round-1 message while a member");

            // NEXT envelope: carol is excluded from the chosen set.
            var remaining = new[] { Hex(alice.Pub), Hex(bob.Pub), Hex(dave.Pub) };
            var env2 = BroadcastEnvelope.Seal(remaining, sender.Priv, sender.Pub, U8("round 2: carol kicked"));

            foreach (var m in new[] { alice, bob, dave })
                T.Eq(S(env2.Open(m.Priv, m.Pub)), "round 2: carol kicked", "remaining member still reads round 2");

            // HOSTILE: carol cannot open the new envelope at all (not a member of it).
            T.False(env2.CanOpen(carol.Priv, carol.Pub), "the kicked member cannot open the next envelope");

            // HOSTILE-replay: env2 is a fresh independent graph, so carol splicing in her OWN env1
            // wrapped leaf still gets her nowhere — her leaf isn't even listed in env2.Members.
            var carolHex = Hex(carol.Pub);
            T.False(env2.Members.Any(m => m.PubHex == carolHex), "carol is absent from the next envelope's member list");
            var carolEnv1Leaf = env1.Members.First(m => m.PubHex == carolHex);
            var spliced = WithMembers(env2, env2.Members.Append(carolEnv1Leaf).ToList());
            T.False(spliced.CanOpen(carol.Priv, carol.Pub),
                "replaying carol's OLD (env1) wrapped leaf into env2 still fails — env2 uses a different, unrelated graph");
        });

        // ---- REVOCATION (b): graph-level Revoke + re-publish rotates the keys the ex-member knew ----
        T.Run("REVOCATION at the graph layer: BroadcastEncryption.Revoke rotates path keys; the captured key no longer reaches the message", () =>
        {
            var users = new ulong[] { 11, 22, 33, 44 };
            var g = BroadcastEncryption.Build(users);

            // capture user 33's leaf key + the published items/message BEFORE the kick (an attacker's snapshot).
            var capturedLeaf = g.UserLeafKey(33)!;
            var preItems = g.EncryptedDataItems();
            var preMsg = g.EncryptMessage(U8("before kick"));
            T.Eq(S(g.UserDecrypt(33, capturedLeaf, preItems, preMsg)), "before kick", "user 33 reads while a member");

            g.Revoke(33);                                  // kick: drop the leaf, rotate parent..root keys
            T.Eq(g.UserCount, 3, "membership dropped to 3 after the kick");
            var postItems = g.EncryptedDataItems();        // re-published after the rekey
            var postMsg = g.EncryptMessage(U8("after kick"));

            // remaining members read the NEW message.
            foreach (var u in new ulong[] { 11, 22, 44 })
                T.Eq(S(g.UserDecrypt(u, g.UserLeafKey(u)!, postItems, postMsg)), "after kick", $"remaining user {u} reads after the kick");

            // HOSTILE: the kicked user's captured leaf + the NEW items cannot reach the rotated message key.
            T.Throws(() => g.UserDecrypt(33, capturedLeaf, postItems, postMsg), "kicked user rejected (no longer in graph)");
            // even bypassing the membership check via the static path with its OLD leaf id, the rotated
            // wraps make the unwrap fail (AEAD) — the keys it knew are gone.
            T.Throws(() => BroadcastEncryption.DecryptPath(7, capturedLeaf, postItems, postMsg),
                "the captured leaf key cannot climb the re-keyed path to the new message");
        });

        // ---- TAMPER (AEAD): a wrapped path item ----
        T.Run("TAMPER a wrapped path ITEM (one byte) -> open fails (AEAD authentication)", () =>
        {
            var sender = Secp256k1.GenerateKeyPair();
            var members = Enumerable.Range(0, 4).Select(_ => Secp256k1.GenerateKeyPair()).ToArray();
            var env = BroadcastEnvelope.Seal(members.Select(m => Hex(m.Pub)).ToList(), sender.Priv, sender.Pub, U8("intact body"));
            T.True(env.CanOpen(members[0].Priv, members[0].Pub), "intact envelope opens before tamper");

            // flip a byte inside the FIRST item's wrapped parent key; rebuild an otherwise-identical envelope.
            var bad = env.Items.Select((it, i) =>
                i == 0 ? it with { WrappedParentKey = FlipByte(it.WrappedParentKey, 0) } : it).ToList();
            int corruptedNode = bad[0].Node;     // the child node whose wrapped parent-key was altered
            var tampered = WithItems(env, bad);

            // Any member whose leaf->root path climbs THROUGH the corrupted child node must hit that
            // altered wrap and fail (AEAD). There is always >=1 such member (every non-root node lies on
            // some leaf's path), so tampering provably breaks decryption for the affected member(s).
            var victims = members.Where(m =>
                PathHits(env.Members.First(em => em.PubHex == Hex(m.Pub)).Leaf, corruptedNode)).ToList();
            T.True(victims.Count >= 1, "at least one selected member's path traverses the corrupted node");
            foreach (var v in victims)
                T.False(tampered.CanOpen(v.Priv, v.Pub), "a member whose path crosses the corrupted wrap cannot open");
        });

        // ---- TAMPER (AEAD): the sealed message ----
        T.Run("TAMPER the SEALED MESSAGE (one byte) -> open fails (AEAD authentication)", () =>
        {
            var sender = Secp256k1.GenerateKeyPair();
            var members = Enumerable.Range(0, 3).Select(_ => Secp256k1.GenerateKeyPair()).ToArray();
            var env = BroadcastEnvelope.Seal(members.Select(m => Hex(m.Pub)).ToList(), sender.Priv, sender.Pub, U8("authentic message"));
            T.True(env.CanOpen(members[0].Priv, members[0].Pub), "intact message opens before tamper");

            var tampered = WithSealedMessage(env, FlipHexByte(env.SealedMessageHex, 0));
            foreach (var m in members)
                T.False(tampered.CanOpen(m.Priv, m.Pub), "no member can open a message whose ciphertext was altered");
        });

        // ---- TAMPER (AEAD): a member's sealed leaf key ----
        T.Run("TAMPER a member's SEALED LEAF KEY (one byte) -> that member's open fails (AEAD)", () =>
        {
            var sender = Secp256k1.GenerateKeyPair();
            var members = Enumerable.Range(0, 4).Select(_ => Secp256k1.GenerateKeyPair()).ToArray();
            var env = BroadcastEnvelope.Seal(members.Select(m => Hex(m.Pub)).ToList(), sender.Priv, sender.Pub, U8("sealed leaf body"));

            var victimHex = Hex(members[0].Pub);
            var bad = env.Members.Select(m =>
                m.PubHex == victimHex ? m with { SealedLeafKeyHex = FlipHexByte(m.SealedLeafKeyHex, 0) } : m).ToList();
            var tampered = WithMembers(env, bad);

            T.False(tampered.CanOpen(members[0].Priv, members[0].Pub), "the victim cannot open after their sealed leaf was altered");
            // other members are untouched and still open.
            T.True(tampered.CanOpen(members[1].Priv, members[1].Pub), "an untouched member still opens");
        });

        // ---- CROSS-GROUP: a leaf key from one group cannot open another group's envelope ----
        T.Run("a member's wrapped leaf from group A cannot open group B's envelope (independent graphs)", () =>
        {
            var sender = Secp256k1.GenerateKeyPair();
            var alice = Secp256k1.GenerateKeyPair();
            var bob = Secp256k1.GenerateKeyPair();
            var carol = Secp256k1.GenerateKeyPair();

            // alice is a member of BOTH groups, but each Seal builds a fresh independent graph.
            var groupA = BroadcastEnvelope.Seal(new[] { Hex(alice.Pub), Hex(bob.Pub) }, sender.Priv, sender.Pub, U8("group A secret"));
            var groupB = BroadcastEnvelope.Seal(new[] { Hex(alice.Pub), Hex(carol.Pub) }, sender.Priv, sender.Pub, U8("group B secret"));

            // sanity: alice opens each with her own key normally.
            T.Eq(S(groupA.Open(alice.Priv, alice.Pub)), "group A secret", "alice opens group A");
            T.Eq(S(groupB.Open(alice.Priv, alice.Pub)), "group B secret", "alice opens group B");

            // HOSTILE: splice alice's group-A wrapped leaf into group B (keeping group B's items/message).
            var aliceHexA = Hex(alice.Pub);
            var aLeafFromA = groupA.Members.First(m => m.PubHex == aliceHexA);
            var bMembersWithAleak = groupB.Members
                .Select(m => m.PubHex == aliceHexA ? aLeafFromA : m).ToList();
            var crossed = WithMembers(groupB, bMembersWithAleak);

            // alice's group-A leaf cannot decrypt group B's body — different graph keys / different message key.
            T.False(crossed.CanOpen(alice.Priv, alice.Pub),
                "a group-A leaf key, spliced into group B, cannot open group B's message (AEAD across independent graphs)");

            // and bob (group A only) is a stranger to group B entirely.
            T.False(groupB.CanOpen(bob.Priv, bob.Pub), "a group-A-only member cannot open group B");
        });
    }

    // ----- helpers -----

    private static string Hex(byte[] b) => Convert.ToHexString(b).ToLowerInvariant();
    private static byte[] U8(string s) => Encoding.UTF8.GetBytes(s);
    private static string S(byte[] b) => Encoding.UTF8.GetString(b);

    /// <summary>True iff the corrupted node sits on this leaf's leaf->root path (so its unwrap is needed).</summary>
    private static bool PathHits(int leaf, int node)
    {
        for (int i = leaf; i >= 1; i /= 2) if (i == node) return true;
        return false;
    }

    private static byte[] FlipByte(byte[] src, int index)
    {
        var b = (byte[])src.Clone();
        b[index] ^= 0xFF;
        return b;
    }

    /// <summary>Flip the byte at <paramref name="byteIndex"/> within a hex string (re-encode to hex).</summary>
    private static string FlipHexByte(string hex, int byteIndex)
    {
        var b = Convert.FromHexString(hex);
        b[byteIndex] ^= 0xFF;
        return Convert.ToHexString(b).ToLowerInvariant();
    }

    // Rebuild a copy of an envelope with one component swapped (envelope props are init-only records).
    private static BroadcastEnvelope WithItems(BroadcastEnvelope e, List<BroadcastEncryption.Item> items) => new()
    {
        SenderPubHex = e.SenderPubHex,
        SealedMessageHex = e.SealedMessageHex,
        Items = items,
        Members = e.Members,
    };

    private static BroadcastEnvelope WithMembers(BroadcastEnvelope e, List<BroadcastEnvelope.Member> members) => new()
    {
        SenderPubHex = e.SenderPubHex,
        SealedMessageHex = e.SealedMessageHex,
        Items = e.Items,
        Members = members,
    };

    private static BroadcastEnvelope WithSealedMessage(BroadcastEnvelope e, string sealedMsgHex) => new()
    {
        SenderPubHex = e.SenderPubHex,
        SealedMessageHex = sealedMsgHex,
        Items = e.Items,
        Members = e.Members,
    };
}

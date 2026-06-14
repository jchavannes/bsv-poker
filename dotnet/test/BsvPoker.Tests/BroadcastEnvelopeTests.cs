using System.Text;
using BsvPoker.Core;
using BsvPoker.Crypto;

namespace BsvPoker.Tests;

/// <summary>
/// The on-chain-travellable GROUP envelope (key-graph broadcast encryption, GB 2623780 B): the sender seals
/// ONE message to a set of member PUBLIC keys; every selected member opens it with their private key alone;
/// an outsider cannot; and the envelope round-trips through JSON unchanged (store-and-forward on-chain).
/// </summary>
public static class BroadcastEnvelopeTests
{
    public static void All()
    {
        Console.WriteLine("broadcast envelope (on-chain group message — seal to member pubkeys, open with private key):");

        T.Run("seal to 3 members; each opens with only their private key", () =>
        {
            var sender = Secp256k1.GenerateKeyPair();
            var alice = Secp256k1.GenerateKeyPair();
            var bob = Secp256k1.GenerateKeyPair();
            var carol = Secp256k1.GenerateKeyPair();
            var pubs = new[] { Hex(alice.Pub), Hex(bob.Pub), Hex(carol.Pub) };

            var msg = Encoding.UTF8.GetBytes("group hand: I raise to 5 sat");
            var env = BroadcastEnvelope.Seal(pubs, sender.Priv, sender.Pub, msg);

            foreach (var m in new[] { alice, bob, carol })
                T.Eq(Encoding.UTF8.GetString(env.Open(m.Priv, m.Pub)), "group hand: I raise to 5 sat", "member opens");
        });

        T.Run("an outsider (not selected) cannot open the envelope", () =>
        {
            var sender = Secp256k1.GenerateKeyPair();
            var alice = Secp256k1.GenerateKeyPair();
            var outsider = Secp256k1.GenerateKeyPair();
            var env = BroadcastEnvelope.Seal(new[] { Hex(alice.Pub) }, sender.Priv, sender.Pub, Encoding.UTF8.GetBytes("secret"));
            T.True(!env.CanOpen(outsider.Priv, outsider.Pub), "outsider blocked");
            T.True(env.CanOpen(alice.Priv, alice.Pub), "member can");
        });

        T.Run("envelope round-trips through JSON (on-chain store-and-forward) and still opens", () =>
        {
            var sender = Secp256k1.GenerateKeyPair();
            var bob = Secp256k1.GenerateKeyPair();
            var env = BroadcastEnvelope.Seal(new[] { Hex(bob.Pub) }, sender.Priv, sender.Pub, Encoding.UTF8.GetBytes("delivered when you return"));
            var json = env.ToJson();
            var back = BroadcastEnvelope.FromJson(json);
            T.Eq(Encoding.UTF8.GetString(back.Open(bob.Priv, bob.Pub)), "delivered when you return", "opens after JSON round-trip");
            T.Eq(back.SenderPubHex, Hex(sender.Pub), "sender pubkey preserved");
        });

        T.Run("the SENDER, included as a member, can decrypt their OWN group message (on-chain history)", () =>
        {
            var sender = Secp256k1.GenerateKeyPair();
            var bob = Secp256k1.GenerateKeyPair();
            // mirror SendGroupChat: the sender is always added to the member set
            var members = new[] { Hex(bob.Pub), Hex(sender.Pub) };
            var env = BroadcastEnvelope.Seal(members, sender.Priv, sender.Pub, Encoding.UTF8.GetBytes("my own group note"));
            T.Eq(Encoding.UTF8.GetString(env.Open(sender.Priv, sender.Pub)), "my own group note", "sender reads their own message");
            T.Eq(Encoding.UTF8.GetString(env.Open(bob.Priv, bob.Pub)), "my own group note", "other member reads it too");
        });

        T.Run("non-power-of-two group (5 members) is padded and all 5 still decrypt", () =>
        {
            var sender = Secp256k1.GenerateKeyPair();
            var members = Enumerable.Range(0, 5).Select(_ => Secp256k1.GenerateKeyPair()).ToArray();
            var env = BroadcastEnvelope.Seal(members.Select(m => Hex(m.Pub)).ToList(), sender.Priv, sender.Pub, Encoding.UTF8.GetBytes("five-way table"));
            foreach (var m in members)
                T.Eq(Encoding.UTF8.GetString(env.Open(m.Priv, m.Pub)), "five-way table", "padded member opens");
        });
    }

    private static string Hex(byte[] b) => Convert.ToHexString(b).ToLowerInvariant();
}

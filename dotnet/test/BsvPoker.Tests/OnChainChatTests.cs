using BsvPoker.Core;
using BsvPoker.Crypto;

namespace BsvPoker.Tests;

/// <summary>
/// Chat AS a Bitcoin transaction: an encrypted ChatDirect output round-trips to the recipient, is opaque to
/// everyone else, rejects tampering, and is a valid typed transaction output (no OP_RETURN, no off-chain
/// channel). This is the only allowed form of player-to-player communication.
/// </summary>
public static class OnChainChatTests
{
    public static void All()
    {
        Console.WriteLine("on-chain chat (a message IS a Bitcoin transaction):");
        var alice = Secp256k1.GenerateKeyPair();
        var bob = Secp256k1.GenerateKeyPair();
        var eve = Secp256k1.GenerateKeyPair();

        T.Run("a chat output decrypts for the recipient and reveals the sender", () =>
        {
            var script = OnChainChat.BuildScript(bob.Pub, alice.Priv, alice.Pub, 0, "all-in, good luck 🂡");
            var got = OnChainChat.TryRead(script, bob.Priv, bob.Pub);
            T.True(got != null, "bob can read it");
            T.Eq(got!.Text, "all-in, good luck 🂡", "plaintext round-trips");
            T.Eq(T.Hex(got.SenderPub), T.Hex(alice.Pub), "sender identity recovered (the senderPub field)");
        });

        T.Run("RECOVERY: the SENDER can also read her own sent message (symmetric identity-key chat, forever)", () =>
        {
            // directive D-D: the key is symmetric (derived from both identity keys), so Alice recovers her own
            // sent conversation from her identity key alone — never an ephemeral secret that could be lost.
            var script = OnChainChat.BuildScript(bob.Pub, alice.Priv, alice.Pub, 7, "I can re-read this in 100 years");
            var asSender = OnChainChat.TryRead(script, alice.Priv, alice.Pub);
            var asRecip  = OnChainChat.TryRead(script, bob.Priv, bob.Pub);
            T.True(asSender != null && asSender.Text == "I can re-read this in 100 years", "Alice (sender) recovers her own message");
            T.True(asRecip  != null && asRecip.Text  == "I can re-read this in 100 years", "Bob (recipient) reads the same message");
        });

        T.Run("it is opaque to anyone who is not the recipient", () =>
        {
            var script = OnChainChat.BuildScript(bob.Pub, alice.Priv, alice.Pub, 1, "secret");
            T.True(OnChainChat.TryRead(script, eve.Priv, eve.Pub) == null, "eve (an outsider to the conversation) cannot read it");
            var carol = Secp256k1.GenerateKeyPair();
            T.True(OnChainChat.TryRead(script, carol.Priv, carol.Pub) == null, "no third party can read a one-to-one message");
        });

        T.Run("it is a valid ChatDirect typed output (no OP_RETURN) and tamper fails the AEAD", () =>
        {
            var script = OnChainChat.BuildScript(bob.Pub, alice.Priv, alice.Pub, 2, "hello");
            var parsed = TxTemplates.Parse(script);
            T.True(parsed is { Kind: TxKind.ChatDirect }, "parses as ChatDirect");
            T.Eq(script[^1], (byte)0xac, "ends in OP_CHECKSIG (recipient owns it)");
            var bad = (byte[])script.Clone(); bad[^40] ^= 0xFF; // flip a byte inside the ciphertext region
            T.True(OnChainChat.TryRead(bad, bob.Priv, bob.Pub) == null, "tampered ciphertext does not decrypt");
        });

        T.Run("a GROUP message (broadcast encryption) is read by every member, opaque to outsiders", () =>
        {
            var carol = Secp256k1.GenerateKeyPair();
            var members = new[] { T.Hex(alice.Pub), T.Hex(bob.Pub), T.Hex(carol.Pub) };
            var script = OnChainChat.BuildGroup(members, alice.Priv, alice.Pub, "table chat: nice hand all");
            T.True(TxTemplates.Parse(script) is { Kind: TxKind.ChatGroup }, "parses as ChatGroup");
            foreach (var m in new[] { alice, bob, carol })
            {
                var got = OnChainChat.TryReadGroup(script, m.Priv, m.Pub);
                T.True(got != null && got.Text == "table chat: nice hand all", "member reads the group message");
                T.Eq(T.Hex(got!.SenderPub), T.Hex(alice.Pub), "sender recovered");
            }
            T.True(OnChainChat.TryReadGroup(script, eve.Priv, eve.Pub) == null, "an outsider cannot read the group message");
            T.True(OnChainChat.TryRead(script, alice.Priv, alice.Pub) == null, "a group message is not a direct message");
        });

        T.Run("a group message rides in a real transaction and is scanned back by a member", () =>
        {
            var w = new OnChainWallet(WalletKeys.NewSeed());
            w.Add(new OnChainWallet.Utxo("bb".PadRight(64, '2'), 0, 1_000_000, 0, 0));
            var script = OnChainChat.BuildGroup(new[] { T.Hex(alice.Pub), T.Hex(bob.Pub) }, alice.Priv, alice.Pub, "gg");
            var spend = w.SpendAction(script, 1000, 500);
            T.True(w.VerifySpend(spend), "the group chat transaction is signed + value-conserving");
            var got = OnChainChat.TryReadGroupTx(spend.Tx, bob.Priv, bob.Pub);
            T.True(got != null && got.Text == "gg", "bob reads the group message out of the transaction");
        });

        T.Run("OFFLINE store-and-forward: a 1-sat discovery dust to the recipient's identity address makes the message findable by scripthash, and it decrypts — zero OP_RETURN", () =>
        {
            var aliceId = Secp256k1.GenerateKeyPair();
            var bobId = Secp256k1.GenerateKeyPair();
            // Alice → Bob: the encrypted data in a SPENDABLE typed output + a 1-sat dust to BOB's identity address
            var data = OnChainChat.BuildScript(bobId.Pub, aliceId.Priv, aliceId.Pub, 0, "delivered after you were offline");
            var bobDiscovery = Core.Chain.P2pkhLockForPub(bobId.Pub);       // a FIXED scripthash Bob subscribes to
            var w = new OnChainWallet(WalletKeys.NewSeed());
            w.Add(new OnChainWallet.Utxo("cc".PadRight(64, '3'), 0, 1_000_000, 0, 0));
            var spend = w.BuildActionMany(new[] { (data, 1L), (bobDiscovery, 1L) }, 1);
            T.True(w.VerifySpend(spend), "the message tx is signed + value-conserving");
            // Bob finds it WITHOUT being online when it was sent: his identity-address scripthash matches an output
            T.True(spend.Tx.Outs.Any(o => o.Script.AsSpan().SequenceEqual(bobDiscovery)), "tx pays Bob's identity address → found by scripthash subscription on sync");
            var got = OnChainChat.TryReadTx(spend.Tx, bobId.Priv, bobId.Pub);
            T.True(got != null && got.Text == "delivered after you were offline", "Bob decrypts the offline-delivered message");
            foreach (var o in spend.Tx.Outs) T.True(o.Script.Length == 0 || o.Script[0] != 0x6a, "NO OP_RETURN anywhere in the tx");
        });

        T.Run("a chat message rides in a real transaction and is read back by scanning the tx", () =>
        {
            // the message is funded as a real BSV transaction output, then found by scanning the tx
            var w = new OnChainWallet(WalletKeys.NewSeed());
            w.Add(new OnChainWallet.Utxo("aa".PadRight(64, '1'), 0, 1_000_000, 0, 0));
            var script = OnChainChat.BuildScript(bob.Pub, alice.Priv, alice.Pub, 0, "nh");
            var spend = w.SpendAction(script, 1000, 500);
            T.True(w.VerifySpend(spend), "the chat transaction is signed + value-conserving");
            var got = OnChainChat.TryReadTx(spend.Tx, bob.Priv, bob.Pub);
            T.True(got != null && got.Text == "nh", "bob reads the message out of the broadcastable transaction");
        });
    }
}

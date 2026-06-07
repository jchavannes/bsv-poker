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
            var script = OnChainChat.BuildScript(bob.Pub, alice.Pub, "all-in, good luck 🂡");
            var got = OnChainChat.TryRead(script, bob.Priv, bob.Pub);
            T.True(got != null, "bob can read it");
            T.Eq(got!.Text, "all-in, good luck 🂡", "plaintext round-trips");
            T.Eq(T.Hex(got.SenderPub), T.Hex(alice.Pub), "sender identity recovered (from inside the ciphertext)");
        });

        T.Run("it is opaque to anyone who is not the recipient", () =>
        {
            var script = OnChainChat.BuildScript(bob.Pub, alice.Pub, "secret");
            T.True(OnChainChat.TryRead(script, eve.Priv, eve.Pub) == null, "eve cannot read it");
            T.True(OnChainChat.TryRead(script, alice.Priv, alice.Pub) == null, "even the sender's key is not the recipient");
        });

        T.Run("it is a valid ChatDirect typed output (no OP_RETURN) and tamper fails the AEAD", () =>
        {
            var script = OnChainChat.BuildScript(bob.Pub, alice.Pub, "hello");
            var parsed = TxTemplates.Parse(script);
            T.True(parsed is { Kind: TxKind.ChatDirect }, "parses as ChatDirect");
            T.Eq(script[^1], (byte)0xac, "ends in OP_CHECKSIG (recipient owns it)");
            var bad = (byte[])script.Clone(); bad[^40] ^= 0xFF; // flip a byte inside the ciphertext region
            T.True(OnChainChat.TryRead(bad, bob.Priv, bob.Pub) == null, "tampered ciphertext does not decrypt");
        });

        T.Run("a chat message rides in a real transaction and is read back by scanning the tx", () =>
        {
            // the message is funded as a real BSV transaction output, then found by scanning the tx
            var w = new OnChainWallet(WalletKeys.NewSeed());
            w.Add(new OnChainWallet.Utxo("aa".PadRight(64, '1'), 0, 1_000_000, 0, 0));
            var script = OnChainChat.BuildScript(bob.Pub, alice.Pub, "nh");
            var spend = w.SpendAction(script, 1000, 500);
            T.True(w.VerifySpend(spend), "the chat transaction is signed + value-conserving");
            var got = OnChainChat.TryReadTx(spend.Tx, bob.Priv, bob.Pub);
            T.True(got != null && got.Text == "nh", "bob reads the message out of the broadcastable transaction");
        });
    }
}

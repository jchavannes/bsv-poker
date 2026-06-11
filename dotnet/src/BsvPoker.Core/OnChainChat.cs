using System.Linq;
using System.Text;
using BsvPoker.Crypto;

namespace BsvPoker.Core;

/// <summary>
/// Chat as a BITCOIN TRANSACTION — the only allowed form of communication. A message is an encrypted
/// <see cref="TxKind.ChatDirect"/> output carried in a real BSV transaction, broadcast over the Bitcoin
/// network and stored on-chain; there is NO off-chain channel. Each message uses a FRESH ephemeral ECDH key
/// to the recipient (forward secrecy, no key reuse) → HKDF → AES-256-GCM. The recipient finds its messages
/// by scanning transactions for ChatDirect outputs addressed to its key and decrypting them. The wire sees
/// only ciphertext; the sender's identity is inside the encrypted payload.
/// </summary>
public static class OnChainChat
{
    private static readonly byte[] Salt = Encoding.ASCII.GetBytes("bsvpoker-onchain-chat-v1");
    private static readonly byte[] Info = Encoding.ASCII.GetBytes("ChatDirect");

    /// <summary>
    /// Build the encrypted ChatDirect output script for one message to <paramref name="recipientPub33"/>.
    /// Fields: [ephemeralPub, recipientPub, ciphertext]; the output is owned (spendable) by the recipient.
    /// </summary>
    public static byte[] BuildScript(byte[] recipientPub33, byte[] senderPub33, string text)
    {
        var eph = Secp256k1.GenerateKeyPair();
        var key = Aead.Hkdf(Concat(Secp256k1.Ecdh(eph.Priv, recipientPub33), eph.Pub), Salt, Info);
        var pt = Concat(senderPub33, Encoding.UTF8.GetBytes(text));         // sender identity travels encrypted
        var ct = Aead.Seal(key, pt, recipientPub33);
        return TxTemplates.BuildOutput(TxKind.ChatDirect, new[] { eph.Pub, recipientPub33, ct }, recipientPub33);
    }

    public sealed record Incoming(byte[] SenderPub, string Text);

    /// <summary>PUBLIC broadcast: a plaintext message anyone can read (incl. bots). Carried as a ChatGroup output
    /// with a zero group id; the body is the sender's pubkey + the plaintext (NOT encrypted — it is public).</summary>
    public static byte[] BuildBroadcast(byte[] senderPub33, string text)
        => TxTemplates.BuildOutput(TxKind.ChatGroup, new[] { new byte[32], senderPub33, Encoding.UTF8.GetBytes(text) }, senderPub33);

    /// <summary>Read a PUBLIC broadcast (ChatGroup, zero group id) — returns sender + text for everyone.</summary>
    public static Incoming? TryReadBroadcast(byte[] script)
    {
        var p = TxTemplates.Parse(script);
        if (p is not { Kind: TxKind.ChatGroup } || p.Fields.Length != 3) return null;
        if (p.Fields[0].Length != 32 || p.Fields[0].Any(b => b != 0)) return null;   // only the public (zero-group) form
        try { return new Incoming(p.Fields[1], Encoding.UTF8.GetString(p.Fields[2])); } catch { return null; }
    }
    public static Incoming? TryReadBroadcastTx(Chain.Tx tx)
    { foreach (var o in tx.Outs) { var r = TryReadBroadcast(o.Script); if (r != null) return r; } return null; }

    /// <summary>Decrypt a ChatDirect output if it is addressed to me; null if it is not chat or not mine.</summary>
    public static Incoming? TryRead(byte[] script, byte[] myPriv32, byte[] myPub33)
    {
        var p = TxTemplates.Parse(script);
        if (p is not { Kind: TxKind.ChatDirect } || p.Fields.Length != 3) return null;
        var ephPub = p.Fields[0]; var recipient = p.Fields[1]; var ct = p.Fields[2];
        if (!recipient.AsSpan().SequenceEqual(myPub33)) return null;        // not addressed to me
        try
        {
            var key = Aead.Hkdf(Concat(Secp256k1.Ecdh(myPriv32, ephPub), ephPub), Salt, Info);
            var pt = Aead.Open(key, ct, myPub33);
            if (pt.Length < 33) return null;
            return new Incoming(pt[..33], Encoding.UTF8.GetString(pt.AsSpan(33).ToArray()));
        }
        catch { return null; }
    }

    /// <summary>Scan all outputs of a transaction for a chat message addressed to me.</summary>
    public static Incoming? TryReadTx(Chain.Tx tx, byte[] myPriv32, byte[] myPub33)
    {
        foreach (var o in tx.Outs) { var r = TryRead(o.Script, myPriv32, myPub33); if (r != null) return r; }
        return null;
    }

    private static byte[] Concat(byte[] a, byte[] b) { var r = new byte[a.Length + b.Length]; a.CopyTo(r, 0); b.CopyTo(r, a.Length); return r; }
}

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
    private static readonly byte[] DmKdfSalt   = Encoding.ASCII.GetBytes("bsvpoker-direct-chat-v2");  // (reserved: HKDF salt fallback)
    private static readonly byte[] DmMarkerTag = Encoding.ASCII.GetBytes("bsvpoker-chat-marker|");
    private static readonly byte[] DmInfoTag   = Encoding.ASCII.GetBytes("bsvpoker-dm|");

    // The per-PAIR CHAT MARKER: deterministic and identical for both parties (sorted identity pubs), so the
    // symmetric key is unique to THIS conversation and recoverable forever from the two IDENTITY keys alone —
    // there is no ephemeral secret that can be lost (directive D-D, transcript 20260612-104337).
    private static byte[] ChatMarker(byte[] pubA33, byte[] pubB33)
    {
        var (lo, hi) = CompareBytes(pubA33, pubB33) <= 0 ? (pubA33, pubB33) : (pubB33, pubA33);
        return Hashes.Sha256(Concat(Concat(DmMarkerTag, lo), hi));
    }

    private static int CompareBytes(byte[] a, byte[] b)
    {
        int n = Math.Min(a.Length, b.Length);
        for (int i = 0; i < n; i++) { int d = a[i].CompareTo(b[i]); if (d != 0) return d; }
        return a.Length.CompareTo(b.Length);
    }

    private static byte[] U64Be(ulong v) { var b = new byte[8]; for (int i = 7; i >= 0; i--) { b[i] = (byte)(v & 0xff); v >>= 8; } return b; }
    private static ulong  U64Be(byte[] b) { ulong v = 0; foreach (var x in b) v = (v << 8) | x; return v; }

    // SYMMETRIC per-message key = HKDF( ECDH(myIdentityPriv, otherIdentityPub), salt = chat marker,
    // info = "bsvpoker-dm|" ‖ senderPub ‖ index ). Both parties derive the SAME key: ECDH is symmetric, the
    // marker is symmetric, and senderPub+index ride in cleartext fields. Deterministic ⇒ recoverable forever.
    // senderPub in the info separates the two directions (Alice→Bob vs Bob→Alice) so equal indices never collide.
    private static byte[] DirectKey(byte[] myPriv32, byte[] otherPub33, byte[] senderPub33, byte[] recipientPub33, ulong index)
    {
        var shared = Secp256k1.Ecdh(myPriv32, otherPub33);
        var marker = ChatMarker(senderPub33, recipientPub33);
        var info   = Concat(Concat(DmInfoTag, senderPub33), U64Be(index));
        return Aead.Hkdf(shared, marker, info);
    }

    /// <summary>
    /// Build a ChatDirect output: a one-to-one message encrypted under a SYMMETRIC key derived from the two
    /// IDENTITY keys (ECDH) + a per-conversation chat marker + a per-message <paramref name="index"/> — NOT an
    /// ephemeral/ECIES scheme (ECIES is banned). Both Alice and Bob recompute the key from their identity key
    /// alone, so either can recover the whole conversation forever (the wallet persists the index/history).
    /// Fields: [senderPub, recipientPub, index(8 BE), ciphertext]; the output is owned (spendable) by the recipient.
    /// </summary>
    public static byte[] BuildScript(byte[] recipientPub33, byte[] senderPriv32, byte[] senderPub33, ulong index, string text)
    {
        var key = DirectKey(senderPriv32, recipientPub33, senderPub33, recipientPub33, index);
        var idx = U64Be(index);
        var aad = Concat(Concat(senderPub33, recipientPub33), idx);
        var ct  = Aead.Seal(key, Encoding.UTF8.GetBytes(text), aad);
        return TxTemplates.BuildOutput(TxKind.ChatDirect, new[] { senderPub33, recipientPub33, idx, ct }, recipientPub33);
    }

    public sealed record Incoming(byte[] SenderPub, string Text);

    // PLAINTEXT "public broadcast" (send-to-everybody) is BANNED by directive: broadcast = the principal's
    // key-graph BROADCAST-ENCRYPTION PATENT (GB 2623780 B / EP4046048B1), NEVER an unencrypted send-to-all.
    // "Broadcast to everyone" is BuildGroup(allKnownRecipients, …): ONE sealed envelope every recipient opens
    // with their own private key alone. The former plaintext BuildBroadcast/TryReadBroadcast primitives were
    // removed for this reason (audit F1, transcript 20260612-103126) — do not reintroduce a plaintext path.

    /// <summary>The group id for a set of member pubkeys: H(sorted member pubs). Stable regardless of order, and
    /// never all-zero for a real group (that form is reserved for the public broadcast above).</summary>
    public static byte[] GroupId(IReadOnlyList<string> memberPubs)
    {
        var joined = string.Join("|", memberPubs.Select(p => p.ToLowerInvariant()).Distinct().OrderBy(p => p, StringComparer.Ordinal));
        var h = Hashes.Sha256(Encoding.UTF8.GetBytes(joined));
        if (h.All(b => b == 0)) h[0] = 1;                                   // never collide with the public-broadcast zero id
        return h;
    }

    /// <summary>
    /// GROUP send using the user's key-graph BROADCAST ENCRYPTION (GB 2623780 B): seal ONE message to the
    /// selected member pubkeys, pack the self-contained <see cref="BroadcastEnvelope"/> (JSON) as the ChatGroup
    /// ciphertext under H(members) as the group id. Any selected member opens it with their private key alone —
    /// so it can sit on-chain and be delivered when an offline member returns. Owned (dust-spendable) by sender.
    /// </summary>
    public static byte[] BuildGroup(IReadOnlyList<string> memberPubs, byte[] senderPriv33, byte[] senderPub33, string text)
    {
        var env = BroadcastEnvelope.Seal(memberPubs, senderPriv33, senderPub33, Encoding.UTF8.GetBytes(text));
        var json = Encoding.UTF8.GetBytes(env.ToJson());
        return TxTemplates.BuildOutput(TxKind.ChatGroup, new[] { GroupId(memberPubs), senderPub33, json }, senderPub33);
    }

    /// <summary>Read a broadcast-encryption GROUP message if I am one of its members; null otherwise. The output
    /// carries a self-contained envelope, so this needs only my own key pair — no shared group state.</summary>
    public static Incoming? TryReadGroup(byte[] script, byte[] myPriv32, byte[] myPub33)
    {
        var p = TxTemplates.Parse(script);
        if (p is not { Kind: TxKind.ChatGroup } || p.Fields.Length != 3) return null;
        if (p.Fields[0].Length != 32 || p.Fields[0].All(b => b == 0)) return null;   // zero id = public broadcast, not a group
        try
        {
            var env = BroadcastEnvelope.FromJson(Encoding.UTF8.GetString(p.Fields[2]));
            if (!env.CanOpen(myPriv32, myPub33)) return null;                          // I'm not a selected member
            var text = Encoding.UTF8.GetString(env.Open(myPriv32, myPub33));
            return new Incoming(p.Fields[1], text);                                    // sender pub is field[1]
        }
        catch { return null; }
    }
    public static Incoming? TryReadGroupTx(Chain.Tx tx, byte[] myPriv32, byte[] myPub33)
    { foreach (var o in tx.Outs) { var r = TryReadGroup(o.Script, myPriv32, myPub33); if (r != null) return r; } return null; }

    /// <summary>Decrypt a ChatDirect output if I am EITHER party (recipient OR sender), so both Alice and Bob can
    /// recover the conversation forever; null if it is not chat or not my conversation. The key is symmetric, so
    /// the sender reading her own sent message derives the same key with the counterparty's pubkey.</summary>
    public static Incoming? TryRead(byte[] script, byte[] myPriv32, byte[] myPub33)
    {
        var p = TxTemplates.Parse(script);
        if (p is not { Kind: TxKind.ChatDirect } || p.Fields.Length != 4) return null;
        var senderPub = p.Fields[0]; var recipient = p.Fields[1]; var idx = p.Fields[2]; var ct = p.Fields[3];
        if (senderPub.Length != 33 || recipient.Length != 33 || idx.Length != 8) return null;
        bool iAmRecipient = recipient.AsSpan().SequenceEqual(myPub33);
        bool iAmSender    = senderPub.AsSpan().SequenceEqual(myPub33);
        if (!iAmRecipient && !iAmSender) return null;                       // not my conversation
        var otherPub = iAmRecipient ? senderPub : recipient;               // the counterparty's identity pub
        try
        {
            var key = DirectKey(myPriv32, otherPub, senderPub, recipient, U64Be(idx));
            var aad = Concat(Concat(senderPub, recipient), idx);
            var pt  = Aead.Open(key, ct, aad);
            return new Incoming(senderPub, Encoding.UTF8.GetString(pt));
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

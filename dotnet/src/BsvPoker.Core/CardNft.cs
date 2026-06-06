using System.Security.Cryptography;
using System.Text;
using BsvPoker.Crypto;

namespace BsvPoker.Core;

/// <summary>
/// A poker card as an ENCRYPTED 1-sat NFT held in a player's wallet. The card's reveal secret
/// (cardIndex ‖ blind) is encrypted with an AES-256-GCM key derived by **ECDH** — the owner's own key
/// agreement (ECDH of the owner's private key with the owner's public key), so ONLY the owner can derive
/// the key. No ephemeral keys, no ECIES — ECDH with an AES key, exactly. A fresh random nonce per seal
/// salts the key so no two seals reuse a key. TRANSFER re-seals to the recipient's own ECDH key, so the
/// sender can no longer derive it and LOSES ACCESS. The on-chain 1-sat NFT binds H(sealed) + the owner.
/// </summary>
public static class CardNft
{
    private static readonly byte[] Info = Encoding.ASCII.GetBytes("bsvpoker-card-ecdh-key-v1");

    public readonly record struct Opened(int CardIndex, byte[] Blind);

    /// <summary>Seal (cardIndex, blind) so only the owner of <paramref name="ownerPriv32"/> can open it. Blob = nonce ‖ aead.</summary>
    public static string SealToOwner(int cardIndex, byte[] blind, byte[] ownerPriv32)
    {
        if (cardIndex < 0 || cardIndex > 51) throw new ArgumentException("card index 0..51");
        var ownerPub = Secp256k1.PublicKeyCompressed(ownerPriv32);
        var shared = Secp256k1.Ecdh(ownerPriv32, ownerPub);            // ECDH key agreement (owner-only)
        var nonce = RandomNumberGenerator.GetBytes(16);                // fresh salt → no key reuse
        var key = Aead.Hkdf(shared, nonce, Info);
        var plaintext = Concat(new[] { (byte)cardIndex }, blind);
        var aead = Aead.Seal(key, plaintext, ownerPub);               // owner pubkey bound as AAD
        return Convert.ToHexString(Concat(nonce, aead)).ToLowerInvariant();
    }

    /// <summary>Open a sealed card with the owner's private key. Throws if not the owner / tampered.</summary>
    public static Opened OpenAsOwner(string sealedHex, byte[] ownerPriv32)
    {
        var blob = Convert.FromHexString(sealedHex);
        if (blob.Length < 16 + 12 + 16 + 1) throw new ArgumentException("sealed blob too short");
        var nonce = blob[..16];
        var aead = blob[16..];
        var ownerPub = Secp256k1.PublicKeyCompressed(ownerPriv32);
        var shared = Secp256k1.Ecdh(ownerPriv32, ownerPub);
        var key = Aead.Hkdf(shared, nonce, Info);
        var pt = Aead.Open(key, aead, ownerPub);                       // throws on wrong key / tamper
        return new Opened(pt[0], pt[1..]);
    }

    /// <summary>True iff this key can open the card (is the current owner and the blob is intact).</summary>
    public static bool CanOpen(string sealedHex, byte[] ownerPriv32)
    {
        try { OpenAsOwner(sealedHex, ownerPriv32); return true; } catch { return false; }
    }

    /// <summary>
    /// Transfer: the current owner opens, and re-seals to the RECIPIENT's own ECDH key. After this the
    /// sender cannot derive the key (it needs the recipient's private key), so the sender LOSES ACCESS.
    /// </summary>
    public static string Transfer(string sealedHex, byte[] fromPriv32, byte[] toPriv32)
    {
        var opened = OpenAsOwner(sealedHex, fromPriv32);
        return SealToOwner(opened.CardIndex, opened.Blind, toPriv32);
    }

    /// <summary>Commitment to a sealed blob (H(sealed)) — bound into the on-chain 1-sat NFT output.</summary>
    public static byte[] SealCommitment(string sealedHex) => Hashes.Sha256(Convert.FromHexString(sealedHex));

    /// <summary>
    /// The 1-sat NFT locking script: &lt;state&gt; OP_DROP &lt;ownerPub&gt; OP_CHECKSIG, where state =
    /// TAG ‖ H(sealed). No OP_RETURN. Issued from the player's own sats (no banker).
    /// </summary>
    public static byte[] NftLock(string sealedHex, byte[] ownerPub33)
    {
        var tag = Encoding.ASCII.GetBytes("BSVPOKER-CARD-NFT-V1");
        var state = Concat(tag, SealCommitment(sealedHex));
        var b = new List<byte>();
        b.Add(0x4c); b.Add((byte)state.Length); b.AddRange(state); // OP_PUSHDATA1 <state>
        b.Add(0x75); // OP_DROP
        b.Add((byte)ownerPub33.Length); b.AddRange(ownerPub33);
        b.Add(0xac); // OP_CHECKSIG
        return b.ToArray();
    }

    private static byte[] Concat(byte[] a, byte[] b) { var o = new byte[a.Length + b.Length]; a.CopyTo(o, 0); b.CopyTo(o, a.Length); return o; }
}

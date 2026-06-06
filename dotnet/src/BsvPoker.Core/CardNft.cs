using System.Security.Cryptography;
using System.Text;
using BsvPoker.Crypto;

namespace BsvPoker.Core;

/// <summary>
/// A poker card as an ENCRYPTED 1-sat NFT held in a player's wallet. The card's reveal secret
/// (cardIndex ‖ blind) is sealed to the OWNER's secp256k1 key via ECIES (ephemeral ECDH → HKDF →
/// AES-256-GCM, owner pubkey bound as AAD). SENDING the card RE-SEALS it to the recipient and the
/// sender LOSES ACCESS (the ephemeral private key is never kept). The on-chain 1-sat NFT binds
/// H(sealed) + the owner; cards are issued from the player's own sats (no dealer/bank). Same model on
/// every network.
/// </summary>
public static class CardNft
{
    private static readonly byte[] Salt = Encoding.ASCII.GetBytes("bsvpoker-card-ecies-salt-v1");
    private static readonly byte[] Info = Encoding.ASCII.GetBytes("bsvpoker-card-key-v1");

    public readonly record struct Opened(int CardIndex, byte[] Blind);

    /// <summary>Seal (cardIndex, blind) to the owner's compressed pubkey. Returns a hex blob = ephPub ‖ aead.</summary>
    public static string SealToOwner(int cardIndex, byte[] blind, byte[] ownerPub33)
    {
        if (cardIndex < 0 || cardIndex > 51) throw new ArgumentException("card index 0..51");
        if (ownerPub33.Length != 33) throw new ArgumentException("ownerPub must be 33-byte compressed");
        var (ephPriv, ephPub) = Secp256k1.GenerateKeyPair();           // FRESH ephemeral — dropped after this call
        var shared = Secp256k1.Ecdh(ephPriv, ownerPub33);
        var key = Aead.Hkdf(Concat(shared, ephPub), Salt, Info);
        var plaintext = Concat(new[] { (byte)cardIndex }, blind);
        var aead = Aead.Seal(key, plaintext, ownerPub33);
        return Convert.ToHexString(Concat(ephPub, aead)).ToLowerInvariant();
    }

    /// <summary>Open a sealed card with the owner's private key. Throws if not the owner / tampered.</summary>
    public static Opened OpenAsOwner(string sealedHex, byte[] ownerPriv32)
    {
        var blob = Convert.FromHexString(sealedHex);
        if (blob.Length < 33 + 12 + 16 + 1) throw new ArgumentException("sealed blob too short");
        var ephPub = blob[..33];
        var aead = blob[33..];
        var ownerPub = Secp256k1.PublicKeyCompressed(ownerPriv32);
        var shared = Secp256k1.Ecdh(ownerPriv32, ephPub);
        var key = Aead.Hkdf(Concat(shared, ephPub), Salt, Info);
        var pt = Aead.Open(key, aead, ownerPub); // throws on wrong key / tamper (AAD = ownerPub)
        return new Opened(pt[0], pt[1..]);
    }

    /// <summary>True iff this key can open the card (is the current owner and the blob is intact).</summary>
    public static bool CanOpen(string sealedHex, byte[] ownerPriv32)
    {
        try { OpenAsOwner(sealedHex, ownerPriv32); return true; } catch { return false; }
    }

    /// <summary>Transfer: current owner opens, re-seals to the recipient. Sender can no longer open the result.</summary>
    public static string Transfer(string sealedHex, byte[] fromPriv32, byte[] toPub33)
    {
        var opened = OpenAsOwner(sealedHex, fromPriv32);
        return SealToOwner(opened.CardIndex, opened.Blind, toPub33);
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
        // push state
        b.Add(0x4c); b.Add((byte)state.Length); b.AddRange(state); // OP_PUSHDATA1
        b.Add(0x75); // OP_DROP
        b.Add((byte)ownerPub33.Length); b.AddRange(ownerPub33);
        b.Add(0xac); // OP_CHECKSIG
        return b.ToArray();
    }

    private static byte[] Concat(byte[] a, byte[] b) { var o = new byte[a.Length + b.Length]; a.CopyTo(o, 0); b.CopyTo(o, a.Length); return o; }
}

using System.Security.Cryptography;

namespace BsvPoker.Crypto;

/// <summary>
/// Authenticated encryption + key derivation (built-in AES-256-GCM + HKDF-SHA256). Used by the
/// encrypted chat (per-message keys) and the wallet's seed-at-rest encryption. Output format is
/// nonce(12) ‖ ciphertext ‖ tag(16); a fresh random nonce per call.
/// </summary>
public static class Aead
{
    public const int KeyLen = 32;
    private const int NonceLen = 12;
    private const int TagLen = 16;

    public static byte[] Hkdf(byte[] ikm, byte[] salt, byte[] info, int length = KeyLen)
        => HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, length, salt, info);

    /// <summary>Encrypt with AES-256-GCM; returns nonce‖ciphertext‖tag. `aad` is authenticated, not encrypted.</summary>
    public static byte[] Seal(byte[] key32, ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> aad = default)
    {
        if (key32.Length != KeyLen) throw new ArgumentException("key must be 32 bytes");
        var nonce = RandomNumberGenerator.GetBytes(NonceLen);
        var ct = new byte[plaintext.Length];
        var tag = new byte[TagLen];
        using var gcm = new AesGcm(key32, TagLen);
        gcm.Encrypt(nonce, plaintext, ct, tag, aad);
        var outb = new byte[NonceLen + ct.Length + TagLen];
        nonce.CopyTo(outb, 0);
        ct.CopyTo(outb, NonceLen);
        tag.CopyTo(outb, NonceLen + ct.Length);
        return outb;
    }

    /// <summary>Decrypt nonce‖ciphertext‖tag; throws (AuthenticationTagMismatchException) on tamper/wrong key.</summary>
    public static byte[] Open(byte[] key32, ReadOnlySpan<byte> blob, ReadOnlySpan<byte> aad = default)
    {
        if (key32.Length != KeyLen) throw new ArgumentException("key must be 32 bytes");
        if (blob.Length < NonceLen + TagLen) throw new ArgumentException("ciphertext too short");
        var nonce = blob[..NonceLen];
        var ct = blob[NonceLen..^TagLen];
        var tag = blob[^TagLen..];
        var pt = new byte[ct.Length];
        using var gcm = new AesGcm(key32, TagLen);
        gcm.Decrypt(nonce, ct, tag, pt, aad);
        return pt;
    }
}

using System.Text;
using System.Security.Cryptography;
using BsvPoker.Crypto;

namespace BsvPoker.Core;

/// <summary>
/// Electrum-grade wallet extras: WIF private-key import/export and Bitcoin "signed message"
/// sign/verify (so the wallet can prove ownership of an address / sign arbitrary text).
/// </summary>
public static class WalletExtras
{
    /// <summary>Export a private key as WIF (mainnet 0x80, compressed flag).</summary>
    public static string ToWif(byte[] priv32, bool compressed = true, byte version = 0x80)
    {
        var payload = new List<byte> { version };
        payload.AddRange(priv32);
        if (compressed) payload.Add(0x01);
        return Base58.CheckEncode(payload.ToArray());
    }

    /// <summary>Import a WIF private key → (priv32, compressed). Throws on a bad checksum/format.</summary>
    public static (byte[] Priv, bool Compressed) FromWif(string wif)
    {
        var p = Base58.CheckDecode(wif.Trim());
        if (p.Length != 33 && p.Length != 34) throw new FormatException("bad WIF length");
        bool compressed = p.Length == 34;
        return (p.Skip(1).Take(32).ToArray(), compressed);
    }

    private static void VarStr(List<byte> b, byte[] s)
    {
        if (s.Length < 0xfd) b.Add((byte)s.Length);
        else { b.Add(0xfd); b.Add((byte)s.Length); b.Add((byte)(s.Length >> 8)); }
        b.AddRange(s);
    }

    /// <summary>The Bitcoin "signed message" digest = sha256d(varstr(magic) ‖ varstr(message)).</summary>
    public static byte[] MessageDigest(string message)
    {
        var b = new List<byte>();
        VarStr(b, Encoding.ASCII.GetBytes("Bitcoin Signed Message:\n"));
        VarStr(b, Encoding.UTF8.GetBytes(message));
        return Hashes.Sha256d(b.ToArray());
    }

    /// <summary>Sign a text message with a private key; returns a base64 compact signature.</summary>
    public static string SignMessage(byte[] priv32, string message)
        => Convert.ToBase64String(Secp256k1.SignDigest(priv32, MessageDigest(message)));

    /// <summary>Verify a base64 signed message against the signer's compressed pubkey.</summary>
    public static bool VerifyMessage(byte[] pub33, string message, string sigBase64)
    {
        try { return Secp256k1.VerifyDigest(pub33, MessageDigest(message), Convert.FromBase64String(sigBase64)); }
        catch { return false; }
    }

    // ----- Password-at-rest: encrypt the wallet SEED backup with a passphrase (encrypted wallet) -----
    // Format (versioned, self-describing): "enc1." ‖ iter ‖ "." ‖ base64(salt16) ‖ "." ‖ base64(nonce‖ct‖tag).
    // Key = PBKDF2-HMAC-SHA256(password, salt, iters) → 32 bytes; payload sealed with AES-256-GCM (Aead).
    private const int Pbkdf2Iters = 250_000;

    /// <summary>Encrypt a secret (the wallet seed backup) under a password. The result is safe to store on disk.</summary>
    public static string EncryptSeed(string secret, string password)
    {
        if (string.IsNullOrEmpty(password)) throw new ArgumentException("password must not be empty");
        var salt = RandomNumberGenerator.GetBytes(16);
        var key = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Pbkdf2Iters, HashAlgorithmName.SHA256, Aead.KeyLen);
        var blob = Aead.Seal(key, Encoding.UTF8.GetBytes(secret));
        return $"enc1.{Pbkdf2Iters}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(blob)}";
    }

    /// <summary>True if a stored seed string is in the encrypted (enc1) format.</summary>
    public static bool IsEncryptedSeed(string stored) => stored.StartsWith("enc1.", StringComparison.Ordinal);

    /// <summary>Decrypt an enc1 secret with the password. Throws (CryptographicException) on wrong password.</summary>
    public static string DecryptSeed(string stored, string password)
    {
        var parts = stored.Split('.', 4);
        if (parts.Length != 4 || parts[0] != "enc1") throw new FormatException("not an enc1 secret");
        int iters = int.Parse(parts[1]);
        var salt = Convert.FromBase64String(parts[2]);
        var blob = Convert.FromBase64String(parts[3]);
        var key = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iters, HashAlgorithmName.SHA256, Aead.KeyLen);
        return Encoding.UTF8.GetString(Aead.Open(key, blob)); // AES-GCM tag verifies the password
    }
}

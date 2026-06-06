using System.Security.Cryptography;
using System.Text;
using BsvPoker.Crypto;

namespace BsvPoker.Core;

/// <summary>
/// BSV-native deterministic wallet keys. The entire wallet is one 32-byte master SEED; every spending
/// key is derived DIRECTLY from that seed by a domain-separated HMAC-SHA256 and reduced to a valid
/// secp256k1 scalar. Backing up the seed backs up the whole wallet. Pure secp256k1 — the BSV curve.
/// </summary>
public sealed class WalletKeys
{
    public byte[] Priv { get; }
    public byte[] Pub => Secp256k1.PublicKeyCompressed(Priv);

    private WalletKeys(byte[] priv) { Priv = priv; }

    /// <summary>
    /// Derive the key for (chain, index). Convention: chain 0 = receive, chain 1 = change. The key is a
    /// pure deterministic function of the seed, so the same seed always reproduces the same wallet.
    /// </summary>
    public static WalletKeys Account(byte[] seed32, uint chain, uint index)
    {
        if (seed32.Length != 32) throw new ArgumentException("seed must be 32 bytes");
        for (uint salt = 0; salt < 256; salt++)
        {
            using var hmac = new HMACSHA256(seed32);
            var d = hmac.ComputeHash(Encoding.ASCII.GetBytes($"bsvpoker-key|{chain}|{index}|{salt}"));
            try { _ = Secp256k1.PublicKeyCompressed(d); return new WalletKeys(d); } // validates the scalar
            catch { /* zero scalar (~2^-128): bump salt and retry */ }
        }
        throw new InvalidOperationException("could not derive a valid key");
    }

    /// <summary>A fresh random 32-byte master seed — the root of a brand-new wallet.</summary>
    public static byte[] NewSeed() => RandomNumberGenerator.GetBytes(32);

    private const byte SeedBackupVersion = 0x9c; // distinct from address/WIF versions

    /// <summary>
    /// Encode the whole wallet as a single human-transcribable SEED backup string (Base58Check). This is
    /// the entire backup — no word list, no key stretching. Restoring it restores every key.
    /// </summary>
    public static string SeedToBackup(byte[] seed32)
    {
        if (seed32.Length != 32) throw new ArgumentException("seed must be 32 bytes");
        var payload = new byte[33];
        payload[0] = SeedBackupVersion;
        seed32.CopyTo(payload, 1);
        return Base58.CheckEncode(payload);
    }

    /// <summary>Decode a SEED backup string back to the 32-byte master seed. Throws on a bad checksum/format.</summary>
    public static byte[] BackupToSeed(string backup)
    {
        var p = Base58.CheckDecode(backup.Trim());
        if (p.Length != 33 || p[0] != SeedBackupVersion) throw new FormatException("not a wallet seed backup");
        return p[1..];
    }
}

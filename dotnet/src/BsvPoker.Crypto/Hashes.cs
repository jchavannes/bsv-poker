using System.Security.Cryptography;

namespace BsvPoker.Crypto;

/// <summary>Bitcoin/BSV hash primitives used by addresses, txids and sighashes.</summary>
public static class Hashes
{
    public static byte[] Sha256(ReadOnlySpan<byte> data) => SHA256.HashData(data);

    /// <summary>SHA-256d = SHA256(SHA256(x)) — txids, block hashes, Base58Check checksum.</summary>
    public static byte[] Sha256d(ReadOnlySpan<byte> data) => SHA256.HashData(SHA256.HashData(data));

    /// <summary>HASH160 = RIPEMD160(SHA256(x)) — the core of a P2PKH address.</summary>
    public static byte[] Hash160(ReadOnlySpan<byte> data) => Ripemd160.Hash(SHA256.HashData(data));
}

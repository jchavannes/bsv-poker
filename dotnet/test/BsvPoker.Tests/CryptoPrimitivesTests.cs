using System.Text;
using BsvPoker.Crypto;

namespace BsvPoker.Tests;

public static class CryptoPrimitivesTests
{
    public static void All()
    {
        Console.WriteLine("hashes / base58 / aead:");

        T.Run("RIPEMD160 standard vectors", () =>
        {
            T.Eq(T.Hex(Ripemd160.Hash(Encoding.ASCII.GetBytes(""))), "9c1185a5c5e9fc54612808977ee8f548b2258d31");
            T.Eq(T.Hex(Ripemd160.Hash(Encoding.ASCII.GetBytes("abc"))), "8eb208f7e05d987a9b044a8e98c6b087f15a0bfc");
            T.Eq(T.Hex(Ripemd160.Hash(Encoding.ASCII.GetBytes("message digest"))), "5d0689ef49d2fae572b881b123a85ffa21595f36");
        });

        T.Run("HASH160 of a known pubkey → the canonical P2PKH address (Base58Check)", () =>
        {
            var pub = T.Bytes("0250863ad64a87ae8a2fe83c1af1a8403cb53f53e486d8511dad8a04887e5b2352");
            var h160 = Hashes.Hash160(pub);
            T.Eq(T.Hex(h160), "f54a5851e9372b87810a8e60cdd2e7cfd80b6e31");
            var payload = new byte[21];
            payload[0] = 0x00; // mainnet P2PKH version
            h160.CopyTo(payload, 1);
            T.Eq(Base58.CheckEncode(payload), "1PMycacnJaSqwwJqjawXBErnLsZ7RkXUAs");
        });

        T.Run("Base58Check round-trips and rejects a corrupted checksum", () =>
        {
            var payload = T.Bytes("00f54a5851e9372b87810a8e60cdd2e7cfd80b6e31");
            var addr = Base58.CheckEncode(payload);
            T.Eq(T.Hex(Base58.CheckDecode(addr)), T.Hex(payload));
            var corrupt = addr[..^1] + (addr[^1] == 's' ? "t" : "s");
            T.Throws(() => Base58.CheckDecode(corrupt), "corrupt checksum must throw");
        });

        T.Run("HKDF is deterministic; different info → different key", () =>
        {
            var ikm = T.Bytes("00112233445566778899aabbccddeeff");
            var salt = Encoding.ASCII.GetBytes("salt");
            var k1 = Aead.Hkdf(ikm, salt, Encoding.ASCII.GetBytes("a"));
            var k2 = Aead.Hkdf(ikm, salt, Encoding.ASCII.GetBytes("a"));
            var k3 = Aead.Hkdf(ikm, salt, Encoding.ASCII.GetBytes("b"));
            T.Eq(T.Hex(k1), T.Hex(k2), "deterministic");
            T.False(T.Hex(k1) == T.Hex(k3), "different info ⇒ different key");
        });

        T.Run("AES-256-GCM seal/open round-trips; tamper + wrong key are rejected", () =>
        {
            var key = Aead.Hkdf(T.Bytes("aabb"), Encoding.ASCII.GetBytes("s"), Encoding.ASCII.GetBytes("i"));
            var pt = Encoding.UTF8.GetBytes("bank-grade secret");
            var aad = Encoding.ASCII.GetBytes("ctx");
            var blob = Aead.Seal(key, pt, aad);
            T.Eq(Encoding.UTF8.GetString(Aead.Open(key, blob, aad)), "bank-grade secret");
            var bad = (byte[])blob.Clone(); bad[^1] ^= 0x01;
            T.Throws(() => Aead.Open(key, bad, aad), "tampered tag");
            var wrong = Aead.Hkdf(T.Bytes("ccdd"), Encoding.ASCII.GetBytes("s"), Encoding.ASCII.GetBytes("i"));
            T.Throws(() => Aead.Open(wrong, blob, aad), "wrong key");
            T.Throws(() => Aead.Open(key, blob, Encoding.ASCII.GetBytes("other-aad")), "wrong aad");
        });

        T.Run("two seals of the same plaintext differ (fresh nonce — semantic security)", () =>
        {
            var key = Aead.Hkdf(T.Bytes("ee"), Encoding.ASCII.GetBytes("s"), Encoding.ASCII.GetBytes("i"));
            var pt = Encoding.UTF8.GetBytes("same");
            T.False(T.Hex(Aead.Seal(key, pt)) == T.Hex(Aead.Seal(key, pt)));
        });
    }
}

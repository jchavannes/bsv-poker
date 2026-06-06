using BsvPoker.Core;
using BsvPoker.Crypto;

namespace BsvPoker.Tests;

public static class WalletExtrasTests
{
    public static void All()
    {
        Console.WriteLine("wallet extras (WIF + signed messages):");

        T.Run("WIF known vector (uncompressed)", () =>
        {
            var priv = T.Bytes("0C28FCA386C7A227600B2FE50B7CAE11EC86D3BF1FBE471BE89827E19D72AA1D");
            T.Eq(WalletExtras.ToWif(priv, compressed: false), "5HueCGU8rMjxEXxiPuD5BDku4MkFqeZyd4dZ1jvhTVqvbTLvyTJ");
        });

        T.Run("WIF round-trips (compressed) and re-derives the same pubkey", () =>
        {
            var priv = T.Seed(9);
            var wif = WalletExtras.ToWif(priv, compressed: true);
            var (back, compressed) = WalletExtras.FromWif(wif);
            T.True(compressed); T.Eq(T.Hex(back), T.Hex(priv));
            T.Eq(T.Hex(Secp256k1.PublicKeyCompressed(back)), T.Hex(Secp256k1.PublicKeyCompressed(priv)));
        });

        T.Run("signed message verifies with the right key; wrong key/message fails", () =>
        {
            var k = Secp256k1.GenerateKeyPair();
            var sig = WalletExtras.SignMessage(k.Priv, "I own this address");
            T.True(WalletExtras.VerifyMessage(k.Pub, "I own this address", sig));
            T.False(WalletExtras.VerifyMessage(k.Pub, "different message", sig));
            var other = Secp256k1.GenerateKeyPair();
            T.False(WalletExtras.VerifyMessage(other.Pub, "I own this address", sig));
        });

        T.Run("password-at-rest: seed encrypts, decrypts with right password, fails with wrong one", () =>
        {
            var seedBackup = WalletKeys.SeedToBackup(WalletKeys.NewSeed());
            var enc = WalletExtras.EncryptSeed(seedBackup, "correct horse battery staple");
            T.True(WalletExtras.IsEncryptedSeed(enc), "ciphertext is in enc1 format");
            T.False(enc.Contains(seedBackup), "the plaintext seed does not leak into the blob");
            T.Eq(WalletExtras.DecryptSeed(enc, "correct horse battery staple"), seedBackup);
            T.Throws(() => WalletExtras.DecryptSeed(enc, "wrong password"));
        });

        T.Run("password-at-rest: two encryptions of the same seed differ (fresh salt+nonce)", () =>
        {
            var seedBackup = WalletKeys.SeedToBackup(WalletKeys.NewSeed());
            var a = WalletExtras.EncryptSeed(seedBackup, "pw");
            var b = WalletExtras.EncryptSeed(seedBackup, "pw");
            T.False(a == b, "salt+nonce randomization makes ciphertexts distinct");
            T.Eq(WalletExtras.DecryptSeed(a, "pw"), WalletExtras.DecryptSeed(b, "pw"));
        });
    }
}

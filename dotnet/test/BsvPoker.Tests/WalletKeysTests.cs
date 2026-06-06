using BsvPoker.Core;
using BsvPoker.Crypto;

namespace BsvPoker.Tests;

/// <summary>
/// BSV-native wallet-key derivation: the whole wallet is one 32-byte master seed; keys are derived
/// directly from it; the seed alone (as a Base58Check backup string) restores everything.
/// </summary>
public static class WalletKeysTests
{
    private static string Addr(byte[] pub)
    {
        var payload = new byte[21]; payload[0] = 0x00; Hashes.Hash160(pub).CopyTo(payload, 1);
        return Base58.CheckEncode(payload);
    }

    public static void All()
    {
        Console.WriteLine("BSV-native wallet keys (seed-derived):");

        T.Run("seed backup string round-trips and a corrupted one is rejected by the checksum", () =>
        {
            var seed = WalletKeys.NewSeed();
            var backup = WalletKeys.SeedToBackup(seed);
            T.Eq(T.Hex(WalletKeys.BackupToSeed(backup)), T.Hex(seed), "round-trips");
            var bad = backup[..^1] + (backup[^1] == 'A' ? 'B' : 'A');
            T.Throws(() => WalletKeys.BackupToSeed(bad), "corrupted backup rejected");
        });

        T.Run("the same seed restores deterministically to the SAME wallet address", () =>
        {
            var seed = WalletKeys.NewSeed();
            var backup = WalletKeys.SeedToBackup(seed);
            var a1 = Addr(WalletKeys.Account(seed, 0, 0).Pub);
            var a2 = Addr(WalletKeys.Account(WalletKeys.BackupToSeed(backup), 0, 0).Pub); // 'restore'
            T.Eq(a1, a2, "restored wallet derives the same receive address");
        });

        T.Run("derivation: distinct indices/chains give distinct valid 33-byte pubkeys", () =>
        {
            var seed = WalletKeys.NewSeed();
            var r0 = WalletKeys.Account(seed, 0, 0).Pub;
            var r1 = WalletKeys.Account(seed, 0, 1).Pub;
            var c0 = WalletKeys.Account(seed, 1, 0).Pub;
            T.Eq(r0.Length, 33); T.True(r0[0] == 0x02 || r0[0] == 0x03);
            T.False(T.Hex(r0) == T.Hex(r1), "receive#0 != receive#1");
            T.False(T.Hex(r0) == T.Hex(c0), "receive#0 != change#0");
        });

        T.Run("a derived key can sign a BSV input that verifies (the wallet can spend)", () =>
        {
            var seed = WalletKeys.NewSeed();
            var k = WalletKeys.Account(seed, 0, 0);
            var tx = new Chain.Tx(2, new() { new("aa".PadRight(64, 'b'), 0, Array.Empty<byte>(), 0xffffffff) }, new() { new(50000, Chain.P2pkhLockForPub(k.Pub)) }, 0);
            var signed = Chain.SignP2pkhInput(tx, 0, k.Priv, k.Pub, 60000);
            T.True(Chain.VerifyP2pkhInput(signed, 0, k.Pub, 60000), "seed-derived key spends");
        });
    }
}

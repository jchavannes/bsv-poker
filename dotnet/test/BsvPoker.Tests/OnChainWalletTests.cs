using BsvPoker.Core;
using BsvPoker.Crypto;

namespace BsvPoker.Tests;

/// <summary>The real-BSV spending wallet: coin selection, signed multi-input spend, change, value
/// conservation, consensus verification, and insufficient-funds handling.</summary>
public static class OnChainWalletTests
{
    public static void All()
    {
        Console.WriteLine("on-chain wallet (coin selection + signed spend):");
        var seed = WalletKeys.NewSeed();
        var recipient = Secp256k1.GenerateKeyPair().Pub;

        OnChainWallet Funded()
        {
            var w = new OnChainWallet(seed);
            w.Add(new OnChainWallet.Utxo("aa".PadRight(64, '1'), 0, 50000, 0, 0));
            w.Add(new OnChainWallet.Utxo("bb".PadRight(64, '2'), 1, 50000, 0, 1));
            w.Add(new OnChainWallet.Utxo("cc".PadRight(64, '3'), 0, 50000, 0, 2));
            return w;
        }

        T.Run("balance sums the UTXOs", () => T.Eq(Funded().Balance, 150000L, "3 × 50000"));

        T.Run("a payment selects coins, signs every input, returns change, and conserves value", () =>
        {
            var w = Funded();
            var s = w.BuildPayment(recipient, amount: 120000, fee: 1000);
            T.True(s.Inputs.Count >= 3, "selected enough coins");
            T.Eq(s.Tx.Outs[0].Value, 120000L, "recipient paid");
            T.Eq(s.Change, 29000L, "change = 150000 - 120000 - 1000");
            T.Eq(s.Tx.Outs.Count, 2, "recipient + change outputs");
            T.True(w.VerifySpend(s), "every input verifies and value is conserved (inputs = outputs + fee)");
        });

        T.Run("a no-change payment omits the change output", () =>
        {
            var w = new OnChainWallet(seed);
            w.Add(new OnChainWallet.Utxo("dd".PadRight(64, '4'), 0, 10000, 0, 0));
            var s = w.BuildPayment(recipient, amount: 9000, fee: 1000);
            T.Eq(s.Change, 0L); T.Eq(s.Tx.Outs.Count, 1, "no change output");
            T.True(w.VerifySpend(s), "verifies");
        });

        T.Run("insufficient funds throws", () =>
        {
            T.Throws(() => Funded().BuildPayment(recipient, amount: 200000, fee: 1000), "can't overspend");
        });

        T.Run("a tampered output breaks verification", () =>
        {
            var w = Funded();
            var s = w.BuildPayment(recipient, 100000, 1000);
            var tampered = s.Tx with { Outs = new() { s.Tx.Outs[0] with { Value = 140000 } } }; // steal the change
            T.False(w.VerifySpend(s with { Tx = tampered }), "altering outputs invalidates the signatures/conservation");
        });
    }
}

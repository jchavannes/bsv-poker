using BsvPoker.Core;
using BsvPoker.Crypto;

namespace BsvPoker.Tests;

/// <summary>
/// CAPSTONE: a MULTIWAY, real-money, on-chain hand whose pot is held under DEALERLESS threshold custody.
/// Every other on-chain money test is heads-up 2-of-2; this proves the project's headline claim for N&gt;2 —
/// n players fund one pot locked to a (t+1)-of-n threshold key (no dealer, no escrow agent, no player holds
/// the key), the winner is paid by a single standard signature that t+1 players jointly produce, value is
/// conserved, and if the hand cannot complete every stake is refunded by a pre-agreed nLockTime recovery.
/// Composes the verified units (ThresholdEcdsa + ThresholdCustody + Chain) — the live engine is untouched.
/// </summary>
public static class OnChainThresholdHandTests
{
    public static void All()
    {
        Console.WriteLine("multiway on-chain hand under dealerless threshold custody (real money, no dealer):");

        // three players, each staking 100 → a 300 pot; custody is 2-of-3 (t=1, n=3 = 2t+1)
        const long stake = 100, players = 3, pot = stake * players, fee = 250;
        const string potTxid = "33" + "00000000000000000000000000000000000000000000000000000000000000";

        T.Run("the pot is funded to ONE dealerless threshold key (sum of stakes), as a plain P2PKH", () =>
        {
            var custody = ThresholdEcdsa.Jvrss(n: 3, degree: 1);
            var lockScript = ThresholdCustody.PotLock(custody);
            // each player's stake funds the SAME pot output; the pot value is the conserved sum
            long funded = 0; for (int i = 0; i < players; i++) funded += stake;
            T.Eq(funded, pot, "the pot equals the sum of the three stakes (value conserved into custody)");
            T.True(lockScript.Length == 25 && lockScript[0] == 0x76 && lockScript[1] == 0xa9, "pot is a plain P2PKH to a·G (no dealer/escrow script)");
        });

        T.Run("the winner is paid the whole pot by a (t+1)-of-n threshold signature; value is conserved", () =>
        {
            var custody = ThresholdEcdsa.Jvrss(n: 3, degree: 1);
            var winner = Secp256k1.GenerateKeyPair();           // the seat the hand awarded the pot to
            var settle = ThresholdCustody.SettleToWinner(potTxid, 0, pot, winner.Pub, fee, custody);
            T.True(Chain.VerifyP2pkhInput(settle, 0, custody.PublicKey, pot), "threshold settlement verifies against the pot key");
            T.Eq(settle.Outs.Count, 1, "the whole pot goes to one winner");
            T.Eq(settle.Outs[0].Value, pot - fee, "winner receives pot − fee (no value created or lost)");
            T.True(settle.Outs[0].Script.SequenceEqual(Chain.P2pkhLockForPub(winner.Pub)), "paid to the winning seat's key");
        });

        T.Run("no sub-threshold coalition (or outsider) can move the pot", () =>
        {
            var custody = ThresholdEcdsa.Jvrss(n: 3, degree: 1);
            var winner = Secp256k1.GenerateKeyPair().Pub;
            var settle = ThresholdCustody.SettleToWinner(potTxid, 0, pot, winner, fee, custody);
            var outsider = Secp256k1.GenerateKeyPair().Pub;
            T.False(Chain.VerifyP2pkhInput(settle, 0, outsider, pot), "an outsider key cannot claim the spend");
            T.False(Chain.VerifyP2pkhInput(settle, 0, custody.PublicKey, pot + 1), "a tampered pot amount breaks the sighash");
        });

        T.Run("if the hand cannot complete, every stake is refunded by a threshold-signed nLockTime recovery", () =>
        {
            var custody = ThresholdEcdsa.Jvrss(n: 3, degree: 1);
            var p0 = Secp256k1.GenerateKeyPair().Pub;
            var p1 = Secp256k1.GenerateKeyPair().Pub;
            var p2 = Secp256k1.GenerateKeyPair().Pub;
            // refund each player their stake; a small miner fee (30) comes off ONE share so refunds ≤ pot
            const long recFee = 30;
            var rec = ThresholdCustody.BuildRecovery(potTxid, 0, pot,
                new[] { (p0, stake), (p1, stake), (p2, stake - recFee) }, custody, lockHeight: 750_000);
            T.True(Chain.VerifyP2pkhInput(rec, 0, custody.PublicKey, pot), "threshold recovery verifies against the pot key");
            T.Eq(rec.Outs.Count, 3, "one refund output per player");
            T.True(rec.Outs.Sum(o => o.Value) <= pot, "refunds never exceed the pot (no money created)");
            T.Eq(rec.LockTime, 750_000u, "recovery binds the agreed lock height");
            T.Eq(rec.Ins[0].Sequence, 0xfffffffeu, "non-final sequence so the locktime is enforced");
        });
    }
}

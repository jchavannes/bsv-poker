using BsvPoker.Core;
using BsvPoker.Crypto;

namespace BsvPoker.Tests;

/// <summary>
/// Threshold-custody pot (EP4152683B1): the pot is an ordinary P2PKH locked to a dealerless (t+1)-of-n
/// threshold key. Paying the winner and refunding via nLockTime each require t+1 players to jointly produce a
/// single standard signature — no dealer, no sub-threshold coalition can move the money — and both verify on
/// the SAME consensus path as a single-key spend, with no multisig script to fingerprint the table.
/// </summary>
public static class ThresholdCustodyTests
{
    public static void All()
    {
        Console.WriteLine("threshold-custody pot (dealerless t+1-of-n, settled by a standard signature):");
        const string potTxid = "11" + "00000000000000000000000000000000000000000000000000000000000000"; // 64 hex
        const long amount = 100_000, fee = 200;

        T.Run("the pot is a plain P2PKH to the threshold key (no multisig fingerprint)", () =>
        {
            var key = ThresholdEcdsa.Jvrss(n: 3, degree: 1);
            var lockScript = ThresholdCustody.PotLock(key);
            T.True(lockScript.Length == 25 && lockScript[0] == 0x76 && lockScript[1] == 0xa9 && lockScript[2] == 0x14
                   && lockScript[^2] == 0x88 && lockScript[^1] == 0xac, "lock is OP_DUP OP_HASH160 <20> OP_EQUALVERIFY OP_CHECKSIG");
            T.True(lockScript.SequenceEqual(Chain.P2pkhLockForPub(key.PublicKey)), "locked to a·G (the dealerless threshold key)");
        });

        T.Run("the winner is paid by a (t+1)-of-n THRESHOLD signature that verifies on the ordinary path", () =>
        {
            var key = ThresholdEcdsa.Jvrss(n: 3, degree: 1);
            var winner = Secp256k1.GenerateKeyPair().Pub;
            var settled = ThresholdCustody.SettleToWinner(potTxid, 0, amount, winner, fee, key);
            T.True(Chain.VerifyP2pkhInput(settled, 0, key.PublicKey, amount), "threshold settlement verifies against the pot key");
            T.Eq(settled.Outs.Count, 1, "single payout output");
            T.Eq(settled.Outs[0].Value, amount - fee, "winner receives amount − fee (value conserved)");
            T.True(settled.Outs[0].Script.SequenceEqual(Chain.P2pkhLockForPub(winner)), "paid to the winner's key");
        });

        T.Run("the settlement does NOT verify against any other key (only the threshold coalition can spend)", () =>
        {
            var key = ThresholdEcdsa.Jvrss(n: 3, degree: 1);
            var winner = Secp256k1.GenerateKeyPair().Pub;
            var settled = ThresholdCustody.SettleToWinner(potTxid, 0, amount, winner, fee, key);
            var stranger = Secp256k1.GenerateKeyPair().Pub;
            T.False(Chain.VerifyP2pkhInput(settled, 0, stranger, amount), "a stranger's key cannot claim the threshold spend");
            T.False(Chain.VerifyP2pkhInput(settled, 0, key.PublicKey, amount + 1), "a tampered amount breaks the sighash");
        });

        T.Run("nLockTime recovery is threshold-signed, conserves value, and binds the locktime", () =>
        {
            var key = ThresholdEcdsa.Jvrss(n: 5, degree: 2);   // 3-of-5 custody
            var a = Secp256k1.GenerateKeyPair().Pub;
            var b = Secp256k1.GenerateKeyPair().Pub;
            var rec = ThresholdCustody.BuildRecovery(potTxid, 0, amount, new[] { (a, 60_000L), (b, 39_800L) }, key, lockHeight: 800_000);
            T.True(Chain.VerifyP2pkhInput(rec, 0, key.PublicKey, amount), "threshold recovery verifies against the pot key");
            T.Eq(rec.LockTime, 800_000u, "recovery carries the agreed lock height");
            T.Eq(rec.Ins[0].Sequence, 0xfffffffeu, "non-final sequence so CLTV/nLockTime binds");
            T.True(rec.Outs.Sum(o => o.Value) <= amount, "refunds never exceed the pot (no money created)");
        });
    }
}

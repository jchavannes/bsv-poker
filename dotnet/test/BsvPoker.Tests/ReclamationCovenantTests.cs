using BsvPoker.Core;
using BsvPoker.Core.Games;
using BsvPoker.Crypto;

namespace BsvPoker.Tests;

/// <summary>
/// EP4152683B1 RECLAMATION COVENANT — when a player goes unresponsive/malicious mid-game, the responsive
/// majority RECLAIMS the committed pot into a holding account under a SECOND threshold key (the faulty party's
/// share swapped out), settles only the agreed parties, and is backed by a time-locked refund so nothing is
/// stranded. The whole organ COMPOSES the proven primitives (ThresholdEcdsa + ThresholdCustody + nLockTime
/// n-of-n recovery); these tests prove the POSITIVE flow and the HOSTILE properties:
///   • a sub-threshold / swapped-out faulty party cannot steal the pot from either account;
///   • the reclamation pays ONLY the agreed parties (a tampered destination/amount breaks the sighash);
///   • the time-lock binds (future locktime + non-final sequence);
///   • value is conserved end-to-end (pot − fees), no money created.
/// </summary>
public static class ReclamationCovenantTests
{
    public static void All()
    {
        Console.WriteLine("EP4152683B1 reclamation covenant (holding account under a 2nd threshold key + time-lock):");
        const string potTxid = "ab" + "00000000000000000000000000000000000000000000000000000000000000";   // 64 hex
        const string holdingTxid = "cd" + "00000000000000000000000000000000000000000000000000000000000000";
        const long pot = 100_000, fee = 200;

        T.Run("the holding account is a plain P2PKH to the SECOND threshold key (no multisig fingerprint)", () =>
        {
            var holding = ReclamationCovenant.SwapFaultyShare(responsiveCount: 3, degree: 1);
            var lockScript = ReclamationCovenant.HoldingLock(holding);
            T.True(lockScript.Length == 25 && lockScript[0] == 0x76 && lockScript[1] == 0xa9 && lockScript[2] == 0x14
                   && lockScript[^2] == 0x88 && lockScript[^1] == 0xac, "lock is OP_DUP OP_HASH160 <20> OP_EQUALVERIFY OP_CHECKSIG");
            T.True(lockScript.SequenceEqual(Chain.P2pkhLockForPub(holding.PublicKey)), "locked to a'·G (the re-keyed holding key)");
        });

        T.Run("swap: the holding key is an INDEPENDENT dealerless key, unrelated to the custody key", () =>
        {
            var custody = ThresholdEcdsa.Jvrss(n: 3, degree: 1);
            var holding = ReclamationCovenant.SwapFaultyShare(responsiveCount: 3, degree: 1);
            T.False(holding.PublicKey.SequenceEqual(custody.PublicKey), "a'·G ≠ a·G — the faulty party's share is swapped out into a fresh key");
            // and the re-key needs enough RESPONSIVE parties (n ≥ 2t+1) — too few must be rejected
            bool rejected = false;
            try { ReclamationCovenant.SwapFaultyShare(responsiveCount: 2, degree: 1); } catch { rejected = true; }
            T.True(rejected, "cannot re-key with fewer than 2t+1 responsive parties");
        });

        T.Run("RECLAIM: the responsive coalition threshold-sweeps the pot into the holding account, verified on-chain", () =>
        {
            var custody = ThresholdEcdsa.Jvrss(n: 3, degree: 1);     // pot in 2-of-3 custody
            var holding = ReclamationCovenant.SwapFaultyShare(responsiveCount: 3, degree: 1);
            var tx = ReclamationCovenant.ReclaimToHolding(potTxid, 0, pot, fee, custody, holding);
            T.True(Chain.VerifyP2pkhInput(tx, 0, custody.PublicKey, pot), "the reclaim is a valid threshold spend of the custody pot");
            T.Eq(tx.Outs.Count, 1, "single holding-account output");
            T.Eq(tx.Outs[0].Value, pot - fee, "pot − fee moves into the holding account (value conserved)");
            T.True(tx.Outs[0].Script.SequenceEqual(ReclamationCovenant.HoldingLock(holding)), "paid into the 2nd threshold key, not to any individual");
        });

        T.Run("SETTLE: the holding account pays ONLY the agreed parties, verified on the consensus path", () =>
        {
            var holding = ReclamationCovenant.SwapFaultyShare(responsiveCount: 3, degree: 1);
            long hv = pot - fee;
            var alice = Secp256k1.GenerateKeyPair().Pub;
            var bob = Secp256k1.GenerateKeyPair().Pub;
            var settle = ReclamationCovenant.SettleHolding(holdingTxid, 0, hv,
                new[] { (alice, 70_000L), (bob, hv - 70_000L - fee) }, holding);
            T.True(Chain.VerifyP2pkhInput(settle, 0, holding.PublicKey, hv), "the settlement is a valid threshold spend of the holding account");
            T.Eq(settle.Outs.Count, 2, "one output per agreed party");
            T.True(settle.Outs[0].Script.SequenceEqual(Chain.P2pkhLockForPub(alice)), "first agreed party paid");
            T.True(settle.Outs[1].Script.SequenceEqual(Chain.P2pkhLockForPub(bob)), "second agreed party paid");
            T.True(settle.Outs.Sum(o => o.Value) <= hv, "payouts never exceed the holding value (no money created)");
        });

        T.Run("end-to-end value is conserved: pot ⇒ holding ⇒ winner loses only the two tiny fees", () =>
        {
            var custody = ThresholdEcdsa.Jvrss(n: 3, degree: 1);
            var holding = ReclamationCovenant.SwapFaultyShare(responsiveCount: 3, degree: 1);
            var reclaim = ReclamationCovenant.ReclaimToHolding(potTxid, 0, pot, fee, custody, holding);
            long hv = reclaim.Outs[0].Value;                                  // pot − fee
            var winner = Secp256k1.GenerateKeyPair().Pub;
            var settle = ReclamationCovenant.SettleHolding(Chain.Txid(reclaim), 0, hv,
                new[] { (winner, hv - fee) }, holding);
            T.Eq(settle.Outs[0].Value, pot - fee - fee, "winner gets pot − reclaim fee − settle fee (every satoshi accounted)");
            T.True(Chain.VerifyP2pkhInput(settle, 0, holding.PublicKey, hv), "the chained settlement verifies against the holding key");
        });

        T.Run("TIME-LOCK: the holding-account recovery binds a future locktime with a non-final sequence", () =>
        {
            var holding = ReclamationCovenant.SwapFaultyShare(responsiveCount: 5, degree: 2);  // 3-of-5 holding
            long hv = pot - fee;
            var a = Secp256k1.GenerateKeyPair().Pub;
            var b = Secp256k1.GenerateKeyPair().Pub;
            var rec = ReclamationCovenant.BuildHoldingRecovery(holdingTxid, 0, hv,
                new[] { (a, 50_000L), (b, hv - 50_000L) }, holding, lockHeight: 815_000);
            T.True(Chain.VerifyP2pkhInput(rec, 0, holding.PublicKey, hv), "the time-locked refund is a valid threshold spend");
            T.Eq(rec.LockTime, 815_000u, "the refund binds the agreed lock height");
            T.Eq(rec.Ins[0].Sequence, 0xfffffffeu, "non-final sequence so nLockTime/CLTV is enforced");
            T.True(rec.Outs.Sum(o => o.Value) <= hv, "refunds never exceed the holding value (no money created)");
        });

        T.Run("the n-of-n holding recovery twin binds the locktime and refunds every party (liveness backstop)", () =>
        {
            var p0 = Secp256k1.GenerateKeyPair().Pub;
            var p1 = Secp256k1.GenerateKeyPair().Pub;
            long hv = pot - fee;
            var rec = ReclamationCovenant.BuildNofNHoldingRecovery(holdingTxid, 0,
                new[] { (p0, hv / 2), (p1, hv - hv / 2) }, fee, lockHeight: 820_000);
            T.Eq(rec.LockTime, 820_000u, "the n-of-n refund binds the agreed lock height");
            T.Eq(rec.Ins[0].Sequence, 0xfffffffeu, "non-final sequence so the locktime is enforced");
            T.Eq(rec.Outs.Sum(o => o.Value), hv - fee, "every party's share is refunded (value conserved, fee aside)");
        });

        // ---------------------------- HOSTILE cases ----------------------------

        T.Run("HOSTILE: a swapped-out faulty / sub-threshold party CANNOT forge the reclaim of the pot", () =>
        {
            var custody = ThresholdEcdsa.Jvrss(n: 3, degree: 1);
            var holding = ReclamationCovenant.SwapFaultyShare(responsiveCount: 3, degree: 1);
            // the attacker does NOT hold the custody shares; they spin up their OWN dealerless key and try to
            // produce a reclaim signature with it (the only thing a sub-threshold coalition can actually do).
            var attackerKey = ThresholdEcdsa.Jvrss(n: 3, degree: 1);
            var forged = ReclamationCovenant.ReclaimToHolding(potTxid, 0, pot, fee, attackerKey, holding);
            T.False(Chain.VerifyP2pkhInput(forged, 0, custody.PublicKey, pot), "a non-custody key cannot move the pot (the threshold signature does not verify)");
        });

        T.Run("HOSTILE: a swapped-out faulty party CANNOT drain the holding account", () =>
        {
            var holding = ReclamationCovenant.SwapFaultyShare(responsiveCount: 3, degree: 1);
            long hv = pot - fee;
            var thief = Secp256k1.GenerateKeyPair().Pub;
            // the faulty party has no share of a'; the best they can attempt is a spend signed by a key they
            // DO control — which is not the holding key, so it fails the consensus check.
            var attackerKey = ThresholdEcdsa.Jvrss(n: 3, degree: 1);
            var forged = ReclamationCovenant.SettleHolding(holdingTxid, 0, hv, new[] { (thief, hv - fee) }, attackerKey);
            T.False(Chain.VerifyP2pkhInput(forged, 0, holding.PublicKey, hv), "a non-holding key cannot drain the holding account");
        });

        T.Run("HOSTILE: the reclamation pays only the AGREED outputs — a tampered amount/destination breaks the sighash", () =>
        {
            var holding = ReclamationCovenant.SwapFaultyShare(responsiveCount: 3, degree: 1);
            long hv = pot - fee;
            var agreed = Secp256k1.GenerateKeyPair().Pub;
            var settle = ReclamationCovenant.SettleHolding(holdingTxid, 0, hv, new[] { (agreed, hv - fee) }, holding);
            // valid as built
            T.True(Chain.VerifyP2pkhInput(settle, 0, holding.PublicKey, hv), "the agreed settlement verifies");
            // tamper the amount the input claims to be spending — the FORKID sighash binds the value
            T.False(Chain.VerifyP2pkhInput(settle, 0, holding.PublicKey, hv + 1), "a tampered input amount breaks the threshold signature");
            // tamper the output destination after signing → signature no longer covers the outputs
            var redirected = settle with { Outs = new List<Chain.TxOut> { new(settle.Outs[0].Value, Chain.P2pkhLockForPub(Secp256k1.GenerateKeyPair().Pub)) } };
            T.False(Chain.VerifyP2pkhInput(redirected, 0, holding.PublicKey, hv), "redirecting the payout after signing invalidates the signature");
        });

        T.Run("HOSTILE: the reclamation cannot CREATE money — over-paying the holding value is rejected", () =>
        {
            var holding = ReclamationCovenant.SwapFaultyShare(responsiveCount: 3, degree: 1);
            long hv = pot - fee;
            var greedy = Secp256k1.GenerateKeyPair().Pub;
            bool rejected = false;
            try { ReclamationCovenant.SettleHolding(holdingTxid, 0, hv, new[] { (greedy, hv + 1) }, holding); }
            catch { rejected = true; }
            T.True(rejected, "a settlement paying more than the holding value is refused (no money created)");

            bool recRejected = false;
            try { ReclamationCovenant.BuildHoldingRecovery(holdingTxid, 0, hv, new[] { (greedy, hv + 1) }, holding, lockHeight: 800_000); }
            catch { recRejected = true; }
            T.True(recRejected, "a refund exceeding the holding value is refused");
        });

        T.Run("HOSTILE: the reclaim fee is range-checked (0 ≤ fee < pot) so it cannot zero-out or invert the pot", () =>
        {
            var custody = ThresholdEcdsa.Jvrss(n: 3, degree: 1);
            var holding = ReclamationCovenant.SwapFaultyShare(responsiveCount: 3, degree: 1);
            bool tooBig = false, negative = false;
            try { ReclamationCovenant.ReclaimToHolding(potTxid, 0, pot, pot, custody, holding); } catch { tooBig = true; }
            try { ReclamationCovenant.ReclaimToHolding(potTxid, 0, pot, -1, custody, holding); } catch { negative = true; }
            T.True(tooBig, "fee ≥ pot is rejected");
            T.True(negative, "a negative fee is rejected");
        });
    }
}

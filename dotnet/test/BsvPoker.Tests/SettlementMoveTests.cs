using BsvPoker.Core;
using BsvPoker.Core.Games;
using BsvPoker.Crypto;

namespace BsvPoker.Tests;

/// <summary>
/// SETTLEMENT — the on-chain payout that closes a hand by paying the n-of-n committed pot out to the final
/// per-seat stacks, with EVERY player's consent. Because the pot is locked n-of-n (Chain.MultisigLockNofN), a
/// settlement can ONLY move with all n signatures; an (n-1) coalition can never settle to a thief, never
/// inflate a payout, and never strand the honest stake. Positive (winner-takes-pot + a split, both verify and
/// conserve value) and HOSTILE (coalition theft, tampered amount, over-pot payouts, stalled refund) — proven
/// OP_CHECKMULTISIG + the FORKID sighash only, no fabricated covenant.
/// </summary>
public static class SettlementMoveTests
{
    public static void All()
    {
        Console.WriteLine("settlement move (n-of-n pot payout to the final stacks; no theft under an n-1 collusion):");
        const int n = 3;
        var kp = Enumerable.Range(0, n).Select(_ => Secp256k1.GenerateKeyPair()).ToArray();
        var pubs = kp.Select(k => k.Pub).ToList();
        string potTxid = "ff".PadRight(64, '0');
        const long potValue = 300, fee = 1;
        var pot = new ZeroConfMoveChain.Committed(potTxid, 0, potValue, pubs);

        // Helper: sign with all n players (in pubkey order) and assemble the settlement.
        Chain.Tx SignAll(Chain.Tx s) =>
            SettlementMove.Apply(s, kp.Select(k => SettlementMove.Sign(s, pot, k.Priv)).ToList());

        T.Run("winner-takes-pot: one seat ends with pot − fee, the rest busted; verifies n-of-n, conserves value", () =>
        {
            // seat 0 won everything; seats 1,2 busted (0). winner stack = pot − fee.
            var stacks = new List<SettlementMove.Payout>
            {
                new(pubs[0], potValue - fee), new(pubs[1], 0), new(pubs[2], 0),
            };
            var s = SettlementMove.Build(pot, stacks, fee);
            T.Eq(s.Outs.Count, 1, "only the winner gets an output (busted seats dropped)");
            T.Eq(s.Outs[0].Value, potValue - fee, "winner receives pot − fee");
            var signed = SignAll(s);
            T.True(SettlementMove.Verify(signed, pot), "the all-n settlement verifies on the consensus path");
            T.True(SettlementMove.ConservesValue(signed, pot, fee), "Σ payouts == pot − fee (value conserved)");
            T.True(Chain.VerifyMultisigNofN(signed, 0, pubs, potValue), "verifies directly on Chain.VerifyMultisigNofN");
        });

        T.Run("split: three seats each keep a stack; all paid correctly, verifies n-of-n, conserves value", () =>
        {
            // a split: 150 / 100 / 49, summing to pot − fee = 299.
            var stacks = new List<SettlementMove.Payout>
            {
                new(pubs[0], 150), new(pubs[1], 100), new(pubs[2], 49),
            };
            var s = SettlementMove.Build(pot, stacks, fee);
            T.Eq(s.Outs.Count, 3, "every positive-stack seat gets a P2PKH output");
            T.Eq(s.Outs[0].Value, 150L, "seat 0 paid its stack");
            T.Eq(s.Outs[1].Value, 100L, "seat 1 paid its stack");
            T.Eq(s.Outs[2].Value, 49L, "seat 2 paid its stack");
            for (int i = 0; i < n; i++)
                T.True(s.Outs[i].Script.AsSpan().SequenceEqual(Chain.P2pkhLockForPub(pubs[i])), $"seat {i} paid to its own P2PKH");
            var signed = SignAll(s);
            T.True(SettlementMove.Verify(signed, pot), "the all-n split settlement verifies");
            T.Eq(signed.Outs.Sum(o => o.Value), potValue - fee, "Σ payouts == pot − fee");
            T.True(SettlementMove.ConservesValue(signed, pot, fee), "ConservesValue agrees");
        });

        T.Run("HOSTILE: an (n-1) coalition CANNOT settle the pot to a thief — the honest signature is mandatory", () =>
        {
            var thief = Secp256k1.GenerateKeyPair().Pub;
            // colluders try to pay the whole pot to a key they control
            var stacks = new List<SettlementMove.Payout> { new(thief, potValue - fee) };
            var s = SettlementMove.Build(pot, stacks, fee);
            // players 0,1 collude; player 2 (honest) withholds → only 2 signatures
            var two = new[] { kp[0], kp[1] }.Select(k => SettlementMove.Sign(s, pot, k.Priv)).ToList();
            T.False(SettlementMove.Verify(SettlementMove.Apply(s, two), pot), "an (n-1) coalition cannot settle the pot");
            // and they cannot fake the honest third slot with one of their own keys
            var forged = new[] { kp[0], kp[1], kp[0] }.Select(k => SettlementMove.Sign(s, pot, k.Priv)).ToList();
            T.False(SettlementMove.Verify(SettlementMove.Apply(s, forged), pot), "a colluder cannot stand in for the honest player's key");
        });

        T.Run("HOSTILE: a tampered payout amount fails verification (the sighash binds the outputs)", () =>
        {
            var stacks = new List<SettlementMove.Payout>
            {
                new(pubs[0], 150), new(pubs[1], 100), new(pubs[2], 49),
            };
            var s = SettlementMove.Build(pot, stacks, fee);
            var signed = SignAll(s);
            T.True(SettlementMove.Verify(signed, pot), "the honest settlement verifies first");
            // an attacker bumps seat 0's payout after signing
            var outs = signed.Outs.ToList();
            outs[0] = outs[0] with { Value = outs[0].Value + 50 };
            T.False(SettlementMove.Verify(signed with { Outs = outs }, pot), "a tampered payout amount breaks every signature");
            T.False(SettlementMove.ConservesValue(signed with { Outs = outs }, pot, fee), "and it no longer conserves value");
        });

        T.Run("HOSTILE: payouts exceeding the pot are rejected at build (no money creation)", () =>
        {
            // stacks summing to MORE than pot − fee
            var greedy = new List<SettlementMove.Payout>
            {
                new(pubs[0], potValue), new(pubs[1], 100), new(pubs[2], 0),
            };
            T.Throws(() => SettlementMove.Build(pot, greedy, fee), "Σ stacks > pot − fee must be rejected");
            // and a single seat claiming more than the whole pot
            var single = new List<SettlementMove.Payout> { new(pubs[0], potValue + 1) };
            T.Throws(() => SettlementMove.Build(pot, single, fee), "a payout above the pot is rejected");
            // under-paying (burning money) is rejected too — value must be conserved exactly
            var under = new List<SettlementMove.Payout> { new(pubs[0], 10), new(pubs[1], 10), new(pubs[2], 0) };
            T.Throws(() => SettlementMove.Build(pot, under, fee), "Σ stacks < pot − fee (burning) is rejected");
        });

        T.Run("HOSTILE: a smuggled over-pot settlement is caught by ConservesValue before signing", () =>
        {
            // build a legitimate settlement, then a peer rewrites an output to over-pay before broadcasting
            var stacks = new List<SettlementMove.Payout> { new(pubs[0], potValue - fee), new(pubs[1], 0), new(pubs[2], 0) };
            var s = SettlementMove.Build(pot, stacks, fee);
            var outs = s.Outs.ToList();
            outs[0] = outs[0] with { Value = potValue + 1000 };   // create money
            var tampered = s with { Outs = outs };
            T.False(SettlementMove.ConservesValue(tampered, pot, fee), "an over-pot payout is rejected by the value-conservation check (refuse to sign)");
        });

        T.Run("liveness: the pre-signed n-of-n REFUND returns each stake and binds the locktime if settlement stalls", () =>
        {
            var contributors = new List<(byte[] Pub, long Stake)> { (pubs[0], 100), (pubs[1], 100), (pubs[2], 100) };
            var refund = SettlementMove.Refund(pot, contributors, fee, lockHeight: 850_000);
            var signed = SignAll(refund);
            T.True(SettlementMove.Verify(signed, pot), "the pre-signed all-n refund verifies");
            T.Eq(signed.LockTime, 850_000u, "refund binds the agreed lock height");
            T.Eq(signed.Ins[0].Sequence, 0xfffffffeu, "non-final sequence so the locktime is enforced");
            T.Eq(signed.Outs.Sum(o => o.Value), potValue - fee, "every stake refunded (value conserved, fee aside)");
            // and the refund, like any n-of-n spend, needs ALL players — n-1 cannot grief-and-grab
            var two = new[] { kp[0], kp[1] }.Select(k => SettlementMove.Sign(refund, pot, k.Priv)).ToList();
            T.False(SettlementMove.Verify(SettlementMove.Apply(refund, two), pot), "an (n-1) coalition cannot broadcast the refund either");
        });
    }
}

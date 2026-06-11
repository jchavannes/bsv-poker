using BsvPoker.Core;
using BsvPoker.Crypto;

namespace BsvPoker.Tests;

/// <summary>
/// n-of-n pot escrow — STRUCTURAL safety against an (n-1) collusion, the principal's "build it so it can't be
/// compromised" at the consensus layer. The pot is locked to ALL n contributors; even if n-1 players collude
/// on a fully compromised system, they cannot move a satoshi without the ONE honest player's signature, so the
/// pot can only ever pay the outcome everyone agreed to (the provable winner). No theft is possible regardless
/// of who is dishonest. (Liveness if a party refuses is handled by the pre-agreed nLockTime threshold refund.)
/// </summary>
public static class OnChainNofNPotTests
{
    public static void All()
    {
        Console.WriteLine("n-of-n pot escrow (no theft even under an n-1 collusion):");
        const int n = 3;
        var kp = Enumerable.Range(0, n).Select(_ => Secp256k1.GenerateKeyPair()).ToArray();
        var pubs = kp.Select(k => k.Pub).ToList();
        const string potTxid = "cc" + "00000000000000000000000000000000000000000000000000000000000000";
        const long pot = 300, fee = 1;   // tiny per-tx fee (tech demo)

        T.Run("the pot is locked to ALL n contributors (OP_n …keys… OP_n OP_CHECKMULTISIG)", () =>
        {
            var lk = Chain.MultisigLockNofN(pubs);
            T.Eq(lk[0], (byte)(0x50 + n), "leading OP_n: n signatures required");
            T.Eq(lk[^1], (byte)0xae, "ends in OP_CHECKMULTISIG");
        });

        T.Run("all n sign → the winner is paid, value conserved, verified on the consensus path", () =>
        {
            var winner = Secp256k1.GenerateKeyPair().Pub;
            var tx = Chain.BuildCooperativeSettlement(potTxid, 0, pot, winner, fee);
            var sigs = kp.Select(k => Chain.SignMultisigN(tx, 0, pubs, pot, k.Priv)).ToList();
            var signed = Chain.ApplyMultisigScriptSigN(tx, 0, sigs);
            T.True(Chain.VerifyMultisigNofN(signed, 0, pubs, pot), "the all-n settlement verifies");
            T.Eq(signed.Outs[0].Value, pot - fee, "winner receives pot − fee (value conserved)");
        });

        T.Run("HOSTILE: any n-1 colluders CANNOT move the pot — the honest player's signature is mandatory", () =>
        {
            var thief = Secp256k1.GenerateKeyPair().Pub;                 // colluders try to pay themselves
            var tx = Chain.BuildCooperativeSettlement(potTxid, 0, pot, thief, fee);
            // players 0 and 1 collude; player 2 (honest) withholds. They can only produce 2 signatures.
            var two = new[] { kp[0], kp[1] }.Select(k => Chain.SignMultisigN(tx, 0, pubs, pot, k.Priv)).ToList();
            T.False(Chain.VerifyMultisigNofN(Chain.ApplyMultisigScriptSigN(tx, 0, two), 0, pubs, pot), "an (n-1) coalition cannot spend the pot");
            // and they cannot fake the third slot with one of their own keys (it must be the honest player's)
            var forged = new[] { kp[0], kp[1], kp[0] }.Select(k => Chain.SignMultisigN(tx, 0, pubs, pot, k.Priv)).ToList();
            T.False(Chain.VerifyMultisigNofN(Chain.ApplyMultisigScriptSigN(tx, 0, forged), 0, pubs, pot), "a colluder cannot stand in for the honest player's key");
        });

        T.Run("liveness: an all-n PRE-SIGNED nLockTime refund returns every stake if settlement stalls", () =>
        {
            // contributors co-sign the refund at SETUP (before funding); anyone can broadcast it after the timeout
            var contributors = new List<(byte[] Pub, long Stake)> { (pubs[0], 100), (pubs[1], 100), (pubs[2], 100) };
            var rec = Chain.BuildNofNRecovery(potTxid, 0, contributors, fee, lockHeight: 850_000);
            var sigs = kp.Select(k => Chain.SignMultisigN(rec, 0, pubs, pot, k.Priv)).ToList();
            var signed = Chain.ApplyMultisigScriptSigN(rec, 0, sigs);
            T.True(Chain.VerifyMultisigNofN(signed, 0, pubs, pot), "the pre-signed all-n refund verifies");
            T.Eq(signed.LockTime, 850_000u, "refund binds the agreed lock height");
            T.Eq(signed.Ins[0].Sequence, 0xfffffffeu, "non-final sequence so the locktime is enforced");
            T.Eq(signed.Outs.Sum(o => o.Value), pot - fee, "every stake is refunded (value conserved, fee aside)");
        });

        T.Run("the signatures bind the outputs: a tampered amount fails verification", () =>
        {
            var winner = Secp256k1.GenerateKeyPair().Pub;
            var tx = Chain.BuildCooperativeSettlement(potTxid, 0, pot, winner, fee);
            var sigs = kp.Select(k => Chain.SignMultisigN(tx, 0, pubs, pot, k.Priv)).ToList();
            var signed = Chain.ApplyMultisigScriptSigN(tx, 0, sigs);
            T.False(Chain.VerifyMultisigNofN(signed, 0, pubs, pot + 1), "a tampered amount breaks every signature (sighash binds the value)");
        });
    }
}

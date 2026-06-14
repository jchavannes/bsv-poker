using BsvPoker.Core;
using BsvPoker.Core.Games;
using BsvPoker.Crypto;

namespace BsvPoker.Tests;

/// <summary>
/// ZERO-CONF resilient move-chain: every in-game move spends an n-of-n committed coin into the next, so a
/// move is consensus-valid at ZERO confirmations yet a state-level adversary cannot double-spend the coin or
/// force an out-of-rules move. The honest player's single-successor signature is the defence; liveness is the
/// pre-signed n-of-n nLockTime recovery. Proven primitives only — no fabricated covenant.
/// </summary>
public static class ZeroConfMoveChainTests
{
    public static void All()
    {
        Console.WriteLine("zero-conf resilient move-chain (every move n-of-n; no double-spend at 0-conf):");
        const int n = 3;
        var kp = Enumerable.Range(0, n).Select(_ => Secp256k1.GenerateKeyPair()).ToArray();
        var pubs = kp.Select(k => k.Pub).ToList();
        string genesisTxid = "ab".PadRight(64, '0');
        const long value = 1000, fee = 1;
        var handId = new byte[16];

        byte[][] BetFields(int seat, byte action, long amt) =>
            new[] { handId, new[] { (byte)seat }, new[] { action }, BitConverter.GetBytes(amt) };

        T.Run("the committed coin is n-of-n (every player must consent to advance it)", () =>
        {
            var lk = ZeroConfMoveChain.Commit(pubs);
            T.Eq(lk[0], (byte)(0x50 + n), "OP_n: all n signatures required to spend the committed coin");
            T.Eq(lk[^1], (byte)0xae, "ends in OP_CHECKMULTISIG");
        });

        T.Run("a 3-move chain: each move verifies on the consensus path and conserves value (− fee/move)", () =>
        {
            var cur = new ZeroConfMoveChain.Committed(genesisTxid, 0, value, pubs);
            for (int m = 0; m < 3; m++)
            {
                var move = ZeroConfMoveChain.BuildMove(cur.Txid, cur.Vout, cur.Value, pubs, TxKind.Bet, pubs[m % n], BetFields(m % n, 2, 10), fee);
                var sigs = kp.Select(k => ZeroConfMoveChain.Sign(move, pubs, cur.Value, k.Priv)).ToList();
                var signed = ZeroConfMoveChain.Apply(move, sigs);
                T.True(ZeroConfMoveChain.Verify(signed, pubs, cur.Value), $"move {m} verifies (all n signed)");
                T.Eq(signed.Outs[0].Value, cur.Value - fee, $"move {m}: committed coin continues at value − fee");
                T.Eq(signed.Outs.Sum(o => o.Value), cur.Value - fee, $"move {m}: value conserved (data out = 0)");
                T.True(TxTemplates.Parse(signed.Outs[1].Script) is { Kind: TxKind.Bet }, $"move {m}: the bound move-data output is a typed Bet");
                cur = ZeroConfMoveChain.Next(signed, pubs);
            }
        });

        T.Run("NO DOUBLE-SPEND at 0-conf: the honest player signs only ONE successor; a conflicting move cannot verify", () =>
        {
            var moveA = ZeroConfMoveChain.BuildMove(genesisTxid, 0, value, pubs, TxKind.Bet, pubs[0], BetFields(0, 2, 10), fee);
            var moveB = ZeroConfMoveChain.BuildMove(genesisTxid, 0, value, pubs, TxKind.Bet, pubs[1], BetFields(1, 2, 99), fee);
            var sigsA = kp.Select(k => ZeroConfMoveChain.Sign(moveA, pubs, value, k.Priv)).ToList();
            T.True(ZeroConfMoveChain.Verify(ZeroConfMoveChain.Apply(moveA, sigsA), pubs, value), "the agreed successor A verifies");
            // players 0,1 try to also push conflicting move B; the honest player (2) WITHHOLDS its signature
            var sigsB = new[] { kp[0], kp[1] }.Select(k => ZeroConfMoveChain.Sign(moveB, pubs, value, k.Priv)).ToList();
            T.False(ZeroConfMoveChain.Verify(ZeroConfMoveChain.Apply(moveB, sigsB), pubs, value), "the conflicting double-spend B cannot reach n-of-n → rejected");
        });

        T.Run("CONSTRAINED ACTION SET: an out-of-rules move the honest player refuses to sign is invalid", () =>
        {
            var bad = ZeroConfMoveChain.BuildMove(genesisTxid, 0, value, pubs, TxKind.Bet, pubs[0], BetFields(0, 2, 1_000_000), fee);
            var partial = new[] { kp[0], kp[1] }.Select(k => ZeroConfMoveChain.Sign(bad, pubs, value, k.Priv)).ToList(); // player 2 refuses
            T.False(ZeroConfMoveChain.Verify(ZeroConfMoveChain.Apply(bad, partial), pubs, value), "an out-of-rules move never gathers n signatures");
        });

        T.Run("the move data is BOUND: tampering the move-data output breaks every signature", () =>
        {
            var move = ZeroConfMoveChain.BuildMove(genesisTxid, 0, value, pubs, TxKind.Bet, pubs[0], BetFields(0, 2, 10), fee);
            var sigs = kp.Select(k => ZeroConfMoveChain.Sign(move, pubs, value, k.Priv)).ToList();
            var signed = ZeroConfMoveChain.Apply(move, sigs);
            var outs = signed.Outs.ToList();
            var badScript = (byte[])outs[1].Script.Clone(); badScript[5] ^= 0xFF;     // flip a byte in the move-data output
            outs[1] = outs[1] with { Script = badScript };
            T.False(ZeroConfMoveChain.Verify(signed with { Outs = outs }, pubs, value), "a tampered move-data output fails the n-of-n sighash");
        });

        T.Run("LIVENESS: the pre-signed n-of-n nLockTime recovery refunds every stake if the chain stalls", () =>
        {
            var contributors = pubs.Select(p => (p, value / n)).ToList();
            var rec = ZeroConfMoveChain.Recovery(genesisTxid, 0, contributors, fee, 850_000);
            var sigs = kp.Select(k => ZeroConfMoveChain.Sign(rec, pubs, value, k.Priv)).ToList();
            var signed = ZeroConfMoveChain.Apply(rec, sigs);
            T.True(ZeroConfMoveChain.Verify(signed, pubs, value), "the pre-signed all-n refund verifies");
            T.Eq(signed.LockTime, 850_000u, "refund binds the agreed lock height");
            T.Eq(signed.Ins[0].Sequence, 0xfffffffeu, "non-final sequence so the locktime is enforced");
        });
    }
}

namespace BsvPoker.Core;

/// <summary>
/// Threshold-custody POT — the pot is locked to a single ordinary P2PKH output whose key is a DEALERLESS
/// (t+1)-of-n threshold key (<see cref="ThresholdEcdsa"/>). No dealer ever exists, no player holds the key,
/// and no sub-threshold coalition can move the money: paying the winner (or refunding via nLockTime) requires
/// t+1 players to jointly produce a single standard ECDSA signature. On-chain the pot looks like any P2PKH
/// output — there is no multisig script to fingerprint the table (the EP4152683B1 privacy/custody property).
///
/// This is the money organ the project's threat model demands: "all players dishonest, all servers
/// compromised, collusion between any sub-threshold set assumed." The settlement and the recovery both
/// verify on the EXACT consensus path a single-key spend uses (<see cref="Chain.VerifyP2pkhInput"/>), because
/// the threshold signature is indistinguishable from a single-key signature.
/// </summary>
public static class ThresholdCustody
{
    /// <summary>The pot's locking script: an ordinary P2PKH to the threshold public key a·G.</summary>
    public static byte[] PotLock(ThresholdEcdsa.Shared key) => Chain.P2pkhLockForPub(key.PublicKey);

    /// <summary>
    /// Pay the winner from the pot by THRESHOLD signing the payout transaction. Builds a one-output P2PKH
    /// payout (winner gets amount−fee), computes the FORKID sighash, asks the t+1-of-n parties to jointly sign
    /// it, and assembles the standard scriptSig. The returned tx verifies with the ordinary verifier.
    /// </summary>
    public static Chain.Tx SettleToWinner(string potTxid, uint vout, long amount, byte[] winnerPub, long fee, ThresholdEcdsa.Shared key)
    {
        if (fee < 0 || fee >= amount) throw new ArgumentException("fee out of range (0 ≤ fee < amount)");
        var outs = new List<Chain.TxOut> { new(amount - fee, Chain.P2pkhLockForPub(winnerPub)) };
        var ins = new List<Chain.TxIn> { new(potTxid, vout, Array.Empty<byte>(), 0xffffffff) };
        var tx = new Chain.Tx(2, ins, outs, 0);
        var digest = Chain.SighashForkId(tx, 0, PotLock(key), amount);
        var sig = ThresholdEcdsa.SignDigest(digest, key);
        return Chain.ApplyP2pkhSig(tx, 0, sig, key.PublicKey);
    }

    /// <summary>
    /// Settle a pot that accumulated as MANY UTXOs (the fully-on-chain model: every betting action is its own
    /// funded tx paying into the threshold pot key, so the pot is a set of outputs). Spends ALL of them to the
    /// winner in one tx, THRESHOLD-signing each input (each input's FORKID sighash is independent of the others,
    /// so they can be signed separately). Verifies on the ordinary consensus path, input by input.
    /// </summary>
    public static Chain.Tx SettleManyToWinner(IReadOnlyList<(string Txid, uint Vout, long Value)> potUtxos, byte[] winnerPub, long fee, ThresholdEcdsa.Shared key)
    {
        if (potUtxos.Count == 0) throw new ArgumentException("no pot outputs");
        long total = potUtxos.Sum(u => u.Value);
        if (fee < 0 || fee >= total) throw new ArgumentException("fee out of range (0 ≤ fee < pot total)");
        var ins = potUtxos.Select(u => new Chain.TxIn(u.Txid, u.Vout, Array.Empty<byte>(), 0xffffffff)).ToList();
        var outs = new List<Chain.TxOut> { new(total - fee, Chain.P2pkhLockForPub(winnerPub)) };
        var tx = new Chain.Tx(2, ins, outs, 0);
        var potLock = PotLock(key);
        for (int i = 0; i < potUtxos.Count; i++)
        {
            var digest = Chain.SighashForkId(tx, i, potLock, potUtxos[i].Value);
            var sig = ThresholdEcdsa.SignDigest(digest, key);
            tx = Chain.ApplyP2pkhSig(tx, i, sig, key.PublicKey);
        }
        return tx;
    }

    /// <summary>
    /// A pre-agreed nLockTime RECOVERY for the pot, THRESHOLD signed: refunds each stake (one output per
    /// payee) but is only final after <paramref name="lockHeight"/> (non-final sequence so the locktime binds).
    /// The parties co-sign this before funding, so the pot can never be stranded if play cannot complete.
    /// </summary>
    public static Chain.Tx BuildRecovery(string potTxid, uint vout, long amount, IReadOnlyList<(byte[] Pub, long Amount)> refunds, ThresholdEcdsa.Shared key, uint lockHeight)
    {
        foreach (var rf in refunds) if (rf.Amount < 0) throw new ArgumentException("negative refund");
        var outs = refunds.Where(r => r.Amount > 0).Select(r => new Chain.TxOut(r.Amount, Chain.P2pkhLockForPub(r.Pub))).ToList();
        if (outs.Count == 0) throw new ArgumentException("no positive refunds");
        if (outs.Sum(o => o.Value) > amount) throw new ArgumentException("refunds exceed the pot (would create money)");
        var ins = new List<Chain.TxIn> { new(potTxid, vout, Array.Empty<byte>(), 0xfffffffe) }; // non-final → locktime active
        var tx = new Chain.Tx(2, ins, outs, lockHeight);
        var digest = Chain.SighashForkId(tx, 0, PotLock(key), amount);
        var sig = ThresholdEcdsa.SignDigest(digest, key);
        return Chain.ApplyP2pkhSig(tx, 0, sig, key.PublicKey);
    }
}

using BsvPoker.Crypto;

namespace BsvPoker.Core.Games;

/// <summary>
/// The on-chain POT for multiplayer group Blackjack. Every player funds the pot together into ONE n-of-n
/// locked output (<see cref="Chain.MultisigLockNofN"/>) — for a 3-player game that is a 3-of-3 output — so no
/// subset of players can move the money: settlement requires EVERY player's signature, exactly as the
/// principal specified. After a hand the pot is distributed by each player's result (win/lose/push/blackjack),
/// and the REMAINING bank is split among the remaining players. The total is conserved to the satoshi; a
/// pre-agreed n-of-n nLockTime recovery (<see cref="Chain.BuildNofNRecovery"/>) guarantees no stake is ever
/// stranded if a player griefs by refusing to co-sign.
/// </summary>
public static class BlackjackPot
{
    /// <summary>The FINAL per-seat amount each player receives from a completed hand: their bet ± result, plus
    /// an equal share of the remaining dealer bank ("the remainder of the pot is distributed between the
    /// remaining players"). The returned amounts sum EXACTLY to the pot (dealerBank + all bets).</summary>
    public static long[] Settle(GroupBlackjack hand, long dealerBank)
    {
        var (payouts, remaining) = hand.Distribute(dealerBank);
        int n = hand.Players;
        var final = new long[n];
        long share = remaining / n, extra = remaining - share * n;   // remainder of the integer division to seat 0
        for (int i = 0; i < n; i++) final[i] = payouts[i] + share + (i == 0 ? extra : 0);
        return final;
    }

    /// <summary>Build the (unsigned) settlement that spends the n-of-n pot, paying each player their final amount
    /// to their own address. Requires sum(finalAmounts) + fee == potValue (value conserved). Every player then
    /// signs it (<see cref="Chain.SignMultisigN"/>) and the sigs are assembled in pubkey order.</summary>
    public static Chain.Tx BuildSettlement(string potTxid, uint vout, long potValue, IReadOnlyList<byte[]> playerPubs, IReadOnlyList<long> finalAmounts, long fee)
    {
        if (playerPubs.Count != finalAmounts.Count) throw new ArgumentException("a final amount per player");
        if (fee < 0) throw new ArgumentException("negative fee");
        long paid = finalAmounts.Sum();
        if (paid + fee != potValue) throw new ArgumentException($"settlement does not conserve the pot: pays {paid} + fee {fee} != pot {potValue}");
        var outs = new List<Chain.TxOut>();
        for (int i = 0; i < playerPubs.Count; i++)
            if (finalAmounts[i] > 0) outs.Add(new Chain.TxOut(finalAmounts[i], Chain.P2pkhLockForPub(playerPubs[i])));
        if (outs.Count == 0) throw new ArgumentException("no positive payouts");
        var ins = new List<Chain.TxIn> { new(potTxid, vout, Array.Empty<byte>(), 0xffffffff) };
        return new Chain.Tx(2, ins, outs, 0);
    }

    /// <summary>Assemble a fully co-signed settlement: every player signs the same unsigned tx; sigs go in
    /// pubkey order. The result verifies under <see cref="Chain.VerifyMultisigNofN"/> only when ALL signed.</summary>
    public static Chain.Tx CoSign(Chain.Tx unsigned, IReadOnlyList<byte[]> playerPubs, long potValue, IReadOnlyList<byte[]> playerSeedsInPubOrder)
    {
        var sigs = new List<byte[]>();
        for (int i = 0; i < playerPubs.Count; i++) sigs.Add(Chain.SignMultisigN(unsigned, 0, playerPubs, potValue, playerSeedsInPubOrder[i]));
        return Chain.ApplyMultisigScriptSigN(unsigned, 0, sigs);
    }
}

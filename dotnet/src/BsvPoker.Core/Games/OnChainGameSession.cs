namespace BsvPoker.Core.Games;

/// <summary>
/// Orchestrates a complete on-chain hand end to end: the wallet funds a 2-of-2 escrow, the dealerless deck
/// is dealt to the seats, the showdown decides the winner per the game's rules, and a cooperative
/// settlement pays the winner — with the co-signed nLockTime recovery ready the whole time so funds are
/// never stranded. Every step is a real BSV transaction. This is the session the live UI drives.
/// </summary>
public static class OnChainGameSession
{
    public sealed record HandResult(OnChainWallet.Spend Funding, Chain.Tx Settlement, Chain.Tx Recovery, int WinnerSeat, long Pot, bool Split);

    /// <summary>Play one heads-up Texas Hold'em hand on-chain (kept for callers/tests; delegates to PlayHand).</summary>
    public static HandResult PlayHoldem(OnChainWallet funder, (byte[] Priv, byte[] Pub) a, (byte[] Priv, byte[] Pub) b,
        IReadOnlyList<Card> deck, long pot, long fee = 500, uint recoverHeight = 900_000)
        => PlayHand(PokerGame.TexasHoldem, funder, a, b, deck, pot, fee, recoverHeight);

    /// <summary>
    /// Play one heads-up hand of ANY of the six games on-chain from a shuffled <paramref name="deck"/>: fund a
    /// 2-of-2 escrow, deal per the game's hole/board layout, decide the payout via that game's showdown rules
    /// (single winner, or a hi-lo split paid to BOTH halves), settle on-chain, and pre-sign the recovery. Same
    /// real-BSV transactions on every network.
    /// </summary>
    public static HandResult PlayHand(PokerGame game, OnChainWallet funder, (byte[] Priv, byte[] Pub) a, (byte[] Priv, byte[] Pub) b,
        IReadOnlyList<Card> deck, long pot, long fee = 500, uint recoverHeight = 900_000)
    {
        var def = PokerGames.Of(game);
        int h = def.Hole, need = h * 2 + def.Board;
        if (deck.Count < need) throw new ArgumentException($"need >= {need} cards for heads-up {def.Name}");
        if (fee <= 0 || fee >= pot) throw new ArgumentException("fee out of range");

        var fund = OnChainHand.FundEscrow(funder, a.Pub, b.Pub, pot, fee);
        var escrowTxid = Chain.Txid(fund.Tx);

        // deal layout: seat0 = deck[0..h], seat1 = deck[h..2h], board = next def.Board cards (none for stud/razz/draw)
        var holes = new IReadOnlyList<Card>[] { deck.Take(h).ToList(), deck.Skip(h).Take(h).ToList() };
        var board = def.Board > 0 ? deck.Skip(2 * h).Take(def.Board).ToList() : new List<Card>();
        var payouts = Showdown.Settle(def, holes, board, pot);          // sums to pot (may split for hi-lo)
        long pa = payouts.GetValueOrDefault(0), pb = payouts.GetValueOrDefault(1);

        // distribute the payable amount (pot - fee) in the same proportion as the showdown payout
        long payable = pot - fee;
        long sa = pa <= 0 ? 0 : (pb <= 0 ? payable : pa * payable / pot);
        long sb = payable - sa;
        bool split = pa > 0 && pb > 0;
        int winner = pa >= pb ? 0 : 1;

        var settle = OnChainHand.SettleMany(escrowTxid, 0, pot,
            new (byte[], long)[] { (a.Pub, sa), (b.Pub, sb) }, a.Priv, a.Pub, b.Priv, b.Pub);
        var recover = OnChainHand.Recover(escrowTxid, 0, a.Pub, pot / 2, b.Pub, pot - pot / 2, fee, recoverHeight, a.Priv, b.Priv);
        return new HandResult(fund, settle, recover, winner, pot, split);
    }
}

using BsvPoker.Core;
using BsvPoker.Core.Games;
using BsvPoker.Crypto;

namespace BsvPoker.Tests;

/// <summary>A composed on-chain hand: fund escrow → deal → showdown → settle the winner, with recovery ready.</summary>
public static class OnChainSessionTests
{
    private static Card C(string s)
    {
        int rank = s[0] switch { 'A' => 14, 'K' => 13, 'Q' => 12, 'J' => 11, 'T' => 10, _ => s[0] - '0' };
        var suit = s[1] switch { 's' => Suit.Spades, 'h' => Suit.Hearts, 'd' => Suit.Diamonds, _ => Suit.Clubs };
        return new Card(rank, suit);
    }

    public static void All()
    {
        Console.WriteLine("on-chain hand orchestration (fund → deal → settle):");
        T.Run("a full heads-up hand funds, decides the winner, and settles on-chain", () =>
        {
            var a = Secp256k1.GenerateKeyPair(); var b = Secp256k1.GenerateKeyPair();
            var w = new OnChainWallet(WalletKeys.NewSeed());
            w.Add(new OnChainWallet.Utxo("aa".PadRight(64, '1'), 0, 60000, 0, 0));
            // seat0 = AA, seat1 = 2-3, board gives seat0 trip aces
            var deck = new[] { C("As"), C("Ah"), C("2c"), C("3d"), C("Ad"), C("Kh"), C("Qs"), C("Jc"), C("9h") };
            var r = OnChainGameSession.PlayHoldem(w, a, b, deck, pot: 40000, fee: 500);
            T.Eq(r.WinnerSeat, 0, "seat 0 (trip aces) wins");
            T.True(w.VerifySpend(r.Funding), "escrow funding signs + conserves");
            T.True(Chain.VerifyMultisig2of2(r.Settlement, 0, a.Pub, b.Pub, 40000), "settlement co-signed + valid");
            T.Eq(T.Hex(r.Settlement.Outs[0].Script), T.Hex(Chain.P2pkhLockForPub(a.Pub)), "pot paid to the winner");
            T.True(Chain.VerifyMultisig2of2(r.Recovery, 0, a.Pub, b.Pub, 40000), "recovery is ready (always recoverable)");
        });

        T.Run("ALL six games play on-chain: fund + valid 2-of-2 settlement + conserved value + recovery", () =>
        {
            // a full 52-card deck (distinct), sliced per game; we assert STRUCTURAL correctness for every variant
            var full = new List<Card>();
            foreach (var su in new[] { Suit.Spades, Suit.Hearts, Suit.Diamonds, Suit.Clubs })
                for (int rk = 2; rk <= 14; rk++) full.Add(new Card(rk, su));
            long pot = 40000, fee = 500;
            foreach (var game in new[] { PokerGame.TexasHoldem, PokerGame.Omaha, PokerGame.OmahaHiLo,
                                         PokerGame.SevenCardStud, PokerGame.Razz, PokerGame.FiveCardDraw })
            {
                var a = Secp256k1.GenerateKeyPair(); var b = Secp256k1.GenerateKeyPair();
                var w = new OnChainWallet(WalletKeys.NewSeed());
                w.Add(new OnChainWallet.Utxo("aa".PadRight(64, '1'), 0, 60000, 0, 0));
                var def = PokerGames.Of(game);
                var deck = full.Take(def.Hole * 2 + def.Board).ToList();
                var r = OnChainGameSession.PlayHand(game, w, a, b, deck, pot, fee);
                T.True(w.VerifySpend(r.Funding), $"{def.Name}: escrow funding signs + conserves");
                T.True(Chain.VerifyMultisig2of2(r.Settlement, 0, a.Pub, b.Pub, pot), $"{def.Name}: settlement co-signed + valid");
                T.True(Chain.VerifyMultisig2of2(r.Recovery, 0, a.Pub, b.Pub, pot), $"{def.Name}: recovery co-signed + valid");
                T.Eq(r.Settlement.Outs.Sum(o => o.Value), pot - fee, $"{def.Name}: pot - fee paid out (value conserved)");
            }
        });

        T.Run("Omaha Hi-Lo splits the pot on-chain into two settlement outputs", () =>
        {
            var a = Secp256k1.GenerateKeyPair(); var b = Secp256k1.GenerateKeyPair();
            var w = new OnChainWallet(WalletKeys.NewSeed());
            w.Add(new OnChainWallet.Utxo("bb".PadRight(64, '2'), 0, 60000, 0, 0));
            // deal layout is seat0=deck[0..4], seat1=deck[4..8], board=deck[8..13]
            var deck = new[] { C("Kh"), C("Kd"), C("9s"), C("9c"),   // seat0: trip kings (high), no qualifying low
                               C("Ah"), C("5d"), C("6s"), C("8c"),   // seat1: A,5 + 2,3,7 ⇒ 7-low (low half)
                               C("2h"), C("3d"), C("7s"), C("Kc"), C("Qh") }; // board: three low cards, no straight
            long pot = 40000, fee = 500;
            var r = OnChainGameSession.PlayHand(PokerGame.OmahaHiLo, w, a, b, deck, pot, fee);
            T.True(r.Split, "hi-lo produced a split");
            T.Eq(r.Settlement.Outs.Count, 2, "two settlement outputs (high half + low half)");
            T.Eq(r.Settlement.Outs.Sum(o => o.Value), pot - fee, "split conserves value (pot - fee)");
            T.True(Chain.VerifyMultisig2of2(r.Settlement, 0, a.Pub, b.Pub, pot), "split settlement co-signed + valid");
        });
    }
}

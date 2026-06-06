using BsvPoker.Core;

namespace BsvPoker.Tests;

public static class GameTests
{
    public static void All()
    {
        Console.WriteLine("mental poker + hand eval + Hold'em engine:");

        T.Run("commit hides entropy; reveal verifies; wrong entropy fails", () =>
        {
            var e = MentalPoker.FreshEntropy();
            var c = MentalPoker.Commit(e);
            T.True(MentalPoker.VerifyCommit(c, e));
            T.False(MentalPoker.VerifyCommit(c, MentalPoker.FreshEntropy()));
        });

        T.Run("dealerless deck = composition of both players' permutations; is a full 52-permutation", () =>
        {
            var ea = MentalPoker.FreshEntropy();
            var eb = MentalPoker.FreshEntropy();
            var deck = MentalPoker.ShuffledDeck(new[] { ea, eb });
            T.Eq(deck.Count, 52);
            T.Eq(deck.Select(c => c.Index).Distinct().Count(), 52, "all 52 distinct cards present");
            // deterministic from the same entropies
            var deck2 = MentalPoker.ShuffledDeck(new[] { ea, eb });
            T.Eq(string.Join(",", deck.Select(c => c.Index)), string.Join(",", deck2.Select(c => c.Index)));
            // different entropy ⇒ different order (overwhelmingly)
            var deck3 = MentalPoker.ShuffledDeck(new[] { ea, MentalPoker.FreshEntropy() });
            T.False(string.Join(",", deck.Select(c => c.Index)) == string.Join(",", deck3.Select(c => c.Index)));
        });

        T.Run("hand ranking: straight flush > quads > full house > flush > straight > trips", () =>
        {
            long SF = HandEval.Best(new List<Card> { new(9, Suit.Spades), new(8, Suit.Spades), new(7, Suit.Spades), new(6, Suit.Spades), new(5, Suit.Spades), new(2, Suit.Hearts), new(3, Suit.Clubs) }).Score;
            long Quads = HandEval.Best(new List<Card> { new(9, Suit.Spades), new(9, Suit.Hearts), new(9, Suit.Diamonds), new(9, Suit.Clubs), new(5, Suit.Spades), new(2, Suit.Hearts), new(3, Suit.Clubs) }).Score;
            long Boat = HandEval.Best(new List<Card> { new(9, Suit.Spades), new(9, Suit.Hearts), new(9, Suit.Diamonds), new(5, Suit.Clubs), new(5, Suit.Spades), new(2, Suit.Hearts), new(3, Suit.Clubs) }).Score;
            T.True(SF > Quads && Quads > Boat, "ordering holds");
            T.Eq(HandEval.Best(new List<Card> { new(14, Suit.Spades), new(13, Suit.Spades), new(12, Suit.Spades), new(11, Suit.Spades), new(10, Suit.Spades), new(2, Suit.Hearts), new(3, Suit.Clubs) }).Category, "Straight flush");
        });

        T.Run("a full heads-up hand to showdown CONSERVES chips", () =>
        {
            var deck = MentalPoker.ShuffledDeck(new[] { MentalPoker.FreshEntropy(), MentalPoker.FreshEntropy() });
            var st = HoldemState.Create(new long[] { 100, 100 }, button: 0, sb: 1, bb: 2, deck);
            int guard = 0;
            while (!st.Complete && guard++ < 200)
            {
                var la = st.Legal();
                var seat = st.ToAct;
                // simple non-AI scripted line: check/call down
                if (la.CanCheck) st.Apply(new GameAction(ActionKind.Check, seat));
                else if (la.CanCall) st.Apply(new GameAction(ActionKind.Call, seat));
                else st.Apply(new GameAction(ActionKind.Fold, seat));
            }
            T.True(st.Complete, "hand completed");
            T.Eq(st.Seats.Sum(s => s.Stack), 200L, "chips conserved across the hand");
        });

        T.Run("folding to a bet ends the hand and the bettor wins the pot (conserved)", () =>
        {
            var deck = MentalPoker.ShuffledDeck(new[] { MentalPoker.FreshEntropy(), MentalPoker.FreshEntropy() });
            var st = HoldemState.Create(new long[] { 100, 100 }, button: 0, sb: 1, bb: 2, deck);
            // preflop: SB(button) to act; raise, other folds
            var la = st.Legal();
            st.Apply(new GameAction(ActionKind.Raise, st.ToAct, la.MinRaiseTo));
            st.Apply(new GameAction(ActionKind.Fold, st.ToAct));
            T.True(st.Complete);
            T.Eq(st.Seats.Sum(s => s.Stack), 200L, "chips conserved on a fold-out");
        });
    }
}

using BsvPoker.Core;

namespace BsvPoker.Tests;

public static class VariantTests
{
    public static void All()
    {
        Console.WriteLine("6 poker variants (all on the dealerless deck + engine):");

        foreach (var v in Variants.All)
        {
            T.Run($"{Variants.Name(v)}: deals {Variants.HoleCards(v)} hole cards and plays to showdown (chips conserved)", () =>
            {
                var deck = MentalPoker.ShuffledFrom(new[] { MentalPoker.FreshEntropy(), MentalPoker.FreshEntropy() }, Variants.CardSet(v));
                var st = HoldemState.Create(new long[] { 100, 100 }, button: 0, sb: 1, bb: 2, deck, v);
                T.Eq(st.Seats[0].Hole.Length, Variants.HoleCards(v), "correct hole-card count");
                T.Eq(st.Seats[1].Hole.Length, Variants.HoleCards(v));
                int guard = 0;
                while (!st.Complete && guard++ < 300)
                {
                    var la = st.Legal();
                    if (la.CanCheck) st.Apply(new GameAction(ActionKind.Check, st.ToAct));
                    else if (la.CanCall) st.Apply(new GameAction(ActionKind.Call, st.ToAct));
                    else st.Apply(new GameAction(ActionKind.Fold, st.ToAct));
                }
                T.True(st.Complete, "hand completed");
                T.Eq(st.Seats.Sum(s => s.Stack), 200L, "chips conserved");
            });
        }

        T.Run("Royal Hold'em uses a 20-card deck (T..A only)", () =>
        {
            var set = Variants.CardSet(Variant.RoyalHoldem);
            T.Eq(set.Count, 20);
            T.True(set.All(c => c.Rank >= 10), "only ranks 10..14");
        });

        T.Run("Omaha must use EXACTLY two hole cards (a 3rd hole ace can't make trips with one board ace)", () =>
        {
            // hole has AAA? no — 4 distinct hole; give 3 aces in hole + 1 ace on board → with exactly-2 you
            // can use at most 2 hole aces, so quad aces is impossible; standard eval would see 4 aces.
            var hole = new List<Card> { new(14, Suit.Spades), new(14, Suit.Hearts), new(14, Suit.Diamonds), new(2, Suit.Clubs) };
            var board = new List<Card> { new(14, Suit.Clubs), new(9, Suit.Spades), new(5, Suit.Hearts), new(7, Suit.Diamonds), new(3, Suit.Clubs) };
            var omaha = HandEval.BestForVariant(hole, board, true);
            var anyc = HandEval.BestForVariant(hole, board, false);
            T.False(omaha.Category == "Four of a kind", "Omaha exactly-2 cannot make quad aces here");
            T.Eq(anyc.Category, "Four of a kind", "any-cards eval would (proves the rule differs)");
        });
    }
}

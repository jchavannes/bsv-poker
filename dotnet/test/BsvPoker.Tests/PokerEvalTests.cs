using BsvPoker.Core;
using BsvPoker.Core.Games;

namespace BsvPoker.Tests;

/// <summary>The poker hand evaluator: category ordering, the wheel straight, kickers, and best-of-seven.</summary>
public static class PokerEvalTests
{
    private static Card C(string s)
    {
        int rank = s[0] switch { 'A' => 14, 'K' => 13, 'Q' => 12, 'J' => 11, 'T' => 10, _ => s[0] - '0' };
        var suit = s[1] switch { 's' => Suit.Spades, 'h' => Suit.Hearts, 'd' => Suit.Diamonds, _ => Suit.Clubs };
        return new Card(rank, suit);
    }
    private static Card[] H(params string[] cs) => cs.Select(C).ToArray();
    private static long S(params string[] cs) => PokerEval.Best(H(cs)).Score;
    private static HandCategory Cat(params string[] cs) => PokerEval.Best(H(cs)).Category;

    public static void All()
    {
        Console.WriteLine("poker hand evaluator:");

        T.Run("each category is detected", () =>
        {
            T.Eq(Cat("Ah", "Kd", "Qs", "Jc", "9h").ToString(), "HighCard");
            T.Eq(Cat("Ah", "Ad", "Qs", "Jc", "9h").ToString(), "Pair");
            T.Eq(Cat("Ah", "Ad", "Qs", "Qc", "9h").ToString(), "TwoPair");
            T.Eq(Cat("Ah", "Ad", "As", "Qc", "9h").ToString(), "Trips");
            T.Eq(Cat("5h", "6d", "7s", "8c", "9h").ToString(), "Straight");
            T.Eq(Cat("Ah", "Kh", "Qh", "Jh", "9h").ToString(), "Flush");
            T.Eq(Cat("Ah", "Ad", "As", "Qc", "Qh").ToString(), "FullHouse");
            T.Eq(Cat("Ah", "Ad", "As", "Ac", "Qh").ToString(), "Quads");
            T.Eq(Cat("5h", "6h", "7h", "8h", "9h").ToString(), "StraightFlush");
        });

        T.Run("category strength is strictly ordered", () =>
        {
            long hc = S("Ah", "Kd", "Qs", "Jc", "9h"), pr = S("2h", "2d", "Qs", "Jc", "9h");
            long tp = S("2h", "2d", "3s", "3c", "9h"), tr = S("2h", "2d", "2s", "Jc", "9h");
            long st = S("5h", "6d", "7s", "8c", "9h"), fl = S("2h", "5h", "7h", "9h", "Jh");
            long fh = S("2h", "2d", "2s", "3c", "3h"), q = S("2h", "2d", "2s", "2c", "3h");
            long sf = S("5h", "6h", "7h", "8h", "9h");
            T.True(hc < pr && pr < tp && tp < tr && tr < st && st < fl && fl < fh && fh < q && q < sf, "ordering holds");
        });

        T.Run("the wheel (A-2-3-4-5) is a straight, and lower than 2-6", () =>
        {
            T.Eq(Cat("Ah", "2d", "3s", "4c", "5h").ToString(), "Straight");
            T.True(S("Ah", "2d", "3s", "4c", "5h") < S("2h", "3d", "4s", "5c", "6h"), "wheel < six-high straight");
            T.True(S("Th", "Jd", "Qs", "Kc", "Ah") > S("9h", "Td", "Js", "Qc", "Kh"), "broadway is the top straight");
        });

        T.Run("kickers decide otherwise-equal hands", () =>
        {
            T.True(S("Ah", "Ad", "Ks", "Qc", "9h") > S("Ah", "Ad", "Ks", "Qc", "8h"), "higher last kicker wins");
            T.True(S("Ah", "Ad", "Ks", "Qc", "Jh") > S("Kh", "Kd", "As", "Qc", "Jh"), "aces beat kings");
        });

        T.Run("best five of seven is chosen", () =>
        {
            // seven cards containing a flush among extras
            T.Eq(Cat("Ah", "Kh", "2h", "7h", "9h", "2d", "3c").ToString(), "Flush");
            // seven cards making a straight flush as the best 5
            T.Eq(Cat("5h", "6h", "7h", "8h", "9h", "Ad", "Ac").ToString(), "StraightFlush");
            // quads beats the trips-only reading of the same seven
            T.Eq(Cat("9h", "9d", "9s", "9c", "Ah", "Kd", "Qs").ToString(), "Quads");
        });
    }
}

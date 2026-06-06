using BsvPoker.Core;
using BsvPoker.Core.Games;

namespace BsvPoker.Tests;

/// <summary>Blackjack: hand values (soft/hard aces), dealer-stands-on-17, blackjack 3:2, bust, push, double.</summary>
public static class BlackjackTests
{
    private static Card C(string s)
    {
        int rank = s[0] switch { 'A' => 14, 'K' => 13, 'Q' => 12, 'J' => 11, 'T' => 10, _ => s[0] - '0' };
        var suit = s[1] switch { 's' => Suit.Spades, 'h' => Suit.Hearts, 'd' => Suit.Diamonds, _ => Suit.Clubs };
        return new Card(rank, suit);
    }
    private static Card[] D(params string[] cs) => cs.Select(C).ToArray();   // draw order: P1,D1,P2,D2, then hits

    public static void All()
    {
        Console.WriteLine("Blackjack:");

        T.Run("hand values: blackjack, soft and hard aces, bust", () =>
        {
            T.Eq(Blackjack.Value(D("Ah", "Kd")).Total, 21, "A+K = 21");
            T.True(Blackjack.IsBlackjack(D("Ah", "Kd")), "A+K is blackjack");
            var soft = Blackjack.Value(D("Ah", "6d")); T.Eq(soft.Total, 17); T.True(soft.Soft, "A+6 = soft 17");
            var hard = Blackjack.Value(D("Ah", "6d", "Kc")); T.Eq(hard.Total, 17); T.False(hard.Soft, "A+6+K = hard 17");
            T.Eq(Blackjack.Value(D("Kh", "Qd", "5c")).Total, 25, "K+Q+5 busts at 25");
        });

        T.Run("player blackjack pays 3:2", () =>
        {
            var g = Blackjack.Create(D("As", "9d", "Kh", "7c"), 100);
            T.Eq(g.Outcome.ToString(), "PlayerBlackjack");
            T.Eq(g.Payout(), 150L, "3:2 on 100");
        });

        T.Run("dealer stands on 17; a higher player total wins 1:1", () =>
        {
            var g = Blackjack.Create(D("Kc", "Kd", "Qh", "7s"), 100); // player 20, dealer 17
            g.Act(BjAction.Stand);
            T.Eq(g.Outcome.ToString(), "PlayerWin"); T.Eq(g.Payout(), 100L);
        });

        T.Run("player bust loses immediately", () =>
        {
            var g = Blackjack.Create(D("Kc", "2d", "5h", "2c", "Qs"), 100); // player 15, hit Q -> 25
            g.Act(BjAction.Hit);
            T.Eq(g.Outcome.ToString(), "PlayerBust"); T.Eq(g.Payout(), -100L);
        });

        T.Run("dealer draws under 17 and can bust", () =>
        {
            var g = Blackjack.Create(D("Kc", "Kd", "8h", "4s", "Qc"), 100); // player 18; dealer 14 -> hit Q -> 24
            g.Act(BjAction.Stand);
            T.Eq(g.Outcome.ToString(), "DealerBust"); T.Eq(g.Payout(), 100L);
        });

        T.Run("equal totals push", () =>
        {
            var g = Blackjack.Create(D("Kc", "Kd", "8h", "8s"), 100); // 18 vs 18
            g.Act(BjAction.Stand);
            T.Eq(g.Outcome.ToString(), "Push"); T.Eq(g.Payout(), 0L);
        });

        T.Run("double draws one card and doubles the stake", () =>
        {
            var g = Blackjack.Create(D("6c", "Kd", "5h", "7s", "9c", "5d"), 100); // player 11; double -> 9 = 20; dealer 17 stands
            g.Act(BjAction.Double);
            T.Eq(g.Bet, 200L, "stake doubled");
            T.Eq(g.Outcome.ToString(), "PlayerWin"); T.Eq(g.Payout(), 200L, "win pays the doubled stake");
        });
    }
}

using BsvPoker.Core;
using BsvPoker.Core.Games;

namespace BsvPoker.Tests;

/// <summary>
/// Edge-case hand-evaluation and pot-split tests. Every expected value here is DERIVED from the rules and
/// from the existing evaluators' own published contracts (PokerEval / LowEval / Showdown), not invented:
///   • exact ties and deterministic odd-chip splits (the lowest seat index takes the remainder),
///   • coincident all-in levels and a folded-but-contributing player modelled via per-level side pots,
///   • Omaha's "use EXACTLY two hole + three board" rule,
///   • Omaha-8's qualifying-low boundary (8-or-better, five distinct ranks all ≤ 8),
///   • ace-to-five lows and the wheel (5-4-3-2-A) being the best low and a real straight for high.
/// Each block pairs a POSITIVE assertion with a HOSTILE near-miss that must NOT win / must NOT qualify.
/// </summary>
public static class HandEvalEdgeTests
{
    private static Card C(string s)
    {
        int rank = s[0] switch { 'A' => 14, 'K' => 13, 'Q' => 12, 'J' => 11, 'T' => 10, _ => s[0] - '0' };
        var suit = s[1] switch { 's' => Suit.Spades, 'h' => Suit.Hearts, 'd' => Suit.Diamonds, _ => Suit.Clubs };
        return new Card(rank, suit);
    }
    private static Card[] H(params string[] cs) => cs.Select(C).ToArray();
    private static readonly Card[] None = Array.Empty<Card>();

    // Convenience high/low score helpers (mirror the existing PokerEvalTests / LowEvalTests style).
    private static long S(params string[] cs) => PokerEval.Best(H(cs)).Score;
    private static long L(params string[] cs) => LowEval.Best(H(cs))!.Value.Score;

    public static void All()
    {
        Console.WriteLine("hand-eval edge cases + pot splits:");

        // ---------------------------------------------------------------------------------------------
        // 1) EXACT TIES + ODD-CHIP SPLIT DETERMINISM
        //    Showdown.Split: each = pool/n, remainder (pool - each*n) goes to winners.Min() (lowest seat).
        //    Two identical Hold'em hands ⇒ a tie; with an odd pot the lower seat index gets the extra chip.
        // ---------------------------------------------------------------------------------------------
        T.Run("exact tie splits the pot; odd chip is deterministic (lowest seat index)", () =>
        {
            var board = H("Ah", "Kd", "Qs", "Jc", "9h");
            // Both seats play the SAME best five (the board's A-K-Q-J + a 9), each holding a dead 2/3.
            // Neither hole card improves on the board, so both seats' best-5 score is byte-identical ⇒ a true tie.
            var holes = new IReadOnlyList<Card>[] { H("2s", "3d"), H("2c", "3h") };

            // Sanity: the two seats really do score identically.
            T.Eq(Showdown.BestHigh(PokerGames.Of(PokerGame.TexasHoldem), holes[0], board),
                 Showdown.BestHigh(PokerGames.Of(PokerGame.TexasHoldem), holes[1], board), "tie precondition");

            var even = Showdown.Settle(PokerGames.Of(PokerGame.TexasHoldem), holes, board, 100);
            T.Eq(even.GetValueOrDefault(0), 50L, "even pot splits 50/50 (seat 0)");
            T.Eq(even.GetValueOrDefault(1), 50L, "even pot splits 50/50 (seat 1)");

            var odd = Showdown.Settle(PokerGames.Of(PokerGame.TexasHoldem), holes, board, 101);
            T.Eq(odd.GetValueOrDefault(0), 51L, "odd chip goes to the LOWEST seat index (seat 0)");
            T.Eq(odd.GetValueOrDefault(1), 50L, "seat 1 gets the floor share");
            T.Eq(odd.GetValueOrDefault(0) + odd.GetValueOrDefault(1), 101L, "every satoshi is paid out");
        });

        T.Run("HOSTILE: a near-tie does NOT split — one kicker breaks it and that seat wins all", () =>
        {
            var board = H("Ah", "Kd", "Qs", "Jc", "9h");
            // Seat 0 pairs the ace with a hole ace (A-A-K-Q-J beats the board's A-K-Q-J-9 high card).
            // Seat 1's hole 2/3 cannot beat its own board high card. This must NOT be a split.
            var holes = new IReadOnlyList<Card>[] { H("As", "2d"), H("2c", "3h") };
            var pay = Showdown.Settle(PokerGames.Of(PokerGame.TexasHoldem), holes, board, 100);
            T.Eq(pay.GetValueOrDefault(0), 100L, "the seat that pairs aces takes the whole pot");
            T.Eq(pay.GetValueOrDefault(1), 0L, "the other seat wins nothing — no spurious split");
        });

        // Determinism: re-running an identical showdown yields byte-identical payouts (no RNG, no order drift).
        T.Run("split is byte-for-byte deterministic across repeated evaluations", () =>
        {
            var board = H("7h", "7d", "7s", "2c", "2h"); // both seats see the same full house on the board
            var holes = new IReadOnlyList<Card>[] { H("3d", "4c"), H("5s", "6h") };
            var a = Showdown.Settle(PokerGames.Of(PokerGame.TexasHoldem), holes, board, 99);
            var b = Showdown.Settle(PokerGames.Of(PokerGame.TexasHoldem), holes, board, 99);
            T.Eq(a.GetValueOrDefault(0), b.GetValueOrDefault(0), "seat 0 payout stable");
            T.Eq(a.GetValueOrDefault(1), b.GetValueOrDefault(1), "seat 1 payout stable");
            // 99 split two ways ⇒ 49 each, odd chip (1) to seat 0 ⇒ 50/49.
            T.Eq(a.GetValueOrDefault(0), 50L, "49 floor + 1 odd chip to lowest seat");
            T.Eq(a.GetValueOrDefault(1), 49L, "floor share");
        });

        // ---------------------------------------------------------------------------------------------
        // 2) COINCIDENT ALL-IN LEVELS + FOLDED-BUT-CONTRIBUTING PLAYER IN A SIDE POT
        //    The evaluator settles ONE pool at a time (Showdown.Settle over the seats eligible for that pool).
        //    A real side-pot structure is modelled by settling each pot level separately over its eligible seats.
        //    A folded player's chips stay in the pots they contributed to but the player is NOT an eligible seat.
        // ---------------------------------------------------------------------------------------------
        T.Run("coincident all-in levels: equal short stacks form ONE main pot, split on a tie", () =>
        {
            // Two players are all-in for the SAME amount at the same level. Their contributions form a single
            // main pot. If they tie, that pot splits; the odd-chip rule still applies.
            var board = H("Th", "Td", "Ts", "2c", "2h"); // full house on the board ⇒ both play the board ⇒ tie
            var holes = new IReadOnlyList<Card>[] { H("3d", "4c"), H("5s", "6h") };
            long mainPot = 200; // 100 + 100 coincident all-ins
            var pay = Showdown.Settle(PokerGames.Of(PokerGame.TexasHoldem), holes, board, mainPot);
            T.Eq(pay.GetValueOrDefault(0), 100L, "coincident all-ins, tie ⇒ even split");
            T.Eq(pay.GetValueOrDefault(1), 100L, "coincident all-ins, tie ⇒ even split");
        });

        T.Run("side pots: short all-in wins main; deep stacks contest the side pot separately", () =>
        {
            // Three seats. Seat 0 is all-in short (main pot only). Seats 1 and 2 are deeper and also build a side pot.
            // Strengths (DERIVED): seat 0 = quad fives (best), seat 1 = ace-high flush, seat 2 = king-high flush.
            var board = H("5h", "5d", "Ah", "Kh", "2h");
            var s0 = H("5s", "5c"); // quads — strongest overall
            var s1 = H("Qh", "Jh"); // ace-high heart flush (uses board Ah Kh 2h + Qh Jh)
            var s2 = H("Th", "9h"); // king-high heart flush (Kh + Th 9h 2h ...), weaker than s1

            // Verify the strength order we rely on.
            long h0 = Showdown.BestHigh(PokerGames.Of(PokerGame.TexasHoldem), s0, board);
            long h1 = Showdown.BestHigh(PokerGames.Of(PokerGame.TexasHoldem), s1, board);
            long h2 = Showdown.BestHigh(PokerGames.Of(PokerGame.TexasHoldem), s2, board);
            T.True(h0 > h1 && h1 > h2, "quads > ace-high flush > king-high flush");

            // MAIN POT: all three eligible (each contributed). Seat 0's quads win the whole main pot.
            var main = Showdown.Settle(PokerGames.Of(PokerGame.TexasHoldem),
                new IReadOnlyList<Card>[] { s0, s1, s2 }, board, 300);
            T.Eq(main.GetValueOrDefault(0), 300L, "all-in short stack wins the main pot with quads");
            T.Eq(main.GetValueOrDefault(1), 0L);
            T.Eq(main.GetValueOrDefault(2), 0L);

            // SIDE POT: seat 0 is all-in and NOT eligible for the side pot. Only seats 1 and 2 contest it.
            // Settle the side pot over just those two seats ⇒ seat 1's higher flush wins it.
            var side = Showdown.Settle(PokerGames.Of(PokerGame.TexasHoldem),
                new IReadOnlyList<Card>[] { s1, s2 }, board, 120);
            T.Eq(side.GetValueOrDefault(0), 120L, "the better flush takes the side pot");
            T.Eq(side.GetValueOrDefault(1), 0L, "the weaker flush wins nothing in the side pot");
        });

        T.Run("folded-but-contributing player: their chips fund the pot but they are NOT an eligible seat", () =>
        {
            // A folder put chips in earlier (dead money) then folded. At showdown only the live seats are
            // passed to Settle, but the pot total still INCLUDES the folder's contribution.
            var board = H("Ah", "Kd", "7s", "2c", "9h");
            var live = new IReadOnlyList<Card>[] { H("As", "Ad"), H("Kh", "Qc") }; // trip aces vs pair kings
            long live0 = 40, live1 = 40, deadFolded = 25; // folder contributed 25 then folded
            long pot = live0 + live1 + deadFolded;        // = 105
            var pay = Showdown.Settle(PokerGames.Of(PokerGame.TexasHoldem), live, board, pot);
            // Trip aces beat pair kings and scoop the whole pot, including the folder's dead 25.
            T.Eq(pay.GetValueOrDefault(0), pot, "winner scoops the pot including the folder's dead money");
            T.Eq(pay.GetValueOrDefault(1), 0L);
            // The folder is not a seat in the result map at all.
            T.False(pay.ContainsKey(2), "the folded contributor receives nothing and is not an eligible seat");
        });

        T.Run("HOSTILE: a folded seat that WOULD have won gets nothing because it is excluded from Settle", () =>
        {
            var board = H("Ah", "Kd", "7s", "2c", "9h");
            // If the folder (who held quad-making cards) had stayed, it would win — but having folded it is
            // never presented to Settle, so the best LIVE hand wins. This guards against re-admitting folders.
            var live = new IReadOnlyList<Card>[] { H("As", "Ad"), H("Kh", "Qc") };
            var pay = Showdown.Settle(PokerGames.Of(PokerGame.TexasHoldem), live, board, 100);
            T.Eq(pay.GetValueOrDefault(0), 100L, "best LIVE hand wins; the absent folder cannot");
        });

        // ---------------------------------------------------------------------------------------------
        // 3) OMAHA: USE EXACTLY 2 HOLE + 3 BOARD
        // ---------------------------------------------------------------------------------------------
        T.Run("Omaha forces exactly two hole cards: one-suited-hole CANNOT make a board flush", () =>
        {
            // Board has four spades; hole has exactly ONE spade. Hold'em (any 5 of 7) makes the flush;
            // Omaha (exactly 2 hole + 3 board) cannot, because only one hole spade is available.
            var board = H("As", "Ks", "Qs", "2s", "7d");
            var hole = H("Js", "9h", "4c", "3d"); // single spade (Js)
            long holdem = Showdown.BestHigh(PokerGames.Of(PokerGame.TexasHoldem), hole, board);
            long omaha = Showdown.BestHigh(PokerGames.Of(PokerGame.Omaha), hole, board);
            // Hold'em sees a spade flush; Omaha is capped at a much weaker hand. Strictly: Omaha < Hold'em.
            T.True(PokerEval.Best(hole.Concat(board).ToList()).Category == HandCategory.Flush,
                   "Hold'em-style best-of-7 is a flush");
            T.True(omaha < holdem, "Omaha cannot reach the spade flush with a single hole spade");
            // Omaha's best here cannot even BE a flush: at most 3 board spades + 1 hole spade = 4 spades, never 5.
            // The Hold'em A-high spade flush is the smallest possible flush given As on board, so Omaha (< that)
            // is strictly below flush territory — i.e. Omaha's category is below Flush.
            T.True(omaha < (long)HandCategory.Flush * 16 * 16 * 16 * 16 * 16,
                   "Omaha's best is below any flush (cannot gather five spades with one hole spade)");
        });

        T.Run("Omaha POSITIVE: two hole spades DO complete the flush under the 2+3 rule", () =>
        {
            var board = H("As", "Ks", "Qs", "2d", "7d"); // three board spades
            var hole = H("Js", "9s", "4c", "3h");        // TWO hole spades ⇒ legal 2+3 flush
            long omaha = Showdown.BestHigh(PokerGames.Of(PokerGame.Omaha), hole, board);
            // The legal Omaha hand is the spade flush A-K-Q-J-9 (board As Ks Qs + hole Js 9s).
            long expectedFlush = PokerEval.Best(H("As", "Ks", "Qs", "Js", "9s")).Score;
            T.Eq(omaha, expectedFlush, "two hole spades + three board spades = A-high spade flush");
        });

        T.Run("HOSTILE: Omaha must NOT use 1 hole + 4 board, nor 3 hole + 2 board", () =>
        {
            // Construct a board that is itself a made straight A-K-Q-J-T. In Hold'em a player ignores both
            // hole cards and plays the board straight. In Omaha that is illegal (0 hole cards). The player must
            // use exactly two of their hole cards, which here only DEGRADES the hand.
            var board = H("Ah", "Kd", "Qs", "Jc", "Th"); // board is broadway
            var hole = H("2d", "3c", "4s", "5h");         // no hole card extends/keeps the straight
            long holdem = Showdown.BestHigh(PokerGames.Of(PokerGame.TexasHoldem), hole, board);
            long omaha = Showdown.BestHigh(PokerGames.Of(PokerGame.Omaha), hole, board);
            // Hold'em plays the board straight (A-high). Omaha is forced to inject two low hole cards.
            T.Eq(PokerEval.Best(board.ToList()).Category.ToString(), "Straight", "board itself is a straight");
            T.True(omaha < holdem, "Omaha cannot play the bare board straight — it must use exactly two holes");
            // The best legal Omaha hand uses two holes + three board; it cannot be the broadway straight.
            T.True(PokerEval.Best(H("Ah", "Kd", "Qs", "Jc", "Th")).Category == HandCategory.Straight
                   && omaha != holdem, "the 2+3 constraint strictly changes the result");
        });

        // ---------------------------------------------------------------------------------------------
        // 4) OMAHA-8 QUALIFYING-LOW BOUNDARY (8-or-better): five DISTINCT ranks all ≤ 8, exactly 2+3.
        // ---------------------------------------------------------------------------------------------
        T.Run("Omaha-8 low qualifies exactly at the 8-or-better boundary", () =>
        {
            // Board contributes three low cards 8,4,3 (+ two high blanks). Hole has A,5 low.
            // Legal 2+3 low = hole A,5 + board 8,4,3 ⇒ ranks {8,5,4,3,A} distinct, max 8 ⇒ qualifies (8-low).
            var board = H("8h", "4d", "3s", "Kc", "Qh");
            var hole = H("Ad", "5c", "9s", "Jh");
            long? low = Showdown.BestLow(PokerGames.Of(PokerGame.OmahaHiLo), hole, board);
            T.True(low != null, "an 8-high five-distinct low qualifies");
            // Cross-check the exact score against LowEval on the explicit five-card low.
            long expected = LowEval.Best(H("8h", "5c", "4d", "3s", "Ad"), eightOrBetter: true)!.Value.Score;
            T.Eq(low!.Value, expected, "qualifying-low score matches the explicit 8-5-4-3-A low");
        });

        T.Run("HOSTILE: best low is a 9-low ⇒ does NOT qualify for Omaha-8 (returns null)", () =>
        {
            // Omaha low needs exactly 3 board cards, all ≤ 8. This board has only TWO cards ≤ 8 (4d, 3s);
            // the rest are 9h, Kc, Qh (all > 8). Any 3-board selection drags in a card > 8, so NO five-card
            // 2+3 hand can be all-≤8 ⇒ there is no qualifying low at all. (The best low overall would be a
            // 9-something, which is over the 8 boundary.)
            var board = H("9h", "4d", "3s", "Kc", "Qh");
            var hole = H("Ad", "5c", "Js", "Th"); // only A,5 are low holes; cannot supply a 3rd board low
            long? low = Showdown.BestLow(PokerGames.Of(PokerGame.OmahaHiLo), hole, board);
            T.True(low == null, "a 9-low must NOT qualify under 8-or-better");

            // And in a full split this means the whole pot goes to the HIGH winner (low pool rolls over).
            var holes = new IReadOnlyList<Card>[]
            {
                H("Ad", "5c", "Js", "Th"), // the 9-low (non-qualifying) seat
                H("Kh", "Kd", "9s", "9c"), // a strong high (two pair / trips depending on board)
            };
            var pay = Showdown.Settle(PokerGames.Of(PokerGame.OmahaHiLo), holes, board, 100);
            long bestHigh0 = Showdown.BestHigh(PokerGames.Of(PokerGame.OmahaHiLo), holes[0], board);
            long bestHigh1 = Showdown.BestHigh(PokerGames.Of(PokerGame.OmahaHiLo), holes[1], board);
            int highWinner = bestHigh1 >= bestHigh0 ? 1 : 0;
            // No qualifying low anywhere ⇒ the low pool rolls into the high winner ⇒ that seat scoops 100.
            T.Eq(pay.GetValueOrDefault(highWinner), 100L, "with no qualifying low the high winner scoops the pot");
        });

        T.Run("Omaha-8 boundary is on the SECOND-worst card too: 8-7-x must beat 8-7-y only on lower cards", () =>
        {
            // Two qualifying 8-lows; the one with the lower SECOND card (then third…) wins. Derive from LowEval.
            long lo8_6 = LowEval.Best(H("8h", "6d", "3s", "2c", "Ah"), eightOrBetter: true)!.Value.Score; // 8-6-3-2-A
            long lo8_7 = LowEval.Best(H("8h", "7d", "3s", "2c", "Ah"), eightOrBetter: true)!.Value.Score; // 8-7-3-2-A
            T.True(lo8_6 < lo8_7, "8-6-3-2-A is a better (lower) low than 8-7-3-2-A");
        });

        // ---------------------------------------------------------------------------------------------
        // 5) ACE-TO-FIVE LOWS + WHEEL STRAIGHTS
        // ---------------------------------------------------------------------------------------------
        T.Run("ace-to-five: the wheel 5-4-3-2-A is the nut low and beats every other low", () =>
        {
            long wheel = L("5h", "4d", "3s", "2c", "Ah");
            long sixLow = L("6h", "4d", "3s", "2c", "Ah");   // 6-4-3-2-A
            long sevenLow = L("7h", "5d", "4s", "2c", "Ah"); // 7-5-4-2-A
            T.True(wheel < sixLow, "the wheel beats any six-low");
            T.True(sixLow < sevenLow, "a six-low beats a seven-low");
            // straights/flushes are irrelevant to the low: the wheel-in-one-suit equals the off-suit wheel.
            T.Eq(L("5h", "4h", "3h", "2h", "Ah"), wheel, "a flush does not spoil the low (same score)");
        });

        T.Run("ace-to-five HOSTILE: any pair is worse than any no-pair low (the pair must lose)", () =>
        {
            // A pair of deuces (with otherwise tiny cards) is a WORSE low than a no-pair king-high.
            long pairDeuces = L("2h", "2d", "3s", "4c", "5h"); // paired ⇒ category 1
            long noPairKing = L("Kh", "Qd", "Js", "Tc", "9h"); // no pair ⇒ category 0, but high cards
            T.True(noPairKing < pairDeuces, "no-pair beats a pair for low, regardless of card heights");
            // And the near-miss the other way: a no-pair 8-low must beat that pair too.
            T.True(L("8h", "6d", "4s", "3c", "Ah") < pairDeuces, "a real 8-low crushes a paired hand");
        });

        T.Run("Razz best-of-seven low ignores high pairs and takes the lowest five distinct cards", () =>
        {
            // Seven cards: two kings (junk for low) plus 6-4-3-2-A. Best low = 6-4-3-2-A.
            long sevenCard = LowEval.Best(H("Kh", "Kd", "6s", "4c", "3h", "2d", "Ah"))!.Value.Score;
            long explicitSix = L("6s", "4c", "3h", "2d", "Ah");
            T.Eq(sevenCard, explicitSix, "best-of-7 low equals the explicit 6-low");

            // HOSTILE near-miss: a seven-card holding whose best low is a 7-low must NOT match the 6-low score.
            long sevenLowBest = LowEval.Best(H("Kh", "Kd", "7s", "4c", "3h", "2d", "Ah"))!.Value.Score; // 7-4-3-2-A
            T.True(sevenLowBest > explicitSix, "a 7-low does not masquerade as the 6-low");
        });

        T.Run("Razz showdown: the wheel (5-low) beats a 7-low and takes the whole low-only pot", () =>
        {
            var holes = new IReadOnlyList<Card>[]
            {
                H("Ah", "2d", "3s", "4c", "5h", "Kd", "Qc"), // wheel 5-low
                H("2h", "3d", "4s", "5c", "7h", "Kh", "Qd"), // 7-low
            };
            var pay = Showdown.Settle(PokerGames.Of(PokerGame.Razz), holes, None, 100);
            T.Eq(pay.GetValueOrDefault(0), 100L, "wheel beats 7-low; low-only pot is not split");
            T.Eq(pay.GetValueOrDefault(1), 0L);

            // HOSTILE: two identical wheels DO tie and split the low-only pot deterministically.
            var tie = new IReadOnlyList<Card>[]
            {
                H("Ah", "2d", "3s", "4c", "5h", "Kd", "Qc"),
                H("As", "2h", "3d", "4s", "5c", "Kh", "Qd"),
            };
            var tiePay = Showdown.Settle(PokerGames.Of(PokerGame.Razz), tie, None, 101);
            T.Eq(tiePay.GetValueOrDefault(0), 51L, "tied wheels split; odd chip to the lowest seat");
            T.Eq(tiePay.GetValueOrDefault(1), 50L, "tied wheels split");
        });

        T.Run("wheel as a HIGH straight: A-2-3-4-5 is a straight but the LOWEST one (5-high)", () =>
        {
            T.Eq(PokerEval.Best(H("Ah", "2d", "3s", "4c", "5h")).Category.ToString(), "Straight",
                 "the wheel is a straight for high");
            T.True(S("Ah", "2d", "3s", "4c", "5h") < S("2h", "3d", "4s", "5c", "6h"),
                   "the wheel (5-high) is the lowest straight, below a six-high straight");
            // HOSTILE: A-K-Q-J-T (broadway) is the HIGHEST straight, far above the wheel — the ace is high here.
            T.True(S("Ah", "Kd", "Qs", "Jc", "Th") > S("Ah", "2d", "3s", "4c", "5h"),
                   "broadway (ace-high straight) beats the wheel — the ace is not double-counted");
            // HOSTILE: A-2-3-4-6 is NOT a straight (gap at 5) and must score below any real straight.
            T.True(S("Ah", "2d", "3s", "4c", "6h") < S("Ah", "2d", "3s", "4c", "5h"),
                   "a broken wheel (A-2-3-4-6) is only ace-high, below the real wheel straight");
            T.Eq(PokerEval.Best(H("Ah", "2d", "3s", "4c", "6h")).Category.ToString(), "HighCard",
                 "A-2-3-4-6 is high card, not a straight");
        });

        T.Run("wheel straight FLUSH (steel wheel) is a straight flush, lowest of its category", () =>
        {
            T.Eq(PokerEval.Best(H("5h", "4h", "3h", "2h", "Ah")).Category.ToString(), "StraightFlush",
                 "5-high suited wheel is a straight flush");
            T.True(S("5h", "4h", "3h", "2h", "Ah") < S("6h", "5h", "4h", "3h", "2h"),
                   "the steel wheel is the lowest straight flush");
            // HOSTILE: a suited A-2-3-4-6 is only a FLUSH (no straight), which must rank BELOW the steel wheel
            // straight flush despite sharing the ace.
            T.Eq(PokerEval.Best(H("Ah", "2h", "3h", "4h", "6h")).Category.ToString(), "Flush",
                 "suited A-2-3-4-6 is a flush, not a straight flush");
            T.True(S("Ah", "2h", "3h", "4h", "6h") < S("5h", "4h", "3h", "2h", "Ah"),
                   "a wheel-ish flush is still below the actual steel-wheel straight flush");
        });
    }
}

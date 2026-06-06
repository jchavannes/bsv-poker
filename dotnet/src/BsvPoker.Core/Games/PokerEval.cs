namespace BsvPoker.Core.Games;

/// <summary>The nine poker hand categories, low to high.</summary>
public enum HandCategory { HighCard, Pair, TwoPair, Trips, Straight, Flush, FullHouse, Quads, StraightFlush }

/// <summary>
/// A fresh, exact poker hand evaluator. Given 5–7 cards it finds the BEST five-card hand and returns a
/// single comparable score (higher is better), plus the category. Handles the wheel (A-2-3-4-5) straight,
/// full kicker ordering, and best-of-seven selection. Pure and total; the basis for every high-poker game.
/// </summary>
public static class PokerEval
{
    public readonly record struct Result(HandCategory Category, long Score);

    /// <summary>Best high hand from 5..7 cards.</summary>
    public static Result Best(IReadOnlyList<Card> cards)
    {
        if (cards.Count < 5) throw new ArgumentException("need at least 5 cards");
        long best = -1; HandCategory bestCat = HandCategory.HighCard;
        foreach (var combo in Combinations(cards.Count, 5))
        {
            var five = new Card[5];
            for (int k = 0; k < 5; k++) five[k] = cards[combo[k]];
            var (cat, sc) = Score5(five);
            if (sc > best) { best = sc; bestCat = cat; }
        }
        return new Result(bestCat, best);
    }

    private static (HandCategory, long) Score5(Card[] five)
    {
        // rank counts (ranks 2..14)
        var count = new int[15];
        var suits = new int[4];
        foreach (var c in five) { count[c.Rank]++; suits[(int)c.Suit]++; }
        bool flush = false; for (int s = 0; s < 4; s++) if (suits[s] == 5) flush = true;

        // straight high (0 if none); wheel A-5 → high card 5
        int straightHigh = 0;
        {
            // build a presence set incl. ace-low
            var present = new bool[15]; foreach (var c in five) present[c.Rank] = true;
            bool aceLow = present[14] && present[2] && present[3] && present[4] && present[5];
            for (int hi = 14; hi >= 6; hi--)
                if (present[hi] && present[hi - 1] && present[hi - 2] && present[hi - 3] && present[hi - 4]) { straightHigh = hi; break; }
            if (straightHigh == 0 && aceLow) straightHigh = 5;
        }

        // ranks ordered by (count desc, rank desc) for kicker tiebreaks
        var byCount = Enumerable.Range(2, 13).Where(r => count[r] > 0)
            .OrderByDescending(r => count[r]).ThenByDescending(r => r).ToArray();
        int[] groups = byCount.Select(r => count[r]).ToArray();

        HandCategory cat;
        int[] tiebreak;
        if (straightHigh > 0 && flush) { cat = HandCategory.StraightFlush; tiebreak = new[] { straightHigh }; }
        else if (groups[0] == 4) { cat = HandCategory.Quads; tiebreak = new[] { byCount[0], byCount[1] }; }
        else if (groups[0] == 3 && groups.Length > 1 && groups[1] == 2) { cat = HandCategory.FullHouse; tiebreak = new[] { byCount[0], byCount[1] }; }
        else if (flush) { cat = HandCategory.Flush; tiebreak = five.Select(c => c.Rank).OrderByDescending(x => x).ToArray(); }
        else if (straightHigh > 0) { cat = HandCategory.Straight; tiebreak = new[] { straightHigh }; }
        else if (groups[0] == 3) { cat = HandCategory.Trips; tiebreak = byCount.ToArray(); }
        else if (groups[0] == 2 && groups.Length > 1 && groups[1] == 2) { cat = HandCategory.TwoPair; tiebreak = byCount.ToArray(); }
        else if (groups[0] == 2) { cat = HandCategory.Pair; tiebreak = byCount.ToArray(); }
        else { cat = HandCategory.HighCard; tiebreak = five.Select(c => c.Rank).OrderByDescending(x => x).ToArray(); }

        long score = (long)cat;
        foreach (var t in tiebreak) score = score * 16 + t;       // pack category then kickers (ranks < 16)
        for (int pad = tiebreak.Length; pad < 5; pad++) score *= 16; // normalize width so scores compare correctly
        return (cat, score);
    }

    // index combinations of size k from n
    private static IEnumerable<int[]> Combinations(int n, int k)
    {
        var idx = Enumerable.Range(0, k).ToArray();
        while (true)
        {
            yield return (int[])idx.Clone();
            int i = k - 1;
            while (i >= 0 && idx[i] == n - k + i) i--;
            if (i < 0) yield break;
            idx[i]++;
            for (int j = i + 1; j < k; j++) idx[j] = idx[j - 1] + 1;
        }
    }
}

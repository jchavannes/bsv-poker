namespace BsvPoker.Core;

/// <summary>Best-5-of-7 Texas Hold'em hand evaluator → a comparable score + category name.</summary>
public static class HandEval
{
    public readonly record struct Result(long Score, string Category);

    private static readonly string[] CatName =
        { "High card", "Pair", "Two pair", "Three of a kind", "Straight", "Flush", "Full house", "Four of a kind", "Straight flush" };

    /// <summary>
    /// Best hand for a variant. Standard variants use the best 5 of (hole+board); Omaha-style variants
    /// must use EXACTLY two hole cards + three board cards.
    /// </summary>
    public static Result BestForVariant(IReadOnlyList<Card> hole, IReadOnlyList<Card> board, bool exactlyTwoHole)
    {
        if (!exactlyTwoHole) return Best(hole.Concat(board).ToList());
        long best = -1; int bestCat = 0;
        for (int a = 0; a < hole.Count; a++)
        for (int b = a + 1; b < hole.Count; b++)
        for (int c = 0; c < board.Count; c++)
        for (int d = c + 1; d < board.Count; d++)
        for (int e = d + 1; e < board.Count; e++)
        {
            var (score, cat) = Score5(hole[a], hole[b], board[c], board[d], board[e]);
            if (score > best) { best = score; bestCat = cat; }
        }
        return new Result(best, CatName[bestCat]);
    }

    public static Result Best(IReadOnlyList<Card> cards)
    {
        long best = -1; int bestCat = 0;
        int n = cards.Count;
        for (int a = 0; a < n; a++)
        for (int b = a + 1; b < n; b++)
        for (int c = b + 1; c < n; c++)
        for (int d = c + 1; d < n; d++)
        for (int e = d + 1; e < n; e++)
        {
            var (score, cat) = Score5(cards[a], cards[b], cards[c], cards[d], cards[e]);
            if (score > best) { best = score; bestCat = cat; }
        }
        return new Result(best, CatName[bestCat]);
    }

    private static (long, int) Score5(Card c1, Card c2, Card c3, Card c4, Card c5)
    {
        var ranks = new List<int> { c1.Rank, c2.Rank, c3.Rank, c4.Rank, c5.Rank };
        ranks.Sort((x, y) => y - x);
        bool flush = c1.Suit == c2.Suit && c2.Suit == c3.Suit && c3.Suit == c4.Suit && c4.Suit == c5.Suit;

        var distinct = ranks.Distinct().OrderByDescending(x => x).ToList();
        int straightHigh = 0;
        if (distinct.Count == 5)
        {
            if (distinct[0] - distinct[4] == 4) straightHigh = distinct[0];
            else if (distinct[0] == 14 && distinct[1] == 5 && distinct[4] == 2) straightHigh = 5; // wheel A-2-3-4-5
        }

        var groups = ranks.GroupBy(r => r).Select(g => (Rank: g.Key, Count: g.Count()))
                          .OrderByDescending(g => g.Count).ThenByDescending(g => g.Rank).ToList();

        int category;
        var tb = new List<int>();
        if (straightHigh > 0 && flush) { category = 8; tb.Add(straightHigh); }
        else if (groups[0].Count == 4) { category = 7; tb.Add(groups[0].Rank); tb.Add(groups[1].Rank); }
        else if (groups[0].Count == 3 && groups[1].Count == 2) { category = 6; tb.Add(groups[0].Rank); tb.Add(groups[1].Rank); }
        else if (flush) { category = 5; tb.AddRange(ranks); }
        else if (straightHigh > 0) { category = 4; tb.Add(straightHigh); }
        else if (groups[0].Count == 3) { category = 3; tb.Add(groups[0].Rank); tb.AddRange(groups.Skip(1).Select(g => g.Rank)); }
        else if (groups[0].Count == 2 && groups[1].Count == 2) { category = 2; tb.Add(Math.Max(groups[0].Rank, groups[1].Rank)); tb.Add(Math.Min(groups[0].Rank, groups[1].Rank)); tb.Add(groups[2].Rank); }
        else if (groups[0].Count == 2) { category = 1; tb.Add(groups[0].Rank); tb.AddRange(groups.Skip(1).Select(g => g.Rank)); }
        else { category = 0; tb.AddRange(ranks); }

        long score = category;
        for (int i = 0; i < 5; i++) score = score * 15 + (i < tb.Count ? tb[i] : 0);
        return (score, category);
    }
}

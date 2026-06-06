namespace BsvPoker.Core;

/// <summary>The six poker variants the lobby offers. All are community-card (board) games on the
/// dealerless mental-poker deck + the shared betting engine, distinguished by hole-card count, the
/// deck, and whether the hand must use EXACTLY two hole cards (Omaha-style).</summary>
public enum Variant { TexasHoldem, Omaha, BigO, Pineapple, Tahoe, RoyalHoldem }

public static class Variants
{
    public static readonly Variant[] All = { Variant.TexasHoldem, Variant.Omaha, Variant.BigO, Variant.Pineapple, Variant.Tahoe, Variant.RoyalHoldem };

    public static string Name(Variant v) => v switch
    {
        Variant.TexasHoldem => "Texas Hold'em",
        Variant.Omaha => "Omaha (use exactly 2)",
        Variant.BigO => "Big O — 5-card Omaha",
        Variant.Pineapple => "Pineapple (3 hole)",
        Variant.Tahoe => "Tahoe (3 hole, exactly 2)",
        Variant.RoyalHoldem => "Royal Hold'em (T–A deck)",
        _ => v.ToString(),
    };

    public static Variant Parse(string s) => Enum.TryParse<Variant>(s, out var v) ? v : Variant.TexasHoldem;

    public static int HoleCards(Variant v) => v switch
    {
        Variant.Omaha => 4,
        Variant.BigO => 5,
        Variant.Pineapple or Variant.Tahoe => 3,
        _ => 2,
    };

    /// <summary>Omaha-style: the hand MUST use exactly two hole cards + three board cards.</summary>
    public static bool ExactlyTwoHole(Variant v) => v is Variant.Omaha or Variant.BigO or Variant.Tahoe;

    /// <summary>The card set this variant is dealt from (Royal Hold'em uses only T..A = 20 cards).</summary>
    public static IReadOnlyList<Card> CardSet(Variant v)
    {
        int minRank = v == Variant.RoyalHoldem ? 10 : 2;
        var set = new List<Card>();
        foreach (Suit s in Enum.GetValues<Suit>())
            for (int r = minRank; r <= 14; r++) set.Add(new Card(r, s));
        return set;
    }
}

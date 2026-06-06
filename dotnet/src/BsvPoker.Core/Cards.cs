using System.Security.Cryptography;

namespace BsvPoker.Core;

public enum Suit { Spades, Hearts, Diamonds, Clubs }

/// <summary>A playing card. Rank 2..14 (11=J,12=Q,13=K,14=A).</summary>
public readonly record struct Card(int Rank, Suit Suit)
{
    public string RankLabel => Rank switch { 14 => "A", 13 => "K", 12 => "Q", 11 => "J", 10 => "10", _ => Rank.ToString() };
    public char Glyph => Suit switch { Suit.Spades => '♠', Suit.Hearts => '♥', Suit.Diamonds => '♦', _ => '♣' };
    public bool IsRed => Suit is Suit.Hearts or Suit.Diamonds;
    public int Index => (Rank - 2) * 4 + (int)Suit; // 0..51
    public static Card FromIndex(int i) => new(i / 4 + 2, (Suit)(i % 4));
    public override string ToString() => $"{RankLabel}{Glyph}";
}

/// <summary>The ordered 52-card deck (index 0..51) and a local CSPRNG shuffle (mental-poker shuffle is in MentalPoker).</summary>
public static class Deck
{
    public static IReadOnlyList<Card> Ordered { get; } = Enumerable.Range(0, 52).Select(Card.FromIndex).ToArray();

    public static List<Card> Shuffled()
    {
        var d = Ordered.ToList();
        for (int i = d.Count - 1; i > 0; i--)
        {
            int j = (int)RandomNumberGenerator.GetInt32(i + 1);
            (d[i], d[j]) = (d[j], d[i]);
        }
        return d;
    }
}

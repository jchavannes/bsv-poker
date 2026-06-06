namespace BsvPoker.Core.Games;

public enum BjAction { Hit, Stand, Double }
public enum BjOutcome { InPlay, PlayerBust, DealerBust, PlayerWin, DealerWin, Push, PlayerBlackjack }

/// <summary>
/// A correct single-seat Blackjack round (peer-dealt, real-BSV wagered on-chain). Card values: 2–10 face
/// value, J/Q/K = 10, Ace = 11 or 1 (soft/hard). Dealer stands on all 17. Blackjack (21 on the first two
/// cards) pays 3:2. Double draws exactly one card and doubles the stake. The deck is the dealerless deal.
/// </summary>
public sealed class Blackjack
{
    private readonly List<Card> _deck;
    private int _pos;
    public List<Card> Player { get; } = new();
    public List<Card> Dealer { get; } = new();
    public long Bet { get; private set; }
    public bool Doubled { get; private set; }
    public BjOutcome Outcome { get; private set; } = BjOutcome.InPlay;
    public bool PlayerDone { get; private set; }

    private Blackjack(IReadOnlyList<Card> deck, long bet) { _deck = deck.ToList(); Bet = bet; }

    public static int CardValue(Card c) => c.Rank >= 10 ? 10 : c.Rank == 14 ? 11 : c.Rank; // ace handled in Value()

    /// <summary>Best total ≤ 21 if possible; soft = an ace is still counted as 11.</summary>
    public static (int Total, bool Soft) Value(IReadOnlyList<Card> cards)
    {
        int total = 0, aces = 0;
        foreach (var c in cards) { if (c.Rank == 14) { aces++; total += 11; } else total += CardValue(c); }
        bool soft = aces > 0;
        while (total > 21 && aces > 0) { total -= 10; aces--; soft = aces > 0; }
        return (total, soft && total <= 21);
    }

    public static bool IsBlackjack(IReadOnlyList<Card> cards) => cards.Count == 2 && Value(cards).Total == 21;

    public static Blackjack Create(IReadOnlyList<Card> deck, long bet)
    {
        if (bet <= 0) throw new ArgumentException("bet must be positive");
        var g = new Blackjack(deck, bet);
        g.Player.Add(g.Draw()); g.Dealer.Add(g.Draw());
        g.Player.Add(g.Draw()); g.Dealer.Add(g.Draw());
        if (IsBlackjack(g.Player) || IsBlackjack(g.Dealer)) { g.PlayerDone = true; g.SettleAtShowdown(); }
        return g;
    }

    private Card Draw() { if (_pos >= _deck.Count) throw new InvalidOperationException("deck exhausted"); return _deck[_pos++]; }

    public void Act(BjAction a)
    {
        if (Outcome != BjOutcome.InPlay || PlayerDone) return;
        switch (a)
        {
            case BjAction.Hit:
                Player.Add(Draw());
                if (Value(Player).Total > 21) { Outcome = BjOutcome.PlayerBust; PlayerDone = true; }
                break;
            case BjAction.Double:
                Bet *= 2; Doubled = true; Player.Add(Draw()); PlayerDone = true;
                if (Value(Player).Total > 21) Outcome = BjOutcome.PlayerBust; else DealerPlayAndSettle();
                break;
            case BjAction.Stand:
                PlayerDone = true; DealerPlayAndSettle();
                break;
        }
    }

    private void DealerPlayAndSettle()
    {
        while (Value(Dealer).Total < 17) Dealer.Add(Draw()); // dealer stands on all 17
        SettleAtShowdown();
    }

    private void SettleAtShowdown()
    {
        int p = Value(Player).Total, d = Value(Dealer).Total;
        bool pbj = IsBlackjack(Player), dbj = IsBlackjack(Dealer);
        if (pbj && dbj) Outcome = BjOutcome.Push;
        else if (pbj) Outcome = BjOutcome.PlayerBlackjack;
        else if (dbj) Outcome = BjOutcome.DealerWin;
        else if (p > 21) Outcome = BjOutcome.PlayerBust;
        else if (d > 21) Outcome = BjOutcome.DealerBust;
        else if (p > d) Outcome = BjOutcome.PlayerWin;
        else if (d > p) Outcome = BjOutcome.DealerWin;
        else Outcome = BjOutcome.Push;
    }

    /// <summary>Net payout to the player (negative = loss). Blackjack pays 3:2; win pays 1:1; push 0.</summary>
    public long Payout() => Outcome switch
    {
        BjOutcome.PlayerBlackjack => Bet * 3 / 2,
        BjOutcome.PlayerWin or BjOutcome.DealerBust => Bet,
        BjOutcome.Push => 0,
        BjOutcome.DealerWin or BjOutcome.PlayerBust => -Bet,
        _ => 0,
    };
}

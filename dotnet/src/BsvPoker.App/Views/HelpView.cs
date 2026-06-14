using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BsvPoker.Core;

namespace BsvPoker.App.Views;

/// <summary>
/// A plain-language HELP / HOW-TO-PLAY screen. No prior poker knowledge is assumed: it explains what the
/// buttons do, how a hand is won, what the ranking of hands is, and the rules of every variant the lobby
/// offers — so a complete beginner can sit down and play and always understand whether they won or lost.
/// </summary>
public sealed class HelpView : UserControl
{
    private static readonly Brush Ink = Brushes.White;
    private static readonly Brush Sub = new SolidColorBrush(Color.FromRgb(0xCF, 0xD8, 0xDC));
    private static readonly Brush Gold = new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0x82));

    public HelpView()
    {
        Background = new SolidColorBrush(Color.FromRgb(0x0D, 0x0D, 0x0D));
        var sp = new StackPanel { Margin = new Thickness(26) };

        sp.Children.Add(H1("How to play"));
        sp.Children.Add(P("This is real poker played peer-to-peer on Bitcoin SV. You never need to know the rules in " +
            "advance — this page explains everything. When a hand finishes, a big banner tells you in plain words " +
            "whether you WON or LOST and how many chips changed. A countdown clock shows the time left in each hand."));

        sp.Children.Add(H2("The buttons"));
        sp.Children.Add(Bullet("Fold — give up this hand. You lose only what you already put in."));
        sp.Children.Add(Bullet("Check — pass the action when there is nothing to call (it costs nothing)."));
        sp.Children.Add(Bullet("Call — match the current bet to stay in the hand."));
        sp.Children.Add(Bullet("Bet / Raise — put in more chips (type the amount in the box). This pressures opponents."));
        sp.Children.Add(Bullet("Leave table — walk away at any time. Your funds are always yours and safe."));

        sp.Children.Add(H2("How a hand is won"));
        sp.Children.Add(P("You get private cards (your \"hole\" cards — only you see them). Shared community cards are " +
            "turned over in the middle (the \"board\"). You make the BEST five-card hand from your cards plus the " +
            "board. After the betting, the player with the best hand wins the pot (all the chips bet that hand). " +
            "If everyone else folds, you win without showing your cards."));

        sp.Children.Add(H2("Hand rankings — strongest first"));
        sp.Children.Add(Bullet("Straight flush — five in a row, all the same suit (e.g. 5 6 7 8 9 of hearts)."));
        sp.Children.Add(Bullet("Four of a kind — four cards of the same rank."));
        sp.Children.Add(Bullet("Full house — three of one rank + two of another."));
        sp.Children.Add(Bullet("Flush — five cards of the same suit (any order)."));
        sp.Children.Add(Bullet("Straight — five in a row, mixed suits."));
        sp.Children.Add(Bullet("Three of a kind — three cards of the same rank."));
        sp.Children.Add(Bullet("Two pair — two pairs."));
        sp.Children.Add(Bullet("Pair — two cards of the same rank."));
        sp.Children.Add(Bullet("High card — none of the above; the highest card plays."));

        sp.Children.Add(H2("The variants in the Lobby"));
        sp.Children.Add(VariantLine(Variant.TexasHoldem, "2 private cards. Use any of your cards + the board to make the best five."));
        sp.Children.Add(VariantLine(Variant.Omaha, "4 private cards, but you MUST use EXACTLY two of them + three board cards."));
        sp.Children.Add(VariantLine(Variant.BigO, "Like Omaha but 5 private cards (still use exactly two)."));
        sp.Children.Add(VariantLine(Variant.Pineapple, "3 private cards; use any to make the best five."));
        sp.Children.Add(VariantLine(Variant.Tahoe, "3 private cards; use EXACTLY two + three board cards."));
        sp.Children.Add(VariantLine(Variant.RoyalHoldem, "Hold'em played with a short deck of only Tens to Aces — big hands are common."));

        sp.Children.Add(H2("Playing your bot"));
        sp.Children.Add(P("In the Lobby press \"Play a bot\" to play a hand against an automatic opponent right on the " +
            "table — you will see its seat and the cards are dealt immediately. \"Play MY bot\" additionally opens " +
            "your own funded bot that can also settle the hand on-chain in the background; the table hand is always " +
            "playable straight away whether or not the on-chain part is funded."));

        sp.Children.Add(H2("Everything is on-chain — and replayable"));
        sp.Children.Add(P("Every move (each card, each bet, the pot, the payout) is a real Bitcoin transaction. " +
            "Open the REPLAY tab to load a finished game and step through it move by move — the whole point of " +
            "putting it on-chain is that any hand can be replayed and verified by anyone, forever."));

        sp.Children.Add(H2("Winning and losing"));
        sp.Children.Add(P("You do NOT need to track this yourself. At the end of every hand the screen shows a clear, " +
            "coloured banner: GREEN \"YOU WIN +N chips\" (with the name of your winning hand), or RED \"You lost this " +
            "hand −N chips\". The running chip standings are shown under the table. A session ends when one player " +
            "holds all the chips."));

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = sp };
        Content = scroll;
    }

    private static TextBlock H1(string t) => new() { Text = t, Foreground = Brushes.White, FontSize = 26, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8) };
    private static TextBlock H2(string t) => new() { Text = t, Foreground = Gold, FontSize = 18, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 16, 0, 6) };
    private static TextBlock P(string t) => new() { Text = t, Foreground = Sub, FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4), MaxWidth = 820, HorizontalAlignment = HorizontalAlignment.Left };
    private static TextBlock Bullet(string t) => new() { Text = "•  " + t, Foreground = Ink, FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10, 2, 0, 2), MaxWidth = 820, HorizontalAlignment = HorizontalAlignment.Left };
    private static TextBlock VariantLine(Variant v, string desc)
        => new() { Foreground = Ink, FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10, 2, 0, 2), MaxWidth = 820, HorizontalAlignment = HorizontalAlignment.Left,
                   Inlines = { new System.Windows.Documents.Run(Variants.Name(v) + " — ") { FontWeight = FontWeights.Bold }, new System.Windows.Documents.Run(desc) } };
}

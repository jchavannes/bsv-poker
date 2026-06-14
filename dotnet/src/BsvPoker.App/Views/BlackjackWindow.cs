using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BsvPoker.App.Controls;
using BsvPoker.App.Views;
using BsvPoker.Core;

namespace BsvPoker.App;

/// <summary>
/// A VISIBLE Blackjack table: shows the dealer's hand on top, YOUR hand below, both as real card faces, with a
/// big coloured result banner (YOU WIN / dealer wins / push) and the bet. Opened the instant a Blackjack hand
/// is dealt — so clicking "Blackjack on-chain" always SHOWS a game, never "money vanished with no screen". The
/// hand still settles fully on-chain in the background; this is the screen the player watches.
/// </summary>
public sealed class BlackjackWindow : Window
{
    private readonly StackPanel _dealerCards = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly StackPanel _playerCards = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _dealerInfo = new() { Foreground = Brushes.White, FontSize = 15, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _playerInfo = new() { Foreground = Brushes.White, FontSize = 15, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Border _resultBox = new() { CornerRadius = new CornerRadius(8), Padding = new Thickness(20, 10, 20, 10), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 8) };
    private readonly TextBlock _result = new() { FontSize = 24, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center };
    private readonly TextBlock _note = new() { Foreground = new SolidColorBrush(Color.FromRgb(0xCF, 0xD8, 0xDC)), FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 460, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };

    public BlackjackWindow(Window owner)
    {
        Owner = owner; Title = "On-chain Blackjack";
        Width = 540; Height = 520; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x4A, 0x28)); ShowActivated = false;

        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(new TextBlock { Text = "Blackjack", FontSize = 22, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 10) });
        root.Children.Add(new TextBlock { Text = "Dealer", Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0x82)), HorizontalAlignment = HorizontalAlignment.Center, FontWeight = FontWeights.Bold });
        root.Children.Add(_dealerCards);
        root.Children.Add(_dealerInfo);
        _resultBox.Child = _result;
        root.Children.Add(_resultBox);
        root.Children.Add(new TextBlock { Text = "You", Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0x82)), HorizontalAlignment = HorizontalAlignment.Center, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 8, 0, 0) });
        root.Children.Add(_playerCards);
        root.Children.Add(_playerInfo);
        root.Children.Add(_note);
        Content = root;
    }

    /// <summary>Show a dealt hand. Call again to update (e.g. as the on-chain broadcast confirms).</summary>
    public void ShowHand(WalletView.BlackjackResult r, string note)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => ShowHand(r, note))); return; }
        _dealerCards.Children.Clear(); _playerCards.Children.Clear();
        foreach (var c in r.Dealer) { var cv = new CardView(); cv.ShowCard(c); _dealerCards.Children.Add(cv); }
        foreach (var c in r.Player) { var cv = new CardView(); cv.ShowCard(c); _playerCards.Children.Add(cv); }
        _dealerInfo.Text = $"Dealer total: {r.DealerTotal}";
        _playerInfo.Text = $"Your total: {r.PlayerTotal}";
        _result.Text = r.YouWin ? $"YOU WIN!  +{r.Bet} sat"
                     : r.Verdict.StartsWith("PUSH") ? "PUSH — stakes returned"
                     : $"Dealer wins  −{r.Bet} sat";
        _resultBox.Background = new SolidColorBrush(r.YouWin ? Color.FromRgb(0x2E, 0x7D, 0x32)
                                                  : r.Verdict.StartsWith("PUSH") ? Color.FromRgb(0x4A, 0x4A, 0x4A)
                                                  : Color.FromRgb(0xB0, 0x2A, 0x2A));
        _note.Text = note;
    }
}

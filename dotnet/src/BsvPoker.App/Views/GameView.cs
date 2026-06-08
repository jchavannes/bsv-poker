using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BsvPoker.Core;

namespace BsvPoker.App.Views;

/// <summary>
/// The poker table. There is NO local/bot deck and NO play money: a fair deal requires a real opponent's
/// entropy, so a hand is only ever a genuine two-party on-chain mental-poker deal against a discovered peer
/// (every message a Bitcoin transaction). With no funds or no opponent there is nothing here.
/// </summary>
public sealed class GameView : UserControl
{
    private readonly Func<string>? _playHand;
    private readonly TextBlock _msg = new() { Foreground = Brushes.White, FontSize = 16, TextWrapping = TextWrapping.Wrap, MaxWidth = 720, TextAlignment = TextAlignment.Center };
    private readonly WrapPanel _board = new() { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 4) };
    private readonly WrapPanel _holes = new() { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
    private readonly TextBlock _oppLine = new() { Foreground = Brushes.White, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };

    public event Action? OnLeaveTable;

    public GameView(byte[] priv, byte[] pub, CardVault vault, Action onCardsChanged, Func<string>? playHand = null)
    {
        _playHand = playHand;
        Background = new SolidColorBrush(Color.FromRgb(0x0D, 0x0D, 0x0D)); Foreground = Brushes.White;

        var felt = new Border { Margin = new Thickness(16), CornerRadius = new CornerRadius(160) };
        felt.Background = new RadialGradientBrush(Color.FromRgb(0x1F, 0x7A, 0x43), Color.FromRgb(0x0B, 0x4A, 0x28));
        var inner = new Border { CornerRadius = new CornerRadius(150), BorderBrush = new SolidColorBrush(Color.FromRgb(0xA9, 0x81, 0x2B)), BorderThickness = new Thickness(6), Margin = new Thickness(10) };
        var col = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(24) };
        col.Children.Add(new TextBlock { Text = "On-chain table — real BSV, real opponent only", FontWeight = FontWeights.Bold, FontSize = 18, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 10) });
        col.Children.Add(_oppLine);
        col.Children.Add(_board);
        col.Children.Add(_holes);
        col.Children.Add(_msg);
        inner.Child = col; felt.Child = inner;

        var play = Mk("Play on-chain hand", "#3A6E2E"); play.Click += (_, _) => ShowMessage(_playHand?.Invoke() ?? "On-chain play is unavailable.");
        var leave = Mk("Leave table", "#555555"); leave.Click += (_, _) => { OnLeaveTable?.Invoke(); ShowMessage("You left the table. Your funds are yours."); };
        var bar = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(10), Children = { play, leave } };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(felt, 0); root.Children.Add(felt);
        var barHost = new Border { Background = new SolidColorBrush(Color.FromRgb(0x16, 0x16, 0x16)), Padding = new Thickness(8) };
        barHost.Child = bar; Grid.SetRow(barHost, 1); root.Children.Add(barHost);
        Content = root;
        ShowMessage("Press “Play on-chain hand”. A fair deal requires a real opponent (discovered automatically) and real BSV funds — there is no local or bot deck.");
    }

    /// <summary>Lobby entry point.</summary>
    public void StartBot(Variant variant) => ShowMessage(_playHand?.Invoke() ?? "On-chain play is unavailable.");

    /// <summary>Update the table message from any thread (e.g. when a live deal completes).</summary>
    public void ShowStatus(string s)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => ShowStatus(s))); return; }
        ShowMessage(s);
    }

    private void ShowMessage(string s) => _msg.Text = s;

    /// <summary>Render a completed hand as real card tiles (your hole cards are now on-chain NFTs) + opponent + result.</summary>
    public void ShowHand(IReadOnlyList<Card> holes, IReadOnlyList<Card> board, string opponent, string result)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => ShowHand(holes, board, opponent, result))); return; }
        _oppLine.Text = $"Opponent: {opponent}";
        _board.Children.Clear(); foreach (var c in board) _board.Children.Add(Tile(c));
        _holes.Children.Clear(); foreach (var c in holes) _holes.Children.Add(Tile(c));
        _msg.Text = result;
    }

    private static UIElement Tile(Card card)
    {
        var fg = card.IsRed ? Brushes.Crimson : Brushes.Black;
        var face = new Border { Width = 54, Height = 74, Margin = new Thickness(4), CornerRadius = new CornerRadius(6), Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20)), BorderThickness = new Thickness(1) };
        var g = new Grid();
        g.Children.Add(new TextBlock { Text = card.RankLabel, FontSize = 13, FontWeight = FontWeights.Bold, Foreground = fg, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(4, 2, 0, 0) });
        g.Children.Add(new TextBlock { Text = card.Glyph.ToString(), FontSize = 24, Foreground = fg, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        face.Child = g; return face;
    }

    private static Button Mk(string text, string hex)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        return new Button { Content = text, Width = 150, Margin = new Thickness(4), Padding = new Thickness(0, 8, 0, 8), Foreground = Brushes.White, BorderThickness = new Thickness(0), Background = new SolidColorBrush(c) };
    }
}

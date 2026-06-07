using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BsvPoker.App.Controls;
using BsvPoker.Core;

namespace BsvPoker.App.Views;

/// <summary>
/// The poker table. Play is ONLY a real on-chain hand: pressing "Play on-chain hand" funds a real 2-of-2
/// pot escrow from your wallet and emits the whole hand as Bitcoin transactions (table/hand genesis, escrow,
/// shuffle, deal, board, bets, showdown, settlement) which are pushed to the network and stored on-chain.
/// There is NO play-money mode, NO off-chain mesh game, and NO free card minting — it refuses to start
/// without real sats and a live connection.
/// </summary>
public sealed class GameView : UserControl
{
    private readonly CardVault _vault;
    private readonly Action _onCardsChanged;
    private readonly Func<IReadOnlyList<Card>, long, string>? _onChainSettle;
    private readonly Func<long, bool>? _canFund;
    private const long OnChainStake = 20_000; // real-sat stake for one on-chain hand

    private readonly StackPanel _board = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 8) };
    private readonly TextBlock _title = new() { Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _msg = new() { Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xF5, 0xE9)), FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(20, 8, 20, 8) };
    private readonly WrapPanel _cards = new() { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 8) };
    private readonly TextBlock _cardsLabel = new() { Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Button _play = Mk("Play on-chain hand", "#3A6E2E");
    private readonly Button _leave = Mk("Leave table", "#555555");

    public event Action? OnLeaveTable;

    public GameView(byte[] priv, byte[] pub, CardVault vault, Action onCardsChanged,
        Func<IReadOnlyList<Card>, long, string>? onChainSettle = null, Func<long, bool>? canFund = null)
    {
        _vault = vault; _onCardsChanged = onCardsChanged; _onChainSettle = onChainSettle; _canFund = canFund;
        Background = new SolidColorBrush(Color.FromRgb(0x0D, 0x0D, 0x0D)); Foreground = Brushes.White;

        var felt = new Border { Margin = new Thickness(16), CornerRadius = new CornerRadius(160) };
        felt.Background = new RadialGradientBrush(Color.FromRgb(0x1F, 0x7A, 0x43), Color.FromRgb(0x0B, 0x4A, 0x28));
        var inner = new Border { CornerRadius = new CornerRadius(150), BorderBrush = new SolidColorBrush(Color.FromRgb(0xA9, 0x81, 0x2B)), BorderThickness = new Thickness(6), Margin = new Thickness(10) };
        var col = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(24) };
        col.Children.Add(_title);
        col.Children.Add(_board);
        col.Children.Add(_msg);
        col.Children.Add(_cardsLabel);
        col.Children.Add(_cards);
        inner.Child = col; felt.Child = inner;

        var bar = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(10) };
        _play.Click += (_, _) => PlayOnChain();
        _leave.Click += (_, _) => LeaveTable();
        bar.Children.Add(_play); bar.Children.Add(_leave);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(felt, 0); root.Children.Add(felt);
        var barHost = new Border { Background = new SolidColorBrush(Color.FromRgb(0x16, 0x16, 0x16)), Padding = new Thickness(8) };
        barHost.Child = bar; Grid.SetRow(barHost, 1); root.Children.Add(barHost);
        Content = root;
        Render(null);
    }

    /// <summary>Lobby entry point: start a hand — which is ONLY ever a real on-chain hand.</summary>
    public void StartBot(Variant variant) => PlayOnChain();

    private void PlayOnChain()
    {
        if (_onChainSettle == null) { Render("On-chain play is unavailable (wallet/node not wired)."); return; }
        if (_canFund != null && !_canFund(OnChainStake))
        {
            Render($"Real BSV required. Fund your wallet with at least {OnChainStake:N0} sat and connect to the network. " +
                   "Poker is ONLY played as on-chain Bitcoin transactions — there is no play-money mode.");
            return;
        }
        var deck = ShuffledDeck(9);
        var result = _onChainSettle(deck, OnChainStake);   // funds escrow + emits the whole on-chain hand tape
        // show the dealt board (these cards are recorded on-chain by the hand's BoardReveal transactions)
        Render(result, deck.Skip(4).Take(5).ToList());
        _onCardsChanged();
    }

    private void LeaveTable()
    {
        OnLeaveTable?.Invoke();
        Render("You left the table. Your funds are yours — every stake is in a real escrow with a pre-signed recovery.");
    }

    private static IReadOnlyList<Card> ShuffledDeck(int n)
    {
        var a = Enumerable.Range(0, 52).ToArray();
        for (int i = 51; i > 0; i--) { int j = RandomNumberGenerator.GetInt32(i + 1); (a[i], a[j]) = (a[j], a[i]); }
        return a.Take(n).Select(Card.FromIndex).ToList();
    }

    private static Button Mk(string text, string hex)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        return new Button { Content = text, Width = 150, Margin = new Thickness(4), Padding = new Thickness(0, 8, 0, 8), Foreground = Brushes.White, BorderThickness = new Thickness(0), Background = new SolidColorBrush(c) };
    }

    private void Render(string? message, IReadOnlyList<Card>? board = null)
    {
        _title.Text = "On-chain table — real BSV only";
        _board.Children.Clear();
        for (int i = 0; i < 5; i++) { var cv = new CardView(); if (board != null && i < board.Count) cv.ShowCard(board[i]); else cv.ShowEmpty(); _board.Children.Add(cv); }
        _msg.Text = message ?? "Press “Play on-chain hand” to fund a real pot escrow and play a hand entirely as Bitcoin transactions.";
        _cards.Children.Clear();
        var owned = _vault.Owned();
        _cardsLabel.Text = $"Card NFTs you hold (1-sat on-chain outputs): {owned.Count}";
        foreach (var (card, _) in owned) { var cv = new CardView(); cv.ShowCard(card); _cards.Children.Add(cv); }
    }
}

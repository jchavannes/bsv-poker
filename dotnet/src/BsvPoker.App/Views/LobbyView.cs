using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BsvPoker.App.Views;

/// <summary>
/// Lobby tab. There is no off-chain table gossip — that would be a non-transaction side channel. A table is
/// created on-chain (a TableGenesis transaction) and a hand is played as Bitcoin transactions. For now this
/// starts a real on-chain hand; full on-chain table discovery (reading table-genesis txs) is the next build.
/// </summary>
public sealed class LobbyView : UserControl
{
    public LobbyView(Action onPlay)
    {
        Background = new SolidColorBrush(Color.FromRgb(0x0D, 0x0D, 0x0D)); Foreground = Brushes.White;
        var root = new StackPanel { Margin = new Thickness(24) };
        root.Children.Add(new TextBlock { Text = "Lobby — on-chain only", FontSize = 22, FontWeight = FontWeights.Bold, Foreground = Brushes.White });
        root.Children.Add(new TextBlock
        {
            Text = "Every table, hand, and message is a Bitcoin transaction. There is no off-chain lobby gossip. " +
                   "Fund your wallet with real BSV (Wallet tab), then start an on-chain hand.",
            Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 14), MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left
        });
        var play = new Button { Content = "Play an on-chain hand", Padding = new Thickness(14, 10, 14, 10), Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x6E, 0x2E)), Foreground = Brushes.White, BorderThickness = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left };
        play.Click += (_, _) => onPlay();
        root.Children.Add(play);
        Content = new ScrollViewer { Content = root };
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BsvPoker.App.Views;

/// <summary>
/// Chat where every message is a Bitcoin transaction. You enter the recipient's public key and their IP
/// endpoint; pressing Send builds an encrypted ChatDirect transaction (ECDH+AES) funded from your wallet,
/// pushes it IP-to-IP straight to the recipient, AND broadcasts the same transaction to the mining nodes so
/// it is stored on-chain. Incoming messages are transactions your peer pushed to you, decrypted here. There
/// is no off-chain channel — the only bytes on the wire are Bitcoin transactions.
/// </summary>
public sealed class ChatView : UserControl
{
    private readonly Func<string, string, string, string> _send; // (recipientPubHex, peerHostPort, text) -> status
    private readonly ListBox _log = new() { Background = new SolidColorBrush(Color.FromRgb(0x0F, 0x0F, 0x0F)), Foreground = Brushes.White, BorderThickness = new Thickness(0), Height = 300 };
    private readonly TextBox _recipient = new() { Width = 540, FontFamily = new FontFamily("Consolas") };
    private readonly TextBox _peer = new() { Width = 200, Text = "127.0.0.1:0" };
    private readonly TextBox _text = new() { Width = 420 };
    private readonly TextBlock _status = new() { Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBlock _endpoint = new() { Foreground = Brushes.Gray, Margin = new Thickness(0, 6, 0, 2) };

    /// <summary>Update the displayed listening address once the TxLink has bound a port.</summary>
    public void SetListenEndpoint(string endpoint)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => SetListenEndpoint(endpoint))); return; }
        _endpoint.Text = $"Your address (peers push their message-transactions here): {endpoint}";
    }

    public ChatView(byte[] myPub, string myListenEndpoint, Func<string, string, string, string> send)
    {
        _send = send;
        Background = new SolidColorBrush(Color.FromRgb(0x0D, 0x0D, 0x0D)); Foreground = Brushes.White;
        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(new TextBlock { Text = "Chat — every message is a Bitcoin transaction (encrypted, on-chain)", FontSize = 20, FontWeight = FontWeights.Bold, Foreground = Brushes.White });
        root.Children.Add(new TextBlock { Text = "Your public key (give this to peers so they can message you):", Foreground = Brushes.Gray, Margin = new Thickness(0, 10, 0, 2) });
        root.Children.Add(new TextBox { Text = Convert.ToHexString(myPub).ToLowerInvariant(), IsReadOnly = true, Width = 540, FontFamily = new FontFamily("Consolas"), Foreground = Brushes.LightGreen, HorizontalAlignment = HorizontalAlignment.Left });
        _endpoint.Text = $"Your address (peers push their message-transactions here): {myListenEndpoint}";
        root.Children.Add(_endpoint);

        root.Children.Add(new TextBlock { Text = "Messages received (transactions peers pushed to you):", Foreground = Brushes.Gray, Margin = new Thickness(0, 12, 0, 2) });
        root.Children.Add(_log);

        root.Children.Add(new TextBlock { Text = "Recipient public key (hex):", Foreground = Brushes.Gray, Margin = new Thickness(0, 12, 0, 2) });
        root.Children.Add(_recipient);
        var line = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        line.Children.Add(new TextBlock { Text = "Recipient IP:port ", Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center });
        line.Children.Add(_peer);
        line.Children.Add(new TextBlock { Text = "  Message ", Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center });
        line.Children.Add(_text);
        var sendBtn = new Button { Content = "Send (as a Bitcoin tx)", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 6, 10, 6) };
        sendBtn.Click += (_, _) => { _status.Text = _send(_recipient.Text.Trim(), _peer.Text.Trim(), _text.Text); _text.Clear(); };
        line.Children.Add(sendBtn);
        root.Children.Add(line);
        root.Children.Add(_status);

        Content = new ScrollViewer { Content = root };
    }

    /// <summary>Display a chat message that arrived as a transaction (pushed by the peer, or seen on-chain).</summary>
    public void AddIncoming(string senderPubHex, string text)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => AddIncoming(senderPubHex, text))); return; }
        var who = senderPubHex.Length >= 12 ? senderPubHex[..12] + "…" : senderPubHex;
        _log.Items.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {who}:  {text}");
    }
}

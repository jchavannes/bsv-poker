using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using BsvPoker.Net;

namespace BsvPoker.App.Views;

/// <summary>
/// Chat tab — encrypted messaging over the P2P mesh (no server): DIRECT messages and GROUP chats. Every
/// message is encrypted with a fresh ephemeral ECDH key per recipient (no key reuse). Online peers come
/// from mesh presence; start a DM with a peer, or a group with several. Conversations + history on the left.
/// </summary>
public sealed class ChatView : UserControl
{
    private readonly ChatService _chat;
    private readonly ListBox _convList = new() { Background = new SolidColorBrush(Color.FromRgb(0x12, 0x12, 0x12)), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
    private readonly ListBox _peerList = new() { Background = new SolidColorBrush(Color.FromRgb(0x12, 0x12, 0x12)), Foreground = Brushes.White, BorderThickness = new Thickness(0), Height = 130 };
    private readonly ListBox _messages = new() { Background = new SolidColorBrush(Color.FromRgb(0x0F, 0x0F, 0x0F)), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
    private readonly TextBox _input = new() { VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBlock _title = new() { Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 16 };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private ChatService.Conversation? _open;

    public ChatView(ChatService chat)
    {
        _chat = chat;
        Background = new SolidColorBrush(Color.FromRgb(0x0D, 0x0D, 0x0D)); Foreground = Brushes.White;
        var grid = new Grid { Margin = new Thickness(10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        // left: conversations + online peers + new-group
        var left = new DockPanel { Margin = new Thickness(0, 0, 10, 0) };
        var leftTop = new StackPanel();
        leftTop.Children.Add(new TextBlock { Text = "Your chat ID — share it so others can message you:", Foreground = Brushes.Gray });
        leftTop.Children.Add(new TextBox { Text = _chat.MyHex, IsReadOnly = true, FontFamily = new FontFamily("Consolas"), FontSize = 11, TextWrapping = TextWrapping.Wrap, Background = new SolidColorBrush(Color.FromRgb(0x0C, 0x0C, 0x0C)), Foreground = Brushes.LightGreen, BorderThickness = new Thickness(0) });
        leftTop.Children.Add(new TextBlock { Text = "Start a DM — paste a peer's chat ID:", Foreground = Brushes.Gray, Margin = new Thickness(0, 8, 0, 2) });
        var dmId = new TextBox { FontFamily = new FontFamily("Consolas"), FontSize = 11 };
        leftTop.Children.Add(dmId);
        var dmBtn = new Button { Content = "Start DM with this ID", Margin = new Thickness(0, 4, 0, 8), Padding = new Thickness(8, 6, 8, 6) };
        dmBtn.Click += (_, _) =>
        {
            var id = dmId.Text.Trim().ToLowerInvariant();
            if (id.Length == 66 && id != _chat.MyHex) { dmId.Clear(); OpenDm(id); }
            else _title.Text = "Enter a valid 66-character chat ID (not your own).";
        };
        leftTop.Children.Add(dmBtn);
        leftTop.Children.Add(new TextBlock { Text = "Conversations", Foreground = Brushes.Gray });
        DockPanel.SetDock(leftTop, Dock.Top); left.Children.Add(leftTop);
        var leftBottom = new StackPanel();
        leftBottom.Children.Add(new TextBlock { Text = "Online peers (click to DM)", Foreground = Brushes.Gray, Margin = new Thickness(0, 8, 0, 2) });
        leftBottom.Children.Add(_peerList);
        var groupBtn = new Button { Content = "New group with selected peers", Margin = new Thickness(0, 6, 0, 0), Padding = new Thickness(8, 6, 8, 6) };
        _peerList.SelectionMode = SelectionMode.Multiple;
        groupBtn.Click += (_, _) => NewGroup();
        leftBottom.Children.Add(groupBtn);
        DockPanel.SetDock(leftBottom, Dock.Bottom); left.Children.Add(leftBottom);
        _convList.SelectionChanged += (_, _) => OpenSelected();
        _peerList.MouseDoubleClick += (_, _) => { if (_peerList.SelectedItem is string p) OpenDm(p); };
        left.Children.Add(_convList);
        Grid.SetColumn(left, 0); grid.Children.Add(left);

        // right: messages + input
        var right = new Grid();
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition());
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(_title, 0); right.Children.Add(_title);
        Grid.SetRow(_messages, 1); right.Children.Add(_messages);
        var bar = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        bar.ColumnDefinitions.Add(new ColumnDefinition());
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_input, 0); bar.Children.Add(_input);
        var send = new Button { Content = "Send", Width = 90, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(0, 6, 0, 6) };
        send.Click += (_, _) => Send();
        _input.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) Send(); };
        Grid.SetColumn(send, 1); bar.Children.Add(send);
        Grid.SetRow(bar, 2); right.Children.Add(bar);
        Grid.SetColumn(right, 1); grid.Children.Add(right);

        Content = grid;
        _title.Text = "Select a peer or conversation";
        _chat.OnUpdate += c => Dispatcher.BeginInvoke(new Action(() => { RefreshConvs(); if (_open != null && c.Id == _open.Id) RefreshMessages(); }));
        _timer.Tick += (_, _) => RefreshPeers();
        _timer.Start();
        RefreshPeers(); RefreshConvs();
    }

    private void RefreshPeers()
    {
        var sel = _peerList.SelectedItems.Cast<string>().ToHashSet();
        _peerList.Items.Clear();
        foreach (var p in _chat.OnlinePeers()) { _peerList.Items.Add(p); }
    }

    private void RefreshConvs()
    {
        var openId = _open?.Id;
        _convList.Items.Clear();
        foreach (var c in _chat.Conversations) _convList.Items.Add(new ConvItem(c));
        if (openId != null) foreach (ConvItem it in _convList.Items) if (it.Conv.Id == openId) { _convList.SelectedItem = it; break; }
    }

    private sealed record ConvItem(ChatService.Conversation Conv) { public override string ToString() => (Conv.IsGroup ? "👥 " : "💬 ") + Conv.Title; }

    private void OpenDm(string peerPubHex)
    {
        var conv = _chat.OpenDm(peerPubHex, "DM " + peerPubHex[..Math.Min(10, peerPubHex.Length)] + "…");
        _open = conv; RefreshConvs(); SelectConv(conv.Id); RefreshMessages();
    }

    private void NewGroup()
    {
        var members = _peerList.SelectedItems.Cast<string>().ToList();
        if (members.Count == 0) { _title.Text = "Select one or more online peers first."; return; }
        var conv = _chat.CreateGroup("Group (" + (members.Count + 1) + ")", members);
        _open = conv; RefreshConvs(); SelectConv(conv.Id); RefreshMessages();
    }

    private void SelectConv(string id) { foreach (ConvItem it in _convList.Items) if (it.Conv.Id == id) { _convList.SelectedItem = it; break; } }

    private void OpenSelected()
    {
        if (_convList.SelectedItem is ConvItem it) { _open = it.Conv; _title.Text = (it.Conv.IsGroup ? "👥 " : "💬 ") + it.Conv.Title; RefreshMessages(); }
    }

    private void RefreshMessages()
    {
        _messages.Items.Clear();
        if (_open == null) return;
        foreach (var m in _open.Messages)
        {
            var who = m.FromHex == _chat.MyHex ? "you" : m.FromHex[..Math.Min(8, m.FromHex.Length)];
            _messages.Items.Add($"{m.TimeUtc.Substring(11)}  {who}: {m.Text}");
        }
        if (_messages.Items.Count > 0) _messages.ScrollIntoView(_messages.Items[^1]);
    }

    private void Send()
    {
        if (_open == null || _input.Text.Trim().Length == 0) return;
        _chat.Send(_open.Id, _input.Text.Trim());
        _input.Clear();
        RefreshMessages();
    }
}

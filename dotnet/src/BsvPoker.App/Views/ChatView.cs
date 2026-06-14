using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace BsvPoker.App.Views;

/// <summary>
/// Telegram/WhatsApp-grade chat where every message is a Bitcoin transaction. Recipients come from your
/// CONTACTS (the wallet address book — identities and bots you saved) UNIONED with peers discovered on the
/// poker gossip overlay, so you pick a person, not a raw key. Three ways to send:
///   • ONE   — encrypted directly to the selected identity (ECDH),
///   • ALL   — the key-graph BROADCAST ENCRYPTION (GB 2623780 B) sealed to EVERY recipient you currently know
///             (all peers) + yourself; only those recipients can read it. NEVER a plaintext send-to-all.
///   • GROUP — the user's key-graph BROADCAST ENCRYPTION (GB 2623780 B): encrypted ONCE to a selected group,
///             only its members can read, with revocation by re-creating the group.
/// Every form is funded as a real BSV tx, pushed IP-to-IP to anyone we have an endpoint for, AND broadcast to
/// miners — so an offline member receives it on-chain (store-and-forward) when they next sync.
/// </summary>
public sealed class ChatView : UserControl
{
    private readonly Func<IReadOnlyList<(string PubHex, string Endpoint)>> _peers;
    private readonly Func<string, string, string, bool, string> _send;                 // (recipientPubHex, endpoint, text, broadcast) -> status
    private Func<IReadOnlyList<(string Label, string PubHex)>>? _contacts;              // wallet address book
    private Func<IReadOnlyList<(string Name, IReadOnlyList<string> Members)>>? _groups; // saved chat groups
    private Action<string, IReadOnlyList<string>>? _saveGroup;                          // (name, members) persist
    private Action<string>? _deleteGroup;
    private Func<IReadOnlyList<string>, string, string>? _sendGroup;                    // (members, text) -> status
    private Func<string, string?>? _handleFor;
    private Action<string, string>? _saveContact;
    private Func<(string Name, string PubHex)?>? _addBot;   // spin up one of MY bots as a chat identity (no game needed)
    private Func<IReadOnlyList<(string PubHex, string Endpoint, string Handle)>>? _online;   // live "who's online" directory

    private readonly ListBox _peerList = new() { Background = new SolidColorBrush(Color.FromRgb(0x12, 0x12, 0x12)), Foreground = Brushes.White, BorderThickness = new Thickness(0), Height = 150 };
    private readonly ListBox _log = new() { Background = new SolidColorBrush(Color.FromRgb(0x0F, 0x0F, 0x0F)), Foreground = Brushes.White, BorderThickness = new Thickness(0), Height = 220 };
    private readonly TextBox _text = new() { Width = 460 };
    private readonly TextBlock _status = new() { Foreground = Brushes.Gainsboro, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private readonly ComboBox _mode = new() { Width = 320, VerticalAlignment = VerticalAlignment.Center };
    private readonly ComboBox _groupPick = new() { Width = 220, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _groupHint = new() { Foreground = Brushes.Gainsboro, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };

    // The unified recipient list shown in _peerList: a contact or a discovered peer (deduped by pubkey).
    private List<(string Label, string PubHex, string Endpoint)> _current = new();

    public void SetHandleResolver(Func<string, string?> handleFor) => _handleFor = handleFor;
    public void SetSaveContact(Action<string, string> save) => _saveContact = save;
    public void SetContacts(Func<IReadOnlyList<(string Label, string PubHex)>> contacts) => _contacts = contacts;
    public void SetGroups(Func<IReadOnlyList<(string Name, IReadOnlyList<string> Members)>> groups, Action<string, IReadOnlyList<string>> save, Action<string> delete)
    { _groups = groups; _saveGroup = save; _deleteGroup = delete; RefreshGroups(); }
    public void SetSendGroup(Func<IReadOnlyList<string>, string, string> sendGroup) => _sendGroup = sendGroup;
    public void SetAddBot(Func<(string Name, string PubHex)?> addBot) => _addBot = addBot;
    /// <summary>The live directory of people ONLINE right now (identity pubkey + reachable endpoint + @handle),
    /// from the node's presence beacons. Shown automatically — no one has to be added first.</summary>
    public void SetOnlineDirectory(Func<IReadOnlyList<(string PubHex, string Endpoint, string Handle)>> online) => _online = online;

    public ChatView(byte[] myPub, Func<IReadOnlyList<(string, string)>> peers, Func<string, string, string, bool, string> send)
    {
        _peers = peers; _send = send;
        Background = new SolidColorBrush(Color.FromRgb(0x0D, 0x0D, 0x0D)); Foreground = Brushes.White;
        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(new TextBlock { Text = "Chat — pick a contact or discovered player. Send to one, to everyone, or to a group.", FontSize = 18, FontWeight = FontWeights.Bold, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap });

        var myHex = Convert.ToHexString(myPub).ToLowerInvariant();
        var idLine = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        idLine.Children.Add(new TextBlock { Text = "Your identity: ", Foreground = Brushes.Gainsboro, VerticalAlignment = VerticalAlignment.Center });
        idLine.Children.Add(new TextBox { Text = myHex, IsReadOnly = true, Width = 460, FontFamily = new FontFamily("Consolas"), Foreground = Brushes.LightGreen, VerticalAlignment = VerticalAlignment.Center });
        var copyKey = new Button { Content = "Copy", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 4, 10, 4) };
        copyKey.Click += (_, _) => { for (int i = 0; i < 5; i++) { try { Clipboard.SetText(myHex); _status.Text = "Identity key copied."; break; } catch { System.Threading.Thread.Sleep(40); } } };
        idLine.Children.Add(copyKey);
        root.Children.Add(idLine);

        root.Children.Add(new TextBlock { Text = "People — your contacts + bots + players discovered. Add anyone here, then message or group them:", Foreground = Brushes.Gainsboro, Margin = new Thickness(0, 12, 0, 2), TextWrapping = TextWrapping.Wrap });
        root.Children.Add(_peerList);

        // ADD IDENTITIES — front and centre. You don't need anyone to be online: add a bot, paste an identity key,
        // or save a discovered player. Everyone you add can be messaged (even offline) and put into a group.
        var addLine = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        var addBot = new Button { Content = "➕ Add a bot", Padding = new Thickness(12, 5, 12, 5), FontWeight = FontWeights.Bold, ToolTip = "Create one of YOUR bots as a chat identity — instantly message it or add it to a group (no game needed)." };
        addBot.Click += (_, _) =>
        {
            if (_addBot == null) { _status.Text = "Add-bot unavailable."; return; }
            var b = _addBot.Invoke();
            if (b == null) { _status.Text = "Could not add a bot (unlock your wallet first)."; return; }
            RefreshPeers();
            _status.Text = $"Added bot {b.Value.Name} — select it to message, or use New group… to group it.";
        };
        addLine.Children.Add(addBot);
        var addBtn = new Button { Content = "➕ Add identity by key…", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(12, 5, 12, 5) };
        addBtn.Click += (_, _) => AddContactDialog();
        addLine.Children.Add(addBtn);
        var saveC = new Button { Content = "Save selected as contact…", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 5, 10, 5) };
        saveC.Click += (_, _) =>
        {
            int i = _peerList.SelectedIndex;
            if (i < 0 || i >= _current.Count) { _status.Text = "Select someone first."; return; }
            SaveContactDialog(_current[i].PubHex);
        };
        addLine.Children.Add(saveC);
        root.Children.Add(addLine);

        root.Children.Add(new TextBlock { Text = "Messages received (transactions pushed to you / found on-chain):", Foreground = Brushes.Gainsboro, Margin = new Thickness(0, 12, 0, 2) });
        root.Children.Add(_log);

        // HOW to send. (Every message is a Bitcoin tx under the hood.)
        _mode.Items.Add("To ONE — encrypted to the selected person (ECDH)");
        _mode.Items.Add("To ALL — encrypted to everyone you know (broadcast-encryption patent)");
        _mode.Items.Add("To a GROUP — encrypted to the group (broadcast encryption)");
        _mode.SelectedIndex = 0;
        _mode.SelectionChanged += (_, _) => UpdateGroupRow();
        var modeLine = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        modeLine.Children.Add(new TextBlock { Text = "Send ", Foreground = Brushes.Gainsboro, VerticalAlignment = VerticalAlignment.Center });
        modeLine.Children.Add(_mode);
        root.Children.Add(modeLine);

        // Group row (only meaningful in GROUP mode): pick a saved group, or create / manage one.
        var groupLine = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        groupLine.Children.Add(new TextBlock { Text = "Group ", Foreground = Brushes.Gainsboro, VerticalAlignment = VerticalAlignment.Center });
        groupLine.Children.Add(_groupPick);
        var newGroup = new Button { Content = "New group…", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 4, 10, 4) };
        newGroup.Click += (_, _) => CreateGroupDialog();
        groupLine.Children.Add(newGroup);
        var delGroup = new Button { Content = "Delete", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(10, 4, 10, 4) };
        delGroup.Click += (_, _) => { if (_groupPick.SelectedItem is string n && n.Length > 0) { _deleteGroup?.Invoke(n); RefreshGroups(); _status.Text = $"Deleted group {n}."; } };
        groupLine.Children.Add(delGroup);
        groupLine.Children.Add(_groupHint);
        root.Children.Add(groupLine);

        var line = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        line.Children.Add(new TextBlock { Text = "Message ", Foreground = Brushes.Gainsboro, VerticalAlignment = VerticalAlignment.Center });
        line.Children.Add(_text);
        var sendBtn = new Button { Content = "Send", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(14, 6, 14, 6), FontWeight = FontWeights.Bold };
        sendBtn.Click += (_, _) => DoSend();
        _text.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) { DoSend(); e.Handled = true; } };
        line.Children.Add(sendBtn);
        root.Children.Add(line);
        root.Children.Add(_status);

        Content = new ScrollViewer { Content = root };

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) => RefreshPeers();
        timer.Start();
        RefreshPeers();
        UpdateGroupRow();
    }

    private void DoSend()
    {
        if (string.IsNullOrWhiteSpace(_text.Text)) { _status.Text = "Type a message."; return; }
        var sent = _text.Text;
        int mode = _mode.SelectedIndex;

        if (mode == 1)   // ALL — broadcast-encryption (patent) sealed to every known recipient; never plaintext
        {
            _status.Text = _send("", "", sent, true);
            _log.Items.Insert(0, $"[{DateTime.Now:HH:mm:ss}] me → everyone (encrypted):  {sent}");
            _text.Clear();
            return;
        }
        if (mode == 2)   // GROUP — broadcast encryption
        {
            if (_sendGroup == null) { _status.Text = "Group send unavailable."; return; }
            if (_groupPick.SelectedItem is not string gname || gname.Length == 0) { _status.Text = "Pick a group, or press New group…"; return; }
            var members = (_groups?.Invoke() ?? new List<(string, IReadOnlyList<string>)>()).FirstOrDefault(g => g.Name == gname).Members;
            if (members == null || members.Count == 0) { _status.Text = "That group has no members."; return; }
            _status.Text = _sendGroup(members, sent);
            _log.Items.Insert(0, $"[{DateTime.Now:HH:mm:ss}] me → group {gname} ({members.Count}):  {sent}");
            _text.Clear();
            return;
        }
        // ONE — encrypted direct
        int i = _peerList.SelectedIndex;
        if (i < 0 || i >= _current.Count) { _status.Text = "Pick a person above for a direct message, or switch to ALL / GROUP."; return; }
        _status.Text = _send(_current[i].PubHex, _current[i].Endpoint, sent, false);
        _log.Items.Insert(0, $"[{DateTime.Now:HH:mm:ss}] me → {_current[i].Label}:  {sent}");
        _text.Clear();
    }

    private void UpdateGroupRow()
    {
        bool group = _mode.SelectedIndex == 2;
        _groupPick.IsEnabled = group;
        _groupHint.Text = group
            ? (_groupPick.Items.Count == 0 ? "No groups yet — press New group… to create one." : "Encrypted once to every member (only they can read).")
            : "";
    }

    private void RefreshGroups()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(RefreshGroups)); return; }
        var sel = _groupPick.SelectedItem as string;
        _groupPick.Items.Clear();
        foreach (var g in _groups?.Invoke() ?? new List<(string, IReadOnlyList<string>)>()) _groupPick.Items.Add(g.Name);
        if (sel != null && _groupPick.Items.Contains(sel)) _groupPick.SelectedItem = sel;
        else if (_groupPick.Items.Count > 0) _groupPick.SelectedIndex = 0;
        UpdateGroupRow();
    }

    private void RefreshPeers()
    {
        var sel = _peerList.SelectedIndex >= 0 && _peerList.SelectedIndex < _current.Count ? _current[_peerList.SelectedIndex].PubHex : null;

        // ONLINE DIRECTORY (presence) + discovered peers + saved contacts, deduped by identity key. The online
        // directory and gossip peers BOTH carry a live endpoint, so a person who is online can be messaged
        // INSTANTLY (IP-to-IP) — no one has to be added first. A contact with no live endpoint is still reachable
        // via the on-chain store-and-forward path.
        var peers = _peers().ToList();
        var online = _online?.Invoke() ?? new List<(string PubHex, string Endpoint, string Handle)>();
        var endpointByPub = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var handleByPub = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (pub, ep) in peers) if (!string.IsNullOrEmpty(ep)) endpointByPub.TryAdd(pub.ToLowerInvariant(), ep);
        foreach (var (pub, ep, h) in online)
        {
            if (!string.IsNullOrEmpty(ep)) endpointByPub[pub.ToLowerInvariant()] = ep;   // presence is the freshest endpoint
            if (!string.IsNullOrWhiteSpace(h)) handleByPub[pub.ToLowerInvariant()] = h;
        }
        var merged = new List<(string Label, string PubHex, string Endpoint)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (label, pub) in _contacts?.Invoke() ?? new List<(string, string)>())
        {
            if (!seen.Add(pub)) continue;
            var ep = endpointByPub.TryGetValue(pub.ToLowerInvariant(), out var e) ? e : "";
            merged.Add(($"{label}{(ep.Length > 0 ? "  ● online" : "")}", pub, ep));
        }
        // people ONLINE who are not already a saved contact — show them with their self-attested @handle so you
        // can message them straight away (a real "who's online" directory).
        foreach (var (pub, ep, h) in online)
        {
            if (!seen.Add(pub)) continue;
            var name = !string.IsNullOrWhiteSpace(h) ? h : _handleFor?.Invoke(pub) ?? $"{pub[..Math.Min(16, pub.Length)]}…";
            merged.Add(($"{name}  ● online", pub, ep));
        }
        foreach (var (pub, ep) in peers)
        {
            if (!seen.Add(pub)) continue;
            var h = handleByPub.TryGetValue(pub.ToLowerInvariant(), out var hh) ? hh : _handleFor?.Invoke(pub);
            merged.Add(((h != null ? h + "  " : "") + $"{pub[..Math.Min(16, pub.Length)]}…" + (ep.Length > 0 ? "  ● online" : ""), pub, ep));
        }

        _current = merged;
        _peerList.Items.Clear();
        foreach (var m in _current) _peerList.Items.Add(m.Label);
        if (_current.Count == 0) _peerList.Items.Add("(no one online yet — open the app on another machine/window and they appear here automatically)");
        if (sel != null) { var idx = _current.FindIndex(p => string.Equals(p.PubHex, sel, StringComparison.OrdinalIgnoreCase)); if (idx >= 0) _peerList.SelectedIndex = idx; }
    }

    private void AddContactDialog()
    {
        var handle = new TextBox { Width = 260, Margin = new Thickness(0, 2, 0, 8) };
        var key = new TextBox { Width = 260, FontFamily = new FontFamily("Consolas") };
        var ok = new Button { Content = "Add contact", Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(12, 6, 12, 6) };
        var sp = new StackPanel { Margin = new Thickness(14), Children = { new TextBlock { Text = "Handle / name:" }, handle, new TextBlock { Text = "Identity public key (66 hex):" }, key, ok } };
        var win = new Window { Title = "Add a contact", SizeToContent = SizeToContent.WidthAndHeight, Owner = Window.GetWindow(this), Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14)), Content = sp };
        ok.Click += (_, _) =>
        {
            var h = handle.Text.Trim(); var k = key.Text.Trim().ToLowerInvariant();
            if (h.Length == 0 || k.Length != 66) { _status.Text = "Need a handle and a 66-hex identity key."; return; }
            _saveContact?.Invoke(h, k); RefreshPeers(); _status.Text = $"Added @{h}."; win.Close();
        };
        win.ShowDialog();
    }

    private void SaveContactDialog(string pub)
    {
        var box = new TextBox { Width = 240 };
        var ok = new Button { Content = "Save", Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(10, 6, 10, 6) };
        var win = new Window { Title = "Save contact", SizeToContent = SizeToContent.WidthAndHeight, Owner = Window.GetWindow(this), Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14)), Content = new StackPanel { Margin = new Thickness(12), Children = { new TextBlock { Text = "Handle for this person:" }, box, ok } } };
        ok.Click += (_, _) => { if (box.Text.Trim().Length > 0) { _saveContact?.Invoke(box.Text.Trim(), pub); RefreshPeers(); _status.Text = $"Saved @{box.Text.Trim()}."; } win.Close(); };
        win.ShowDialog();
    }

    /// <summary>Create a group by NAMING it and multi-selecting members from contacts + discovered people (incl. bots).</summary>
    private void CreateGroupDialog()
    {
        if (_saveGroup == null) { _status.Text = "Groups unavailable."; return; }
        var name = new TextBox { Width = 280, Margin = new Thickness(0, 2, 0, 8) };
        var list = new ListBox { SelectionMode = SelectionMode.Multiple, Height = 220, Width = 380, Background = new SolidColorBrush(Color.FromRgb(0x12, 0x12, 0x12)), Foreground = Brushes.White };
        var picks = _current.ToList();
        foreach (var m in picks) list.Items.Add(m.Label);
        var ok = new Button { Content = "Create group", Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(12, 6, 12, 6), FontWeight = FontWeights.Bold };
        var sp = new StackPanel { Margin = new Thickness(14), Children = { new TextBlock { Text = "Group name:" }, name, new TextBlock { Text = "Members (Ctrl/Shift-click to multi-select):" }, list, ok } };
        var win = new Window { Title = "New group", SizeToContent = SizeToContent.WidthAndHeight, Owner = Window.GetWindow(this), Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14)), Content = sp };
        ok.Click += (_, _) =>
        {
            var members = list.SelectedItems.Cast<object>().Select(o => picks[list.Items.IndexOf(o)].PubHex).Distinct().ToList();
            if (name.Text.Trim().Length == 0 || members.Count == 0) { return; }
            _saveGroup(name.Text.Trim(), members); RefreshGroups(); _groupPick.SelectedItem = name.Text.Trim();
            _status.Text = $"Group {name.Text.Trim()} created ({members.Count} members)."; win.Close();
        };
        win.ShowDialog();
    }

    private readonly Dictionary<string, DateTime> _recentShown = new();   // (sender|text) -> when, to dedup live vs on-chain

    /// <summary>Display a chat message that arrived as a transaction. The SAME message can reach us twice — once
    /// live (pushed IP-to-IP) and again when the on-chain sync finds it — so we suppress an identical
    /// (sender, text) seen within the last 90s.</summary>
    public void AddIncoming(string senderPubHex, string text)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => AddIncoming(senderPubHex, text))); return; }
        var key = senderPubHex + "" + text;
        var now = DateTime.Now;
        if (_recentShown.TryGetValue(key, out var when) && (now - when) < TimeSpan.FromSeconds(90)) { _recentShown[key] = now; return; }
        _recentShown[key] = now;
        if (_recentShown.Count > 256) foreach (var k in _recentShown.Where(kv => (now - kv.Value) > TimeSpan.FromMinutes(10)).Select(kv => kv.Key).ToList()) _recentShown.Remove(k);
        var h = _handleFor?.Invoke(senderPubHex);
        var who = h ?? (senderPubHex.Length >= 12 ? senderPubHex[..12] + "…" : senderPubHex);
        _log.Items.Insert(0, $"[{now:HH:mm:ss}] {who}:  {text}");
    }
}

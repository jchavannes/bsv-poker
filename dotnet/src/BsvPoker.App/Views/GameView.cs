using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using BsvPoker.App.Controls;
using BsvPoker.Core;
using BsvPoker.Net;

namespace BsvPoker.App.Views;

/// <summary>
/// The poker table. Two modes:
///  - PRACTICE (hot-seat): both hands on one screen, real engine, for solo testing.
///  - NETWORKED: real 2-player over the P2P mesh (dealerless mental-poker deal; you see only YOUR hole
///    cards until showdown; your controls are live only on your turn). Driven by <see cref="NetGame"/>.
/// </summary>
public sealed class GameView : UserControl
{
    private readonly P2PNode _node;
    private readonly byte[] _priv;
    private readonly byte[] _pub;
    private readonly CardVault _vault;
    private readonly Action _onCardsChanged;
    private readonly Func<IReadOnlyList<Card>, long, string>? _onChainSettle; // settle a completed hand on-chain via the wallet/node
    private const long OnChainPot = 20_000;                                    // demo stake in satoshis for an on-chain hand

    private long[] _stacks = { 100, 100 };
    private int _button;
    private HoldemState? _practice;
    private NetGame? _net;

    /// <summary>Raised for every in-game move so the host (MainWindow) can fund it as an on-chain tx and
    /// dual-path broadcast it (IP-to-IP + nodes). Every move becomes a real transaction.</summary>
    public event Action<NetGame.MoveRecord>? OnMove;
    private bool _botMode;
    private Variant _botVariant = Variant.TexasHoldem;
    private int _lastMintedHand = -1; // mint my hole-card NFTs once per dealt hand (by hand number)
    private bool _practiceMinted;     // mint my NFTs once per practice/bot hand too (ALL MINT)
    private int _practiceHandSeq;     // increments each practice/bot deal — a stable per-hand key for the clock
    private int _netResultHand = -1;  // which networked hand number the start-stack was captured for

    private readonly StackPanel _topCards = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly StackPanel _board = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 8) };
    private readonly StackPanel _botCards = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _topInfo = new() { Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _botInfo = new() { Foreground = Brushes.White, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBlock _pot = new() { Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0x82)), FontSize = 18, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center };
    // The BIG plain-language outcome banner — "YOU WIN +50 chips!" / "You lost this hand (−20)". Shown only when
    // a hand has finished, so a player who does not know poker ALWAYS sees clearly whether they won or lost.
    private readonly Border _resultBox = new() { CornerRadius = new CornerRadius(8), Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(0, 8, 0, 8), HorizontalAlignment = HorizontalAlignment.Center, Visibility = Visibility.Collapsed };
    private readonly TextBlock _result = new() { FontSize = 22, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
    private long _myStackBefore = -1;   // my stack at the start of the current hand, to compute "you won/lost N this hand"
    // PER-HAND COUNTDOWN: a visible clock on screen, starting at HandSeconds, that RESETS at the start of every
    // hand and ticks down while a hand is live. It is shown so the player always sees how long is left in the
    // current hand. (One minute per hand, restarting each hand — the user's explicit rule.)
    private readonly TextBlock _clock = new() { FontSize = 17, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 2) };
    public int HandSeconds { get; set; } = 60;          // the per-hand countdown length (resets every hand)
    private int _secondsLeft;                            // seconds remaining in the current hand
    private int _clockHandKey = -1;                      // which hand the clock is currently counting (to detect a new hand)
    private System.Windows.Threading.DispatcherTimer? _clockTimer;
    private readonly TextBlock _msg = new() { Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xF5, 0xE9)), FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _standings = new() { Foreground = new SolidColorBrush(Color.FromRgb(0xCF, 0xD8, 0xDC)), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _bet = new() { Width = 70, Text = "6", VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0) };
    private readonly Button _deal = Mk("Practice deal", "#444444");
    private readonly Button _fold = Mk("Fold", "#7A2E2E");
    private readonly Button _check = Mk("Check", "#333333");
    private readonly Button _call = Mk("Call", "#333333");
    private readonly Button _betBtn = Mk("Bet / Raise", "#2E5A7A");
    private readonly Button _leave = Mk("Leave table", "#555555");
    private readonly Button _onchain = Mk("On-chain settle", "#3A6E2E");

    /// <summary>Raised when the player leaves the table (so the host can stop any bot, etc.).</summary>
    public event Action? OnLeaveTable;

    /// <summary>Resolves an identity pubkey (hex) to a friendly label (pseudonym/@handle). Set by the host so the
    /// table shows the player's PSEUDONYM rather than a raw key. Falls back to "You" when unset/unknown.</summary>
    private Func<string, string?>? _labelFor;
    public void SetIdentityLabelResolver(Func<string, string?> labelFor) => _labelFor = labelFor;
    private string MyLabel()
    {
        try { var l = _labelFor?.Invoke(Convert.ToHexString(_pub).ToLowerInvariant()); if (!string.IsNullOrWhiteSpace(l)) return l; } catch { }
        return "You";
    }

    public GameView(P2PNode node, byte[] priv, byte[] pub, CardVault vault, Action onCardsChanged,
        Func<IReadOnlyList<Card>, long, string>? onChainSettle = null)
    {
        _node = node; _priv = priv; _pub = pub; _vault = vault; _onCardsChanged = onCardsChanged; _onChainSettle = onChainSettle;
        Background = new SolidColorBrush(Color.FromRgb(0x0D, 0x0D, 0x0D)); Foreground = Brushes.White;

        var felt = new Border { Margin = new Thickness(16), CornerRadius = new CornerRadius(160) };
        felt.Background = new RadialGradientBrush(Color.FromRgb(0x1F, 0x7A, 0x43), Color.FromRgb(0x0B, 0x4A, 0x28));
        var inner = new Border { CornerRadius = new CornerRadius(150), BorderBrush = new SolidColorBrush(Color.FromRgb(0xA9, 0x81, 0x2B)), BorderThickness = new Thickness(6), Margin = new Thickness(10) };
        var g = new Grid { Margin = new Thickness(24) };
        g.RowDefinitions.Add(new RowDefinition());
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition());
        var top = new StackPanel { VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center };
        top.Children.Add(_topInfo); top.Children.Add(_topCards); Grid.SetRow(top, 0); g.Children.Add(top);
        var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        _resultBox.Child = _result;
        mid.Children.Add(_clock); mid.Children.Add(_pot); mid.Children.Add(_board); mid.Children.Add(_resultBox); mid.Children.Add(_msg); mid.Children.Add(_standings); Grid.SetRow(mid, 1); g.Children.Add(mid);
        var bot = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center };
        bot.Children.Add(_botCards); bot.Children.Add(_botInfo); Grid.SetRow(bot, 2); g.Children.Add(bot);
        inner.Child = g; felt.Child = inner;

        var bar = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(10) };
        _deal.Click += (_, _) => PracticeDeal();
        _fold.Click += (_, _) => Do(ActionKind.Fold, 0);
        _check.Click += (_, _) => Do(ActionKind.Check, 0);
        _call.Click += (_, _) => Do(ActionKind.Call, 0);
        _betBtn.Click += (_, _) => { if (long.TryParse(_bet.Text.Trim(), out var to)) Do(ActionKind.Raise, to); };
        _leave.Click += (_, _) => LeaveTable();
        _onchain.Click += (_, _) => SettleOnChain();
        bar.Children.Add(_deal); bar.Children.Add(_fold); bar.Children.Add(_check); bar.Children.Add(_call); bar.Children.Add(_bet); bar.Children.Add(_betBtn);
        if (_onChainSettle != null) bar.Children.Add(_onchain);   // only when the wallet/node are available
        bar.Children.Add(_leave);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(felt, 0); root.Children.Add(felt);
        var barHost = new Border { Background = new SolidColorBrush(Color.FromRgb(0x16, 0x16, 0x16)), Padding = new Thickness(8) };
        barHost.Child = bar; Grid.SetRow(barHost, 1); root.Children.Add(barHost);
        Content = root;
        // the on-screen per-hand countdown ticks once a second
        _clockTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => ClockTick();
        _clockTimer.Start();
        Render();
    }

    // Identify the current live hand (practice/bot or networked) by a stable key, or -1 if no hand is in play.
    private int CurrentHandKey()
    {
        if (_net != null) return _net.Hand != null && !_net.Hand.Complete ? _net.HandNumber : -1;
        if (_practice is { Complete: false }) return _practiceHandSeq;
        return -1;
    }

    private void ClockTick()
    {
        int key = CurrentHandKey();
        if (key < 0)
        {
            // no live hand → hide/freeze the clock (between hands, or at a finished hand showing the result)
            _clock.Text = "";
            return;
        }
        if (key != _clockHandKey) { _clockHandKey = key; _secondsLeft = HandSeconds; }   // NEW hand → reset to a full minute
        else if (_secondsLeft > 0) _secondsLeft--;
        // show MM:SS, turning amber then red as it runs down, so the player always sees the time left in THIS hand
        int s = Math.Max(0, _secondsLeft);
        _clock.Text = $"⏱ Hand time: 0:{s:D2}";
        _clock.Foreground = s > 20 ? new SolidColorBrush(Color.FromRgb(0xCF, 0xD8, 0xDC))
                          : s > 10 ? new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07))
                                   : new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));
    }

    public void StartNetworked(string tableId, string tableName)
    {
        _net?.Stop();
        _practice = null;
        _lastMintedHand = -1;
        _net = new NetGame(_node, tableId, _priv, _pub);
        _net.OnUpdate += () => Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(Render));
        _net.OnMove += m => OnMove?.Invoke(m);   // surface every move so the host turns it into a funded on-chain dual-path tx
        _net.Start();
        Render();
    }

    private static Button Mk(string text, string hex)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        return new Button { Content = text, Width = 110, Margin = new Thickness(4), Padding = new Thickness(0, 8, 0, 8), Foreground = Brushes.White, BorderThickness = new Thickness(0), Background = new SolidColorBrush(c) };
    }

    /// <summary>
    /// Show the BIG, unmistakable end-of-hand result so a player who does NOT know poker always knows whether
    /// they won or lost, by how much, and (at a showdown) with what hand. Computes my net chip change this hand
    /// from the stack I started the hand with, names the winner from the engine's payouts, and colours the
    /// banner green for a win, red for a loss, grey for a tie/no-change.
    /// </summary>
    private void ShowResultBanner(HoldemState st, int mySeat, string opponentLabel)
    {
        long after = st.Seats[mySeat].Stack;
        long net = _myStackBefore >= 0 ? after - _myStackBefore : 0;   // chips I gained (+) or lost (−) this hand
        bool iWon = st.Payouts.TryGetValue(mySeat, out var myWin) && myWin > 0;
        bool tie = st.Payouts.Count > 1 && iWon;                        // I shared the pot

        // name MY best hand if there was a real showdown (a full board + my cards face-up)
        string handName = "";
        try
        {
            if (st.Board.Count >= 3 && st.Seats[mySeat].Hole.All(c => !c.IsFaceDown))
                handName = HandEval.BestForVariant(st.Seats[mySeat].Hole, st.Board, Variants.ExactlyTwoHole(st.Variant)).Category;
        }
        catch { }

        string headline;
        Color bg, fg = Colors.White;
        if (net > 0)
        {
            headline = (tie ? "YOU SPLIT THE POT" : "YOU WIN!") + $"   +{net} chips";
            if (handName.Length > 0) headline += $"\nwith {Article(handName)} {handName.ToLowerInvariant()}";
            bg = Color.FromRgb(0x2E, 0x7D, 0x32);   // green
        }
        else if (net < 0)
        {
            headline = $"You lost this hand   {net} chips";
            headline += $"\n{opponentLabel} won the pot.";
            bg = Color.FromRgb(0xB0, 0x2A, 0x2A);   // red
        }
        else
        {
            headline = "Hand over — no chips changed (tie / blinds returned).";
            bg = Color.FromRgb(0x4A, 0x4A, 0x4A);   // grey
        }
        _result.Text = headline;
        _result.Foreground = new SolidColorBrush(fg);
        _resultBox.Background = new SolidColorBrush(bg);
        _resultBox.Visibility = Visibility.Visible;
    }

    private static string Article(string word) => "AEIOU".IndexOf(char.ToUpperInvariant(word[0])) >= 0 ? "an" : "a";

    /// <summary>Surface a non-disruptive note under the table (e.g. that the hand was also recorded on-chain and
    /// is replayable). Shown in the standings line so it never interrupts play or steals focus.</summary>
    public void ShowOnChainNote(string note)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => ShowOnChainNote(note))); return; }
        if (!string.IsNullOrWhiteSpace(note)) _standings.Text = note;
    }

    /// <summary>Lobby "Play a bot" — you (seat 0) vs a practice bot (seat 1) at the chosen variant.</summary>
    public void StartBot(Variant variant)
    {
        _net?.Stop(); _net = null; _botMode = true; _botVariant = variant; _practiceMinted = false;
        for (int i = 0; i < 2; i++) if (_stacks[i] < 2) _stacks[i] = 100;
        var deck = MentalPoker.ShuffledFrom(new[] { MentalPoker.FreshEntropy(), MentalPoker.FreshEntropy() }, Variants.CardSet(variant));
        _myStackBefore = _stacks[0]; _practiceHandSeq++; _resultBox.Visibility = Visibility.Collapsed;  // new hand → fresh clock + clear last result
        _practice = HoldemState.Create(_stacks, _button, 1, 2, deck, variant);
        _button ^= 1;
        Render();
        DriveBot();
    }

    private void PracticeDeal()
    {
        _net?.Stop(); _net = null; _botMode = true; // the practice button now plays a bot at the current variant
        for (int i = 0; i < 2; i++) if (_stacks[i] < 2) _stacks[i] = 100;
        var deck = MentalPoker.ShuffledFrom(new[] { MentalPoker.FreshEntropy(), MentalPoker.FreshEntropy() }, Variants.CardSet(_botVariant));
        _myStackBefore = _stacks[0]; _practiceHandSeq++; _resultBox.Visibility = Visibility.Collapsed;  // new hand → fresh clock + clear last result
        _practice = HoldemState.Create(_stacks, _button, 1, 2, deck, _botVariant);
        _button ^= 1;
        Render();
        DriveBot();
    }

    private void DriveBot()
    {
        // bot is seat 1; act on its turns until it's the human's turn (seat 0) or the hand ends.
        while (_botMode && _practice is { Complete: false } st && st.ToAct == 1)
        {
            try { st.Apply(BotPolicy.Decide(st)); } catch { break; }
        }
        if (_practice is { Complete: true }) _stacks = _practice.Seats.Select(s => s.Stack).ToArray();
        Render();
    }

    private void Do(ActionKind kind, long amt)
    {
        if (_net != null) { _net.Act(kind, amt); return; }
        if (_practice == null || _practice.Complete) return;
        if (_botMode && _practice.ToAct != 0) return; // in bot mode you only act for seat 0
        try { _practice.Apply(new GameAction(kind, _practice.ToAct, amt)); } catch (Exception ex) { _msg.Text = ex.Message; return; }
        if (_practice.Complete) { _stacks = _practice.Seats.Select(s => s.Stack).ToArray(); Render(); return; }
        if (_botMode) { DriveBot(); return; }
        Render();
    }

    /// <summary>
    /// Leave the table at ANY time and keep your funds — without exception. Stops the networked session
    /// (with real BSV this is where the pre-signed nLockTime recovery returns your stake), drops any bot,
    /// and returns to a clean table. The button is never disabled.
    /// </summary>
    private void LeaveTable()
    {
        _net?.Stop(); _net = null; _practice = null; _botMode = false; _lastMintedHand = -1;
        OnLeaveTable?.Invoke();
        Render();
        _msg.Text = "You left the table. Your funds are safe and yours — you can always walk away.";
    }

    /// <summary>
    /// Settle the just-completed hand ON-CHAIN: reconstruct the 9-card heads-up deck from the finished hand
    /// and hand it to the wallet, which emits + broadcasts the full transaction tape (pot escrow → ... →
    /// settlement) on the real BSV network. Only available once the hand reached a 5-card-board showdown.
    /// </summary>
    private void SettleOnChain()
    {
        if (_onChainSettle == null) { _msg.Text = "On-chain settle is unavailable (no wallet/node)."; return; }
        var deck = CompletedHandDeck();
        if (deck == null) { _msg.Text = "Play a hand to showdown (a full 5-card board) first, then press On-chain settle."; return; }
        _msg.Text = _onChainSettle(deck, OnChainPot);
    }

    /// <summary>The 9-card heads-up deck (seat0 holes, seat1 holes, 5 board cards) of a completed practice hand, or null.</summary>
    private IReadOnlyList<Card>? CompletedHandDeck()
    {
        var st = _practice;
        if (st is not { Complete: true } || st.Seats.Count < 2 || st.Board.Count < 5) return null;
        var s0 = st.Seats[0].Hole.ToList(); var s1 = st.Seats[1].Hole.ToList();
        if (s0.Count < 2 || s1.Count < 2 || s0.Concat(s1).Any(c => c.IsFaceDown)) return null;
        return new List<Card> { s0[0], s0[1], s1[0], s1[1], st.Board[0], st.Board[1], st.Board[2], st.Board[3], st.Board[4] };
    }

    private void Render()
    {
        _topCards.Children.Clear(); _botCards.Children.Clear(); _board.Children.Clear(); _standings.Text = "";
        if (_net != null) RenderNet(); else RenderPractice();
    }

    private void RenderNet()
    {
        var ng = _net!;
        var hand = ng.Hand;
        _deal.IsEnabled = true;
        if (hand == null)
        {
            for (int i = 0; i < 2; i++) { _topCards.Children.Add(new CardView()); _botCards.Children.Add(new CardView()); }
            for (int i = 0; i < 5; i++) _board.Children.Add(new CardView());
            _pot.Text = "Pot: 0"; _msg.Text = ng.Status; _botInfo.Text = MyLabel(); _topInfo.Text = "Opponent";
            _fold.IsEnabled = _check.IsEnabled = _call.IsEnabled = _betBtn.IsEnabled = false;
            return;
        }
        int me = ng.MySeat < 0 ? 0 : ng.MySeat;
        // capture my starting stack ONCE per networked hand so the result banner can show what I won/lost
        if (_netResultHand != ng.HandNumber && !hand.Complete) { _netResultHand = ng.HandNumber; _myStackBefore = hand.Seats[me].Stack + hand.Seats[me].TotalCommit; _resultBox.Visibility = Visibility.Collapsed; }
        // Cards are NFTs: mint MY hole cards into my wallet vault (sealed to me) once per dealt hand.
        if (_lastMintedHand != ng.HandNumber && ng.MySeat >= 0 && hand.Seats[me].Hole.All(c => !c.IsFaceDown))
        {
            _lastMintedHand = ng.HandNumber;
            foreach (var c in hand.Seats[me].Hole) _vault.AddCard(c.Index, System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            _onCardsChanged();
        }
        foreach (var c in hand.Seats[me].Hole) _botCards.Children.Add(MakeMyCard(c, null));   // YOUR cards are clickable
        // one group per opponent seat (holes are face-down sentinels of the variant's count until showdown)
        _topInfo.Text = hand.Seats.Count > 2 ? "Opponents" : "";
        foreach (var s in hand.Seats.Where(s => s.Seat != me))
        {
            bool theirTurn = !hand.Complete && hand.ToAct == s.Seat;
            var grp = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(12, 0, 12, 0), HorizontalAlignment = HorizontalAlignment.Center };
            grp.Children.Add(new TextBlock { Text = $"Seat {s.Seat} — {s.Stack}{(s.Folded ? " (folded)" : "")}{(theirTurn ? "  ◀" : "")}", Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
            var cards = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            foreach (var c in s.Hole) { var cv = new CardView(); if (c.IsFaceDown) cv.ShowBack(); else cv.ShowCard(c); cards.Children.Add(cv); }
            grp.Children.Add(cards);
            _topCards.Children.Add(grp);
        }
        for (int i = 0; i < 5; i++) { var cv = new CardView(); if (i < hand.Board.Count) cv.ShowCard(hand.Board[i]); else cv.ShowEmpty(); _board.Children.Add(cv); }
        _pot.Text = $"Pot: {hand.Pot}";
        bool myTurn = !hand.Complete && hand.ToAct == me;
        _botInfo.Text = $"{MyLabel()} (seat {me}) — stack {hand.Seats[me].Stack}{(myTurn ? "  ◀ your turn" : "")}";
        if (hand.Complete) ShowResultBanner(hand, me, opponentLabel: "your opponent");
        _msg.Text = ng.Status + (ng.HandLog.Count > 0 ? "   •   " + ng.HandLog[^1] : "");
        _standings.Text = "Session — " + ng.Standings;
        var la = myTurn ? hand.Legal() : null;
        _fold.IsEnabled = la?.CanFold ?? false;
        _check.IsEnabled = la?.CanCheck ?? false;
        _call.IsEnabled = la?.CanCall ?? false;
        _betBtn.IsEnabled = la?.CanBetOrRaise ?? false;
        if (la is { CanCall: true }) _call.Content = $"Call {la.CallAmount}"; else _call.Content = "Call";
        if (la is { CanBetOrRaise: true }) _bet.Text = la.MinRaiseTo.ToString();
    }

    /// <summary>Charge a small on-chain fee for a card action (e.g. discarding to draw a replacement). Set by the
    /// host (MainWindow) to fund + broadcast a real 1-sat tx; returns true if it was paid.</summary>
    public Func<long, bool>? PayFee;

    /// <summary>One of MY cards, made CLICKABLE: click it to select (gold lift) and open its actions — view it,
    /// copy it, and (where the game allows) discard it and draw a replacement for a fee.</summary>
    private CardView MakeMyCard(Card c, Action? onSwap)
    {
        var cv = new CardView(); cv.ShowCard(c); cv.SetSelectable(true);
        cv.Clicked += clicked => ShowCardActions(c, onSwap);
        return cv;
    }

    private void ShowCardActions(Card c, Action? onSwap)
    {
        var sp = new StackPanel { Margin = new Thickness(16) };
        var big = new CardView(); big.ShowCard(c); big.Width = 96; big.Height = 138; big.HorizontalAlignment = HorizontalAlignment.Center;
        sp.Children.Add(big);
        sp.Children.Add(new TextBlock { Text = $"{c.RankLabel}{c.Glyph}", Foreground = Brushes.White, FontSize = 18, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) });
        sp.Children.Add(new TextBlock { Text = "This is YOUR card — sealed on-chain as an NFT only you can open.", Foreground = new SolidColorBrush(Color.FromRgb(0xCF, 0xD8, 0xDC)), TextWrapping = TextWrapping.Wrap, MaxWidth = 240, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 4, 0, 10) });
        var win = new Window { Title = "Card", SizeToContent = SizeToContent.WidthAndHeight, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = Window.GetWindow(this), Background = new SolidColorBrush(Color.FromRgb(0x12, 0x12, 0x12)), ResizeMode = ResizeMode.NoResize };
        var copy = Mk("Copy card", "#2E5A7A"); copy.Click += (_, _) => { try { for (int i = 0; i < 5; i++) { try { System.Windows.Clipboard.SetText($"{c.RankLabel}{c.Glyph}"); break; } catch { System.Threading.Thread.Sleep(40); } } } catch { } };
        sp.Children.Add(copy);
        if (onSwap != null)
        {
            var swap = Mk("Discard & draw a new card (pay 1 sat)", "#8A5A00");
            swap.Click += (_, _) => { win.Close(); onSwap(); };
            sp.Children.Add(swap);
        }
        else
        {
            sp.Children.Add(new TextBlock { Text = "(This variant deals community cards — there is no card swap. Discard/draw applies to draw games.)", Foreground = new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E)), TextWrapping = TextWrapping.Wrap, MaxWidth = 240, TextAlignment = TextAlignment.Center, FontSize = 11, Margin = new Thickness(0, 8, 0, 0) });
        }
        var close = Mk("Close", "#444444"); close.Click += (_, _) => win.Close(); sp.Children.Add(close);
        win.Content = sp; win.ShowDialog();
    }

    // Discard hole card #idx in the PRACTICE hand and draw a fresh replacement, charging a real 1-sat fee.
    private void SwapPracticeCard(int idx)
    {
        var st = _practice;
        if (st == null || st.Complete || idx < 0 || idx >= st.Seats[0].Hole.Length) return;
        if (PayFee != null && !PayFee(1)) { _msg.Text = "Could not pay the 1-sat draw fee — fund your wallet first."; return; }
        // a fresh card not already on the table (hole + board + opponent holes)
        var inPlay = new HashSet<int>(st.Seats.SelectMany(s => s.Hole).Where(c => !c.IsFaceDown).Select(c => c.Index).Concat(st.Board.Select(c => c.Index)));
        var rnd = System.Security.Cryptography.RandomNumberGenerator.GetInt32(52);
        int tries = 0; while (inPlay.Contains(rnd) && tries++ < 200) rnd = System.Security.Cryptography.RandomNumberGenerator.GetInt32(52);
        st.Seats[0].Hole[idx] = Card.FromIndex(rnd);
        _vault.AddCard(rnd, System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)); _onCardsChanged();
        _msg.Text = $"Discarded a card and drew {Card.FromIndex(rnd).RankLabel}{Card.FromIndex(rnd).Glyph} (paid 1 sat, on-chain).";
        Render();
    }

    private void RenderPractice()
    {
        var st = _practice;
        if (st == null)
        {
            _msg.Text = "Join a table in the Lobby to play someone — or press Practice deal (hot-seat).";
            for (int i = 0; i < 2; i++) { _topCards.Children.Add(new CardView()); _botCards.Children.Add(new CardView()); }
            for (int i = 0; i < 5; i++) _board.Children.Add(new CardView());
            _pot.Text = "Pot: 0"; _botInfo.Text = $"{MyLabel()} — {_stacks[0]}"; _topInfo.Text = $"Player 2 — {_stacks[1]}";
            _deal.IsEnabled = true; _fold.IsEnabled = _check.IsEnabled = _call.IsEnabled = _betBtn.IsEnabled = false;
            return;
        }
        // ALL MINT: every hand — including a bot/practice hand — mints MY hole cards as encrypted NFTs into my
        // wallet vault (sealed to me), so they show up under My NFTs during the game, exactly like a networked
        // hand. Minted once per dealt hand.
        if (!_practiceMinted && st.Seats[0].Hole.Any() && st.Seats[0].Hole.All(c => !c.IsFaceDown))
        {
            _practiceMinted = true;
            foreach (var c in st.Seats[0].Hole) _vault.AddCard(c.Index, System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            _onCardsChanged();
        }
        for (int i = 0; i < st.Seats[0].Hole.Length; i++) { int idx = i; _botCards.Children.Add(MakeMyCard(st.Seats[0].Hole[i], () => SwapPracticeCard(idx))); }   // clickable: select/discard/draw
        foreach (var c in st.Seats[1].Hole) { var cv = new CardView(); if (_botMode && !st.Complete) cv.ShowBack(); else cv.ShowCard(c); _topCards.Children.Add(cv); }
        for (int i = 0; i < 5; i++) { var cv = new CardView(); if (i < st.Board.Count) cv.ShowCard(st.Board[i]); else cv.ShowEmpty(); _board.Children.Add(cv); }
        _pot.Text = $"Pot: {st.Pot}";
        bool myTurn = !st.Complete && st.ToAct == 0;
        _botInfo.Text = $"{MyLabel()} (seat 0) — {st.Seats[0].Stack}{(myTurn ? "  ◀ your turn" : "")}";
        _topInfo.Text = (_botMode ? "Bot (opponent)" : "Seat 1") + $" — {st.Seats[1].Stack}";
        // When the hand is OVER, show a BIG plain-language result so a non-poker player always knows what happened.
        if (st.Complete) ShowResultBanner(st, mySeat: 0, opponentLabel: _botMode ? "the bot" : "Seat 1");
        _msg.Text = st.Complete ? "Press \"Deal\" (or Practice deal) to play the next hand." : st.Message;
        var la = st.Complete ? null : st.Legal();
        _deal.IsEnabled = st.Complete;
        _fold.IsEnabled = la?.CanFold ?? false;
        _check.IsEnabled = la?.CanCheck ?? false;
        _call.IsEnabled = la?.CanCall ?? false;
        _betBtn.IsEnabled = la?.CanBetOrRaise ?? false;
        if (la is { CanCall: true }) _call.Content = $"Call {la.CallAmount}"; else _call.Content = "Call";
        if (la is { CanBetOrRaise: true }) _bet.Text = la.MinRaiseTo.ToString();
    }
}

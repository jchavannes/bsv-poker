using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BsvPoker.Core;
using BsvPoker.Core.Games;
using BsvPoker.Crypto;

namespace BsvPoker.App.Views;

/// <summary>
/// REPLAY a REAL on-chain game. Every move of a hand is a real Bitcoin transaction, so you load a game by its
/// START ADDRESS (or any of its transaction ids) and the app pulls EVERY transaction of that game off the chain,
/// in order, and steps through them move by move — table genesis, the deal, each bet, the board, the showdown,
/// the settlement — narrated in plain language with the on-chain txid of each step. This is the actual game, not
/// a demo. (A locally-played hand can also be loaded directly, and a built sample is available offline.)
/// </summary>
public sealed class ReplayView : UserControl
{
    private static readonly Brush Sub = new SolidColorBrush(Color.FromRgb(0xCF, 0xD8, 0xDC));
    private static readonly Brush Gold = new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0x82));

    private List<Chain.Tx> _txs = new();
    private int _i;

    /// <summary>Set by the host (MainWindow): given a game START ADDRESS or any of its tx ids, fetch EVERY
    /// transaction of that game from the chain, in order. Lets Replay load a real game with no local data.</summary>
    public Func<string, Task<List<Chain.Tx>>>? GameFetcher;

    private readonly TextBox _addr = new() { Width = 460, VerticalContentAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Consolas") };
    private readonly TextBlock _title = new() { Foreground = Brushes.White, FontSize = 22, FontWeight = FontWeights.Bold };
    private readonly TextBlock _counter = new() { Foreground = Gold, FontSize = 15, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 8, 0, 4) };
    private readonly TextBlock _narrate = new() { Foreground = Brushes.White, FontSize = 16, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4), MaxWidth = 820 };
    private readonly TextBlock _txid = new() { Foreground = Sub, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8), MaxWidth = 820 };
    private readonly TextBlock _status = new() { Foreground = Sub, FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4), MaxWidth = 820 };
    private readonly Button _prev = Btn("◀ Prev");
    private readonly Button _next = Btn("Next ▶");
    private readonly Button _first = Btn("⏮ Start");
    private readonly Button _last = Btn("End ⏭");

    public ReplayView()
    {
        Background = new SolidColorBrush(Color.FromRgb(0x0D, 0x0D, 0x0D));
        var sp = new StackPanel { Margin = new Thickness(26) };
        sp.Children.Add(_title);
        _title.Text = "Replay a real on-chain game";
        sp.Children.Add(new TextBlock { Foreground = Sub, FontSize = 14, TextWrapping = TextWrapping.Wrap, MaxWidth = 820,
            Text = "Paste the game's START ADDRESS (or any transaction id from the game). The app pulls every move of that game off the chain and replays it, step by step — each step in plain language with the on-chain txid you can verify." });

        var loadRow = new WrapPanel { Margin = new Thickness(0, 12, 0, 6) };
        loadRow.Children.Add(new TextBlock { Text = "Game start address / tx id: ", Foreground = Sub, VerticalAlignment = VerticalAlignment.Center });
        loadRow.Children.Add(_addr);
        var go = Btn("Replay this game"); go.Click += async (_, _) => await LoadFromChain();
        loadRow.Children.Add(go);
        sp.Children.Add(loadRow);

        sp.Children.Add(_status);
        sp.Children.Add(_counter);
        sp.Children.Add(_narrate);
        sp.Children.Add(_txid);

        var nav = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        _first.Click += (_, _) => { _i = 0; Show(); };
        _prev.Click += (_, _) => { if (_i > 0) _i--; Show(); };
        _next.Click += (_, _) => { if (_i < _txs.Count - 1) _i++; Show(); };
        _last.Click += (_, _) => { _i = _txs.Count - 1; Show(); };
        nav.Children.Add(_first); nav.Children.Add(_prev); nav.Children.Add(_next); nav.Children.Add(_last);
        sp.Children.Add(nav);

        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = sp };
        SetNavEnabled(false);
        _narrate.Text = "No game loaded — paste a game's start address above and press \"Replay this game\".";
    }

    /// <summary>Load a locally-played hand's transactions directly (e.g. one this app just produced).</summary>
    public void Load(OnChainHandTape.Tape tape) => LoadTxs(tape.Steps.Select(s => s.Tx).ToList());

    public void LoadTxs(List<Chain.Tx> txs)
    {
        _txs = txs ?? new(); _i = 0;
        SetNavEnabled(_txs.Count > 0);
        _status.Text = _txs.Count > 0 ? $"Loaded {_txs.Count} on-chain move(s)." : "No game moves found at that address.";
        Show();
    }

    private async Task LoadFromChain()
    {
        var key = _addr.Text.Trim();
        if (key.Length == 0) { _status.Text = "Enter the game's start address or a tx id."; return; }
        if (GameFetcher == null) { _status.Text = "Chain lookup is unavailable (open and unlock your wallet first)."; return; }
        _status.Text = "Fetching the game's transactions from the chain…";
        try
        {
            var txs = await GameFetcher(key);
            LoadTxs(txs);
        }
        catch (System.Exception ex) { _status.Text = "Could not load that game: " + ex.Message; }
    }

    private void SetNavEnabled(bool on) { _first.IsEnabled = _prev.IsEnabled = _next.IsEnabled = _last.IsEnabled = on; }

    private void Show()
    {
        if (_txs.Count == 0) { _counter.Text = ""; _txid.Text = ""; return; }
        if (_i < 0) _i = 0; if (_i >= _txs.Count) _i = _txs.Count - 1;
        var tx = _txs[_i];
        _counter.Text = $"Move {_i + 1} of {_txs.Count}";
        _narrate.Text = Narrate(tx);
        _txid.Text = "tx: " + Chain.Txid(tx);
        _prev.IsEnabled = _i > 0; _next.IsEnabled = _i < _txs.Count - 1;
    }

    // Plain-language narration of one on-chain transaction, parsing its typed output back to its kind + fields.
    private static string Narrate(Chain.Tx tx)
    {
        var parsed = TryParse(tx);
        if (parsed == null) return "💸 A transaction in this game (funding or change).";
        return parsed.Kind switch
        {
            TxKind.TableGenesis => "🎲 The table is created on-chain (its genesis transaction).",
            TxKind.GameStart => "▶️ A game is started at the table.",
            TxKind.HandStart => "🃏 A new hand begins.",
            TxKind.PotEscrow => "💰 The POT is funded into a shared escrow — the real money at stake.",
            TxKind.Recovery => "🛟 A pre-signed refund is co-signed up front, so no stake can ever be stranded.",
            TxKind.ShuffleStage => "🔀 A player masks and shuffles the encrypted deck (mental poker — no one sees the cards).",
            TxKind.ShuffleReveal => "✅ The shuffle secrets are revealed so anyone can verify no card was swapped or duplicated.",
            TxKind.Deal => "🤚 A card is dealt, encrypted so ONLY its owner can read it.",
            TxKind.BoardReveal => "🂠 Community board cards are revealed in the middle.",
            TxKind.Bet => DescribeBet(parsed),
            TxKind.Showdown => "👀 At showdown, a player reveals their hole cards on-chain.",
            TxKind.Settlement => "🏆 The pot is paid out to the winner — the hand is settled on-chain.",
            TxKind.Points => "⭐ A point is recorded on-chain, bound to the scorer's identity and this game.",
            TxKind.Identity => "🪪 An identity NFT (a player) referenced in the game.",
            _ => $"On-chain step: {parsed.Kind}.",
        };
    }

    private static string DescribeBet(TxTemplates.Parsed? p)
    {
        // Bet fields: handId, seat, action, amount
        if (p == null || p.Fields.Length < 3) return "💵 A betting action is recorded on-chain.";
        int seat = p.Fields[1].Length > 0 ? p.Fields[1][0] : 0;
        int action = p.Fields[2].Length > 0 ? p.Fields[2][0] : 0;
        string a = action switch { 0 => "checks/folds", 1 => "calls", 2 => "bets/raises", _ => "acts" };
        return $"💵 Seat {seat} {a} — recorded as its own on-chain transaction.";
    }

    private static TxTemplates.Parsed? TryParse(Chain.Tx tx)
    {
        foreach (var o in tx.Outs) { var p = TxTemplates.Parse(o.Script); if (p != null) return p; }
        return null;
    }

    private static Button Btn(string text) => new()
    {
        Content = text, Margin = new Thickness(4), Padding = new Thickness(12, 8, 12, 8), MinWidth = 90,
        Foreground = Brushes.White, BorderThickness = new Thickness(0),
        Background = new SolidColorBrush(Color.FromRgb(0x2E, 0x5A, 0x7A)),
    };
}

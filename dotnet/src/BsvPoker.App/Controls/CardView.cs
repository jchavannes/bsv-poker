using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using BsvPoker.Core;

namespace BsvPoker.App.Controls;

/// <summary>A graphically-rendered playing card (WPF shapes/text, no image assets). Can be made clickable and
/// selectable so the player can pick a card directly (e.g. to discard/replace it, view it, or act on it).</summary>
public sealed class CardView : Border
{
    /// <summary>The card currently shown (null if face-down/empty), so click handlers know what was clicked.</summary>
    public Card? Card { get; private set; }
    /// <summary>When true the card responds to clicks (hand cursor, selection highlight) and raises <see cref="Clicked"/>.</summary>
    public bool Selectable { get; set; }
    /// <summary>True while the card is selected (a gold highlight + lift). Toggled on click when Selectable.</summary>
    public bool Selected { get; private set; }
    /// <summary>Raised when a Selectable card is clicked (after its Selected state has toggled).</summary>
    public event Action<CardView>? Clicked;

    private static readonly Thickness Rest = new(4);
    private static readonly Thickness Lifted = new(4, -8, 4, 16);   // raise a selected card

    public CardView()
    {
        Width = 60; Height = 86; CornerRadius = new CornerRadius(7);
        BorderThickness = new Thickness(1); Margin = Rest;
        SnapsToDevicePixels = true;
        Effect = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 2, Opacity = 0.5, Color = Colors.Black };
        MouseLeftButtonUp += (_, e) =>
        {
            if (!Selectable) return;
            Selected = !Selected;
            ApplySelectionChrome();
            Clicked?.Invoke(this);
            e.Handled = true;
        };
        ShowEmpty();
    }

    /// <summary>Make this card pickable (cursor + click). Pass false to make it inert.</summary>
    public void SetSelectable(bool on) { Selectable = on; Cursor = on ? System.Windows.Input.Cursors.Hand : null; if (!on) SetSelected(false); }

    /// <summary>Set the selected state programmatically (e.g. to clear a selection after an action).</summary>
    public void SetSelected(bool on) { Selected = on; ApplySelectionChrome(); }

    private void ApplySelectionChrome()
    {
        if (Selected) { BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00)); BorderThickness = new Thickness(3); Margin = Lifted; }
        else { BorderThickness = new Thickness(1); Margin = Rest; if (Card != null) BorderBrush = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)); }
    }

    public void ShowEmpty()
    {
        Card = null;
        Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
        BorderBrush = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255));
        Child = null;
        ApplySelectionChrome();
    }

    public void ShowBack()
    {
        Card = null;
        Background = new LinearGradientBrush(Color.FromRgb(0x1B, 0x3A, 0x7A), Color.FromRgb(0x0E, 0x1F, 0x45), 45);
        BorderBrush = new SolidColorBrush(Color.FromRgb(0xC9, 0xA2, 0x27));
        Child = new Border { Margin = new Thickness(6), CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromArgb(120, 0xC9, 0xA2, 0x27)) };
        ApplySelectionChrome();
    }

    public void ShowCard(Card c)
    {
        Card = c;
        Background = Brushes.White;
        BorderBrush = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
        var color = c.IsRed ? new SolidColorBrush(Color.FromRgb(0xC1, 0x10, 0x10)) : Brushes.Black;
        var grid = new Grid { Margin = new Thickness(2) };
        var tl = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(3, 1, 0, 0) };
        tl.Children.Add(new TextBlock { Text = c.RankLabel, Foreground = color, FontWeight = FontWeights.Bold, FontSize = 15 });
        tl.Children.Add(new TextBlock { Text = c.Glyph.ToString(), Foreground = color, FontSize = 13 });
        var center = new TextBlock { Text = c.Glyph.ToString(), Foreground = color, FontSize = 32, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(center);
        grid.Children.Add(tl);
        Child = grid;
        ApplySelectionChrome();   // keep the gold highlight/lift if this card is selected
    }
}

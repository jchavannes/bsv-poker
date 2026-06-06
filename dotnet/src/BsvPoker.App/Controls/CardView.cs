using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using BsvPoker.Core;

namespace BsvPoker.App.Controls;

/// <summary>A graphically-rendered playing card (WPF shapes/text, no image assets).</summary>
public sealed class CardView : Border
{
    public CardView()
    {
        Width = 60; Height = 86; CornerRadius = new CornerRadius(7);
        BorderThickness = new Thickness(1); Margin = new Thickness(4);
        SnapsToDevicePixels = true;
        Effect = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 2, Opacity = 0.5, Color = Colors.Black };
        ShowEmpty();
    }

    public void ShowEmpty()
    {
        Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
        BorderBrush = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255));
        Child = null;
    }

    public void ShowBack()
    {
        Background = new LinearGradientBrush(Color.FromRgb(0x1B, 0x3A, 0x7A), Color.FromRgb(0x0E, 0x1F, 0x45), 45);
        BorderBrush = new SolidColorBrush(Color.FromRgb(0xC9, 0xA2, 0x27));
        Child = new Border { Margin = new Thickness(6), CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromArgb(120, 0xC9, 0xA2, 0x27)) };
    }

    public void ShowCard(Card c)
    {
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
    }
}

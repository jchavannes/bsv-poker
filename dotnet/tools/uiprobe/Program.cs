using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// Loads the REAL App.xaml implicit styles, applies them to a panel of controls (ComboBox, Button, TextBox,
// DataGrid), renders OFFSCREEN to a PNG, and exits. Lets the wallet's dark theme be verified with no window
// and no login — exactly the post-login contrast that can't otherwise be screenshotted.
class Program
{
    [System.STAThread]
    static void Main()
    {
        try
        {
            var appXaml = File.ReadAllText(@"D:\claude\Mental Poker\bsv-poker\dotnet\src\BsvPoker.App\App.xaml");
            const string open = "<Application.Resources>", close = "</Application.Resources>";
            int s = appXaml.IndexOf(open), e = appXaml.IndexOf(close);
            string inner = appXaml.Substring(s + open.Length, e - (s + open.Length));
            string rdXaml = "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" + inner + "</ResourceDictionary>";
            var rd = (ResourceDictionary)XamlReader.Parse(rdXaml);

            var root = new Grid { Background = new SolidColorBrush(Color.FromRgb(0x0D, 0x0D, 0x0D)), Width = 560, Height = 380 };
            root.Resources = rd;
            var sp = new StackPanel { Margin = new Thickness(16) };
            sp.Children.Add(new TextBlock { Text = "Theme probe — every control should be DARK with BRIGHT text", Foreground = Brushes.White, FontSize = 14 });
            var cb = new ComboBox { Width = 220, Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            cb.Items.Add("Mainnet"); cb.Items.Add("Testnet"); cb.Items.Add("Regtest"); cb.SelectedIndex = 0;
            sp.Children.Add(new TextBlock { Text = "Network (closed ComboBox):", Foreground = Brushes.Gainsboro, Margin = new Thickness(0, 10, 0, 2) });
            sp.Children.Add(cb);
            sp.Children.Add(new TextBlock { Text = "TextBox:", Foreground = Brushes.Gainsboro, Margin = new Thickness(0, 10, 0, 2) });
            sp.Children.Add(new TextBox { Text = "1000", Width = 220, HorizontalAlignment = HorizontalAlignment.Left });
            var btn = new Button { Content = "Send", Width = 120, Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            sp.Children.Add(btn);
            var dg = new DataGrid { Margin = new Thickness(0, 12, 0, 0), Height = 70, AutoGenerateColumns = false, Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14)) };
            dg.Columns.Add(new DataGridTextColumn { Header = "Outpoint", Width = 160 });
            dg.Columns.Add(new DataGridTextColumn { Header = "Value (sat)", Width = 120 });
            sp.Children.Add(dg);
            root.Children.Add(sp);

            root.Measure(new Size(560, 380));
            root.Arrange(new Rect(0, 0, 560, 380));
            root.UpdateLayout();

            var rtb = new RenderTargetBitmap(560, 380, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(root);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            var outp = Path.Combine(Path.GetTempPath(), "uiprobe.png");
            using var fs = File.Create(outp);
            enc.Save(fs);
            System.Console.WriteLine("OK saved " + outp);
        }
        catch (System.Exception ex) { System.Console.WriteLine("PROBE ERROR: " + ex); }
    }
}

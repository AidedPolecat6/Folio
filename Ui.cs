using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Folio;

internal static class Ui
{
    public static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(131, 185, 236));
    public static readonly Brush Surface = new SolidColorBrush(Color.FromRgb(37, 40, 45));
    public static readonly Brush Canvas = new SolidColorBrush(Color.FromRgb(54, 58, 65));
    public static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(169, 177, 189));
    public static readonly Brush Line = new SolidColorBrush(Color.FromRgb(66, 73, 84));
    public static Border Divider() => new() { Width = 1, Height = 24, Background = Line, Margin = new Thickness(7, 4, 7, 4) };
    public static Border Band(UIElement child, string color) => new() { Child = child, Background = (Brush)new BrushConverter().ConvertFromString(color)!, BorderBrush = Line, BorderThickness = new Thickness(0, 0, 0, 1) };
    public static Button Button(string text, string tip, Action action)
    {
        var b = new Button { Content = text, ToolTip = tip, Padding = new Thickness(9, 5, 9, 5), Margin = new Thickness(2), MinHeight = 30 };
        System.Windows.Automation.AutomationProperties.SetName(b, tip);
        ToolTipService.SetShowOnDisabled(b, true);
        b.Click += (_, _) => Run(action);
        return b;
    }
    public static MenuItem Menu(string text, Action action)
    {
        var m = new MenuItem { Header = text, ToolTip = text.Replace("_", "") };
        m.Click += (_, _) => Run(action);
        return m;
    }
    public static void Run(Action action)
    {
        try { action(); }
        catch (Exception e) { ErrorLog.Write(e, "UI action"); MessageBox.Show(e.Message, "Folio", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
    public static string? Prompt(string title, string initial)
    {
        var box = new TextBox { Text = initial, Margin = new Thickness(12), MinWidth = 320 };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var panel = new StackPanel(); panel.Children.Add(box); panel.Children.Add(buttons);
        var window = new Window { Title = title, Content = panel, SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Application.Current.MainWindow, ShowInTaskbar = false };
        var ok = Button("OK", "Confirm", () => { if (!string.IsNullOrWhiteSpace(box.Text)) window.DialogResult = true; });
        ok.IsDefault = true;
        var cancel = Button("Cancel", "Cancel", () => window.DialogResult = false); cancel.IsCancel = true;
        buttons.Children.Add(ok); buttons.Children.Add(cancel);
        window.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        return window.ShowDialog() == true ? box.Text.Trim() : null;
    }
}

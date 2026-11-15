using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using FileManager.Controls;

namespace FileManager.Views;

/// <summary>Small rounded, chrome-less dialogs built in code.</summary>
public static class Dialogs
{
    public static void Res(AvaloniaObject o, AvaloniaProperty p, string key) => o[!p] = new DynamicResourceExtension(key);

    public static Window Make(string title, Control body, StackPanel buttons, double width = 440, string? icon = null)
    {
        var w = new Window
        {
            Width = width, SizeToContent = SizeToContent.Height, CanResize = false, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Title = title,
            ExtendClientAreaToDecorationsHint = true,
            ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome,
            ExtendClientAreaTitleBarHeightHint = -1,
        };
        Res(w, Window.BackgroundProperty, "SurfaceBrush");

        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        if (icon is not null)
        {
            var ico = new Ico { Kind = icon, Width = 20, Height = 20, VerticalAlignment = VerticalAlignment.Center };
            Res(ico, Ico.ForegroundProperty, "AccentBrush");
            header.Children.Add(ico);
        }
        var tt = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center };
        tt.Classes.Add("header");
        header.Children.Add(tt);

        buttons.Orientation = Orientation.Horizontal;
        buttons.Spacing = 8;
        buttons.HorizontalAlignment = HorizontalAlignment.Right;
        buttons.Margin = new Thickness(0, 6, 0, 0);

        var root = new Border
        {
            Padding = new Thickness(24, 20, 24, 20),
            Background = Brushes.Transparent,
            Child = new StackPanel { Spacing = 14, Children = { header, body, buttons } },
        };
        root.PointerPressed += (_, e) =>
        {
            if (e.Source == root && e.GetCurrentPoint(root).Properties.IsLeftButtonPressed) w.BeginMoveDrag(e);
        };
        w.Content = root;
        w.KeyDown += (_, e) => { if (e.Key == Key.Escape) w.Close(); };
        return w;
    }

    public static Button Btn(string text, string? cls = null)
    {
        var b = new Button { Content = text, MinWidth = 84, HorizontalContentAlignment = HorizontalAlignment.Center };
        if (cls is not null) b.Classes.Add(cls);
        return b;
    }

    public static TextBlock Text(string text, bool sub = false)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        if (sub) t.Classes.Add("sub");
        return t;
    }

    public static async Task<string?> Prompt(Window owner, string title, string label, string initial, bool selectStem = false)
    {
        string? result = null;
        var box = new TextBox { Text = initial };
        var body = new StackPanel { Spacing = 6, Children = { Text(label, true), box } };
        var buttons = new StackPanel();
        var w = Make(title, body, buttons, icon: "rename");
        var cancel = Btn("Cancel");
        var ok = Btn("OK", "accent");
        ok.IsDefault = true;
        cancel.Click += (_, _) => w.Close();
        ok.Click += (_, _) => { result = box.Text; w.Close(); };
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { result = box.Text; w.Close(); } };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        w.Opened += (_, _) =>
        {
            box.Focus();
            box.SelectionStart = 0;
            var dot = initial.LastIndexOf('.');
            box.SelectionEnd = selectStem && dot > 0 ? dot : initial.Length;
        };
        await w.ShowDialog(owner);
        return result;
    }

    public static async Task<bool> Confirm(Window owner, string title, string message, string okText, bool danger = false, string? detail = null)
    {
        bool result = false;
        var body = new StackPanel { Spacing = 8, Children = { Text(message) } };
        if (detail is not null) body.Children.Add(Text(detail, true));
        var buttons = new StackPanel();
        var w = Make(title, body, buttons, icon: danger ? "warning" : "info");
        var cancel = Btn("Cancel");
        var ok = Btn(okText, danger ? "danger" : "accent");
        cancel.Click += (_, _) => w.Close();
        ok.Click += (_, _) => { result = true; w.Close(); };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        w.Opened += (_, _) => cancel.Focus();
        await w.ShowDialog(owner);
        return result;
    }

    public static async Task Info(Window owner, string title, string message)
    {
        var buttons = new StackPanel();
        var w = Make(title, Text(message), buttons, icon: "info");
        var ok = Btn("OK", "accent");
        ok.Click += (_, _) => w.Close();
        buttons.Children.Add(ok);
        await w.ShowDialog(owner);
    }
}

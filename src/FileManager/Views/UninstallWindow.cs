using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform;
using FileManager.Shared;

namespace FileManager.Views;

/// <summary>Shown when Windows "Apps &amp; features" runs FileManager.exe --uninstall.</summary>
public sealed class UninstallWindow : Window
{
    public UninstallWindow()
    {
        Title = "Uninstall File Manager";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaTitleBarHeightHint = 36;
        try { Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://FileManager/Assets/icon.ico"))); } catch { }

        var title = new TextBlock { Text = "Uninstall File Manager?" };
        title.Classes.Add("header");
        var info = InstallCore.Detect();
        var text = Dialogs.Text($"This removes the app from {info?.Dir ?? InstallCore.DefaultDir}, plus its shortcuts. Your own files are not touched.", true);
        var keep = new CheckBox { Content = "Also remove my settings (theme, font, last folders)" };
        var status = Dialogs.Text("", true);

        var cancel = Dialogs.Btn("Cancel");
        var go = Dialogs.Btn("Uninstall", "danger");
        cancel.Click += (_, _) => Close();
        go.Click += async (_, _) =>
        {
            go.IsEnabled = cancel.IsEnabled = false;
            status.Text = "Removing…";
            bool remove = keep.IsChecked == true;
            try { await Task.Run(() => InstallCore.Uninstall(remove)); status.Text = "File Manager was removed."; }
            catch (Exception ex) { status.Text = "Uninstall failed: " + ex.Message; }
            cancel.Content = "Close";
            cancel.IsEnabled = true;
        };

        Content = new StackPanel
        {
            Margin = new Thickness(24, 30, 24, 20), Spacing = 14,
            Children =
            {
                title, text, keep, status,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, go } },
            },
        };
    }
}

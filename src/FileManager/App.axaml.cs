using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using FileManager.Services;
using FileManager.Views;

namespace FileManager;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        Settings.Load();
        ThemeService.Apply(Settings.Current);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = Program.Args.Contains("--uninstall", StringComparer.OrdinalIgnoreCase)
                ? new UninstallWindow()
                : new MainWindow();
        }
        base.OnFrameworkInitializationCompleted();
    }
}

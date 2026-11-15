using System.Diagnostics;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using FileManager.Shared;

namespace FileManager.Setup;

public partial class SetupWindow : Window
{
    readonly string _version;
    string? _installedExe;

    public SetupWindow()
    {
        InitializeComponent();
        var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
        _version = $"{v.Major}.{v.Minor}.{v.Build}";
        VersionText.Text = "Version " + _version;
        try { Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://FileManager-Setup/Assets/icon.ico"))); } catch { }

        var installed = InstallCore.Detect();
        DirBox.Text = installed?.Dir ?? InstallCore.DefaultDir;

        if (installed is not null)
        {
            ManageText.Text = $"Version {installed.Version} is installed in {installed.Dir}. This setup contains version {_version}.";
            ReinstallButton.Content = installed.Version == _version ? "Reinstall" : "Update";
            Show(ManagePanel);
            if (Program.Args.Contains("--uninstall", StringComparer.OrdinalIgnoreCase)) RemoveSettingsBox.Focus();
        }
        else Show(InstallPanel);
    }

    void Show(Control panel)
    {
        foreach (var p in new Control[] { InstallPanel, ManagePanel, ProgressPanel, DonePanel })
            p.IsVisible = p == panel;
    }

    static Stream Payload() =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.exe")
        ?? throw new InvalidOperationException("This setup was built without the application payload. Run build/build.ps1 to create a complete installer.");

    async void Install_Click(object? s, RoutedEventArgs e) => await Install(DirBox.Text?.Trim() ?? "", LaunchBox.IsChecked == true);

    async void Reinstall_Click(object? s, RoutedEventArgs e) => await Install(InstallCore.Detect()?.Dir ?? DirBox.Text?.Trim() ?? "", false);

    async Task Install(string dir, bool launch)
    {
        if (string.IsNullOrWhiteSpace(dir)) return;
        bool desktop = DesktopBox.IsChecked == true, start = StartBox.IsChecked == true;
        ProgressTitle.Text = "Installing…";
        Show(ProgressPanel);
        var progress = new Progress<(double Value, string Text)>(p => { Bar.Value = p.Value; ProgressText.Text = p.Text; });
        try
        {
            await Task.Run(() => InstallCore.Install(dir, _version, Payload, desktop, start, progress));
            _installedExe = Path.Combine(dir, InstallCore.ExeName);
            if (launch) { Launch(); Close(); return; }
            DoneTitle.Text = "File Manager is ready";
            DoneText.Text = $"Installed to {dir}. You can find it in the Start menu. To remove it later, run this setup again or use Windows Settings → Apps.";
            LaunchButton.IsVisible = true;
        }
        catch (Exception ex)
        {
            DoneTitle.Text = "Installation failed";
            DoneText.Text = ex.Message;
            LaunchButton.IsVisible = false;
        }
        Show(DonePanel);
    }

    async void Uninstall_Click(object? s, RoutedEventArgs e)
    {
        bool removeSettings = RemoveSettingsBox.IsChecked == true;
        ProgressTitle.Text = "Uninstalling…";
        ProgressText.Text = "Removing files and shortcuts…";
        Bar.IsIndeterminate = true;
        Show(ProgressPanel);
        try
        {
            await Task.Run(() => InstallCore.Uninstall(removeSettings));
            DoneTitle.Text = "File Manager was removed";
            DoneText.Text = "The app, its shortcuts and its Windows Settings entry are gone." + (removeSettings ? " Your settings were removed too." : " Your settings were kept in case you reinstall.");
        }
        catch (Exception ex)
        {
            DoneTitle.Text = "Uninstall failed";
            DoneText.Text = ex.Message;
        }
        Bar.IsIndeterminate = false;
        LaunchButton.IsVisible = false;
        Show(DonePanel);
    }

    async void Browse_Click(object? s, RoutedEventArgs e)
    {
        var res = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose install folder" });
        var p = res.FirstOrDefault()?.TryGetLocalPath();
        if (p is not null) DirBox.Text = Path.Combine(p, InstallCore.AppName);
    }

    void Launch_Click(object? s, RoutedEventArgs e) { Launch(); Close(); }

    void Launch()
    {
        try { if (_installedExe is not null) Process.Start(new ProcessStartInfo(_installedExe) { UseShellExecute = true }); } catch { }
    }

    void Cancel_Click(object? s, RoutedEventArgs e) => Close();

    void Drag(object? s, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
    }
}

using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FileManager.Models;
using FileManager.Services;

namespace FileManager.Views;

public partial class DashboardView : UserControl
{
    CancellationTokenSource? _cts;
    readonly DispatcherTimer _progress;
    StorageScanner? _scanner;
    readonly Stopwatch _sw = new();

    public DashboardView()
    {
        InitializeComponent();
        _progress = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _progress.Tick += (_, _) =>
        {
            if (_scanner is { } s)
                StatusText.Text = $"Scanning… {s.FilesScanned:N0} files · {Fmt.Size(s.BytesScanned)} · {_sw.Elapsed.TotalSeconds:0} s";
        };
    }

    public void Activate() => LoadDrives();

    void LoadDrives()
    {
        var cards = new List<DriveCard>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady || d.TotalSize == 0) continue;
                var used = d.TotalSize - d.AvailableFreeSpace;
                var label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? (d.DriveType == DriveType.Fixed ? "Local Disk" : d.DriveType.ToString()) : d.VolumeLabel;
                cards.Add(new DriveCard
                {
                    Title = $"{label} ({d.Name.TrimEnd('\\')})",
                    Root = d.RootDirectory.FullName,
                    UsedPercent = used * 100.0 / d.TotalSize,
                    FreeText = $"{Fmt.Size(used)} used · {Fmt.Size(d.AvailableFreeSpace)} free of {Fmt.Size(d.TotalSize)}",
                });
            }
            catch { }
        }
        DriveList.ItemsSource = cards;
    }

    void Drive_Click(object? s, RoutedEventArgs e)
    {
        if (s is Button { Tag: string root }) Scan(root);
    }

    async void ScanFolder_Click(object? s, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;
        var res = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Scan folder" });
        var p = res.FirstOrDefault()?.TryGetLocalPath();
        if (p is not null) Scan(p);
    }

    void Stop_Click(object? s, RoutedEventArgs e) => _cts?.Cancel();

    public async void Scan(string root)
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        var scanner = _scanner = new StorageScanner();
        Tree.ItemsSource = null; TopList.ItemsSource = null; TypeList.ItemsSource = null;
        TreeEmpty.IsVisible = false;
        Busy.IsVisible = true; StopButton.IsVisible = true;
        _sw.Restart();
        _progress.Start();

        DirNode node;
        try { node = await Task.Run(() => scanner.Scan(root, cts.Token)); }
        catch (Exception ex) { StatusText.Text = "Scan failed: " + ex.Message; Done(); return; }
        if (_scanner != scanner) return;
        _sw.Stop();
        Done();

        node.IsExpanded = true;
        Tree.ItemsSource = new[] { node };

        var top = scanner.TopFiles().Select(t => new FileItem
        {
            Name = Path.GetFileName(t.Path), FullPath = t.Path, Size = t.Size,
            Extension = Path.GetExtension(t.Path).ToLowerInvariant(),
        }).ToList();
        TopList.ItemsSource = top;
        foreach (var f in top) IconCache.Request(f, () => _scanner != scanner);

        var types = scanner.Types.OrderByDescending(kv => kv.Value.Size).Take(40).ToList();
        long max = types.Count > 0 ? Math.Max(1, types[0].Value.Size) : 1;
        TypeList.ItemsSource = types.Select(kv => new TypeStat
        {
            Ext = kv.Key, Size = kv.Value.Size, Count = kv.Value.Count, Percent = kv.Value.Size * 100.0 / max,
        }).ToList();

        StatusText.Text = (cts.IsCancellationRequested ? "Stopped (partial result) · " : "")
            + $"{node.FullPath} · {Fmt.Size(node.Size)} in {node.Files:N0} files and {node.Folders:N0} folders · {_sw.Elapsed.TotalSeconds:0.0} s"
            + (scanner.DeniedFolders > 0 ? $" · {scanner.DeniedFolders:N0} folders not accessible (run as admin to include them)" : "");
    }

    void Done()
    {
        _progress.Stop();
        Busy.IsVisible = false;
        StopButton.IsVisible = false;
        _cts = null;
    }

    DirNode? SelectedNode => Tree.SelectedItem as DirNode;

    void Tree_DoubleTapped(object? s, TappedEventArgs e)
    {
        if ((e.Source as Control)?.DataContext is DirNode n && (n.Children.Count == 0 || n.IsFilesNode))
            MainWindow.Current.OpenInBrowser(n.FullPath);
    }

    void TreeMenu_Click(object? s, RoutedEventArgs e)
    {
        if (SelectedNode is not { } n || s is not MenuItem { Tag: string cmd }) return;
        switch (cmd)
        {
            case "open": MainWindow.Current.OpenInBrowser(n.FullPath); break;
            case "explorer": Shell.Reveal(n.FullPath); break;
            case "scan": Scan(n.FullPath); break;
            case "dups": MainWindow.Current.ShowDuplicatesFor(n.FullPath); break;
        }
    }

    void TopList_DoubleTapped(object? s, TappedEventArgs e)
    {
        if ((e.Source as Control)?.DataContext is FileItem f)
            MainWindow.Current.OpenInBrowser(f.Folder, f.FullPath);
    }
}

using System.Collections.Concurrent;
using System.Diagnostics;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FileManager.Models;
using FileManager.Services;

namespace FileManager.Views;

public partial class SearchView : UserControl
{
    readonly AvaloniaList<FileItem> _results = [];
    readonly ConcurrentQueue<FileItem> _incoming = new();
    readonly DispatcherTimer _drain;
    CancellationTokenSource? _cts;
    string _root = "";
    bool _rootChosen;
    int _gen;
    readonly Stopwatch _sw = new();

    public SearchView()
    {
        InitializeComponent();
        Results.ItemsSource = _results;
        _drain = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _drain.Tick += (_, _) => Drain();
    }

    public void Activate(string currentFolder, string? query)
    {
        if (!_rootChosen) SetRoot(currentFolder);
        if (query is not null) { QueryBox.Text = query; Start(); }
        else Dispatcher.UIThread.Post(() => { QueryBox.Focus(); QueryBox.SelectAll(); });
    }

    void SetRoot(string root)
    {
        _root = root;
        RootText.Text = Path.GetFileName(Path.TrimEndingDirectorySeparator(root)) is { Length: > 0 } n ? n : root;
        ToolTip.SetTip(RootButton, root);
    }

    async void Root_Click(object? s, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;
        var start = await top.StorageProvider.TryGetFolderFromPathAsync(_root);
        var res = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Search in…", SuggestedStartLocation = start });
        var p = res.FirstOrDefault()?.TryGetLocalPath();
        if (p is null) return;
        _rootChosen = true;
        AllDrivesBox.IsChecked = false;
        SetRoot(p);
    }

    void Query_KeyDown(object? s, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { Start(); e.Handled = true; }
        else if (e.Key == Key.Escape) { Stop(); e.Handled = true; }
    }

    void Go_Click(object? s, RoutedEventArgs e)
    {
        if (_cts is not null) Stop(); else Start();
    }

    void Stop()
    {
        _cts?.Cancel();
    }

    async void Start()
    {
        var text = QueryBox.Text?.Trim() ?? "";
        int typeIdx = TypeBox.SelectedIndex;
        if (text.Length == 0 && typeIdx == 0 && SizeBox.SelectedIndex == 0) { StatusText.Text = "Type something to search for."; return; }

        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        int gen = ++_gen;
        _results.Clear();
        while (_incoming.TryDequeue(out _)) { }

        var roots = AllDrivesBox.IsChecked == true
            ? DriveInfo.GetDrives().Where(d => { try { return d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable; } catch { return false; } }).Select(d => d.RootDirectory.FullName).ToList()
            : [_root];

        var q = new SearchQuery
        {
            Text = text, Kind = KindBox.SelectedIndex, Extensions = FileTypes.Category(typeIdx),
            MinSize = SizeBox.SelectedIndex switch { 1 => 1L << 20, 2 => 100L << 20, 3 => 1L << 30, _ => 0 },
            Roots = roots, IncludeHidden = HiddenBox.IsChecked == true,
        };

        GoButton.Content = "Stop";
        Busy.IsVisible = true;
        StatusText.Text = "Searching…";
        _sw.Restart();
        _drain.Start();
        int total = 0;
        try { total = await SearchEngine.RunAsync(q, _incoming, cts.Token); }
        catch (OperationCanceledException) { }
        _sw.Stop();
        if (gen != _gen) return;
        _drain.Stop();
        Drain();
        _cts = null;
        GoButton.Content = "Search";
        Busy.IsVisible = false;
        var where = AllDrivesBox.IsChecked == true ? "all drives" : _root;
        StatusText.Text = cts.IsCancellationRequested
            ? $"Stopped · {_results.Count:N0} results"
            : $"{_results.Count:N0} results in {_sw.Elapsed.TotalSeconds:0.0} s · {where}" + (total >= q.Limit ? $" (limited to {q.Limit:N0})" : "");
    }

    void Drain()
    {
        if (_incoming.IsEmpty) return;
        var batch = new List<FileItem>();
        while (batch.Count < 3000 && _incoming.TryDequeue(out var it)) batch.Add(it);
        _results.AddRange(batch);
        int gen = _gen;
        foreach (var it in batch) IconCache.Request(it, () => gen != _gen);
        if (_cts is not null) StatusText.Text = $"Searching… {_results.Count:N0} found";
    }

    List<FileItem> Selected => Results.SelectedItems?.Cast<FileItem>().ToList() ?? [];

    void Results_DoubleTapped(object? s, TappedEventArgs e)
    {
        if ((e.Source as Control)?.DataContext is not FileItem it) return;
        if (it.IsDirectory) MainWindow.Current.OpenInBrowser(it.FullPath);
        else Shell.Open(it.FullPath);
    }

    async void Menu_Click(object? s, RoutedEventArgs e)
    {
        var sel = Selected;
        if (sel.Count == 0 || s is not MenuItem { Tag: string cmd }) return;
        var first = sel[0];
        switch (cmd)
        {
            case "open":
                foreach (var it in sel.Take(10)) Shell.Open(it.FullPath);
                break;
            case "reveal":
                MainWindow.Current.OpenInBrowser(first.Folder, first.FullPath);
                break;
            case "explorer":
                Shell.Reveal(first.FullPath);
                break;
            case "copypath":
                var cb = TopLevel.GetTopLevel(this)?.Clipboard;
                if (cb is not null) await cb.SetTextAsync(string.Join(Environment.NewLine, sel.Select(i => i.FullPath)));
                break;
            case "delete":
                if (!await Dialogs.Confirm(MainWindow.Current, "Move to Recycle Bin?", $"{sel.Count:N0} item(s) will be moved to the Recycle Bin.", "Move to Recycle Bin", true)) return;
                try
                {
                    await FileOps.Delete(sel.Select(i => i.FullPath).ToList(), true);
                    foreach (var it in sel.Where(i => !File.Exists(i.FullPath) && !Directory.Exists(i.FullPath))) _results.Remove(it);
                }
                catch (Exception ex) { await Dialogs.Info(MainWindow.Current, "Couldn't delete", ex.Message); }
                break;
        }
    }
}

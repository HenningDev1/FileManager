using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FileManager.Models;
using FileManager.Services;

namespace FileManager.Views;

public partial class DuplicatesView : UserControl
{
    readonly AvaloniaList<object> _rows = [];
    List<DupGroup> _groups = [];
    CancellationTokenSource? _cts;
    string _root = "";
    bool _rootChosen;

    public DuplicatesView()
    {
        InitializeComponent();
        List.ItemsSource = _rows;
    }

    public void SuggestRoot(string folder)
    {
        if (!_rootChosen) SetRoot(folder);
    }

    public void ForceRoot(string folder)
    {
        _rootChosen = true;
        SetRoot(folder);
    }

    void SetRoot(string root)
    {
        _root = root;
        RootText.Text = root;
        ToolTip.SetTip(RootButton, root);
    }

    async void Root_Click(object? s, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;
        var start = await top.StorageProvider.TryGetFolderFromPathAsync(_root);
        var res = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Find duplicates in…", SuggestedStartLocation = start });
        var p = res.FirstOrDefault()?.TryGetLocalPath();
        if (p is null) return;
        _rootChosen = true;
        SetRoot(p);
    }

    async void Scan_Click(object? s, RoutedEventArgs e)
    {
        if (_cts is not null) { _cts.Cancel(); return; }
        if (!Directory.Exists(_root)) { StatusText.Text = "Choose a folder first."; return; }

        var cts = _cts = new CancellationTokenSource();
        _rows.Clear();
        _groups = [];
        SummaryBar.IsVisible = false;
        ScanButton.Content = "Stop";
        Busy.IsVisible = true;
        long min = MinSizeBox.SelectedIndex switch { 1 => 100L << 10, 2 => 1L << 20, 3 => 10L << 20, 4 => 100L << 20, _ => 1 };
        var started = DateTime.Now;
        try
        {
            _groups = await DuplicateFinder.FindAsync([_root], min, NameMatchBox.IsChecked == true, HiddenBox.IsChecked == true,
                msg => Dispatcher.UIThread.Post(() => { if (_cts == cts) StatusText.Text = msg; }), cts.Token);
            Populate();
            StatusText.Text = $"Finished in {(DateTime.Now - started).TotalSeconds:0.0} s · {_root}";
        }
        catch (OperationCanceledException) { StatusText.Text = "Scan stopped."; }
        catch (Exception ex) { StatusText.Text = "Scan failed: " + ex.Message; }
        finally
        {
            _cts = null;
            ScanButton.Content = "Scan";
            Busy.IsVisible = false;
        }
    }

    void Populate()
    {
        _rows.Clear();
        var rows = new List<object>();
        foreach (var g in _groups)
        {
            var first = g.Files[0];
            rows.Add(new DupHeader
            {
                Group = g,
                Title = g.IsNameOnly
                    ? $"Similar names: {first.Name}"
                    : $"{g.Files.Count} identical copies of {first.Name}",
                Subtitle = g.IsNameOnly
                    ? "Copy-style names but different content — review manually, nothing is pre-selected"
                    : $"{Fmt.Size(g.Size)} each · {Fmt.Size(g.Wasted)} could be freed",
            });
            foreach (var f in g.Files)
            {
                f.IsChecked = false;
                rows.Add(f);
                IconCache.Request(f.FullPath, false, false, Path.GetExtension(f.FullPath).ToLowerInvariant(), b => f.Icon = b, () => false);
            }
        }
        _rows.AddRange(rows);
        SummaryBar.IsVisible = _groups.Count > 0;
        int content = _groups.Count(g => !g.IsNameOnly);
        SummaryText.Text = _groups.Count == 0
            ? "No duplicates found"
            : $"{content:N0} duplicate groups · {Fmt.Size(_groups.Sum(g => g.Wasted))} reclaimable"
              + (_groups.Count > content ? $" · {_groups.Count - content:N0} similar-name groups" : "");
        if (_groups.Count == 0) { SummaryBar.IsVisible = true; DeleteButton.IsEnabled = false; }
        UpdateSelection();
    }

    void Check_Changed(object? s, RoutedEventArgs e) => UpdateSelection();

    void UpdateSelection()
    {
        var sel = _groups.SelectMany(g => g.Files).Where(f => f.IsChecked).ToList();
        SelectionText.Text = sel.Count == 0
            ? "Tick the copies you want to remove, or use “Select suggested”."
            : $"{sel.Count:N0} files selected · {Fmt.Size(sel.Sum(f => f.Size))}";
        DeleteButton.IsEnabled = sel.Count > 0;
    }

    void SelectSuggested_Click(object? s, RoutedEventArgs e)
    {
        foreach (var f in _groups.SelectMany(g => g.Files)) f.IsChecked = f.Suggested;
        UpdateSelection();
    }

    void SelectNone_Click(object? s, RoutedEventArgs e)
    {
        foreach (var f in _groups.SelectMany(g => g.Files)) f.IsChecked = false;
        UpdateSelection();
    }

    async void Delete_Click(object? s, RoutedEventArgs e)
    {
        var sel = _groups.SelectMany(g => g.Files).Where(f => f.IsChecked).ToList();
        if (sel.Count == 0) return;
        int wipedGroups = _groups.Count(g => !g.IsNameOnly && g.Files.All(f => f.IsChecked));
        var detail = wipedGroups > 0
            ? $"Warning: in {wipedGroups} group(s) every copy is selected, so no copy would remain outside the Recycle Bin."
            : "Files go to the Recycle Bin, so you can restore them.";
        if (!await Dialogs.Confirm(MainWindow.Current, "Move duplicates to Recycle Bin?",
                $"{sel.Count:N0} file(s), {Fmt.Size(sel.Sum(f => f.Size))} in total, will be moved to the Recycle Bin.",
                "Move to Recycle Bin", true, detail))
            return;

        StatusText.Text = "Moving to Recycle Bin…";
        try { await FileOps.Delete(sel.Select(f => f.FullPath).ToList(), true); }
        catch (Exception ex) { await Dialogs.Info(MainWindow.Current, "Couldn't remove everything", ex.Message); }

        int removed = 0;
        foreach (var f in sel.Where(f => !File.Exists(f.FullPath)))
        {
            f.Group.Files.Remove(f);
            removed++;
        }
        _groups = _groups.Where(g => g.Files.Count > 1).ToList();
        Populate();
        StatusText.Text = $"Moved {removed:N0} file(s) to the Recycle Bin.";
    }

    void Open_Click(object? s, RoutedEventArgs e)
    {
        if (s is Button { Tag: string p }) Shell.Open(p);
    }

    void Reveal_Click(object? s, RoutedEventArgs e)
    {
        if (s is Button { Tag: string p }) MainWindow.Current.OpenInBrowser(Path.GetDirectoryName(p)!, p);
    }
}

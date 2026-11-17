using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FileManager.Models;
using FileManager.Services;

namespace FileManager.Views;

public partial class FilePane : UserControl
{
    public const string ThisPC = "::thispc";
    const string DragFormat = "x-filemanager-paths";

    public string CurrentPath { get; private set; } = ThisPC;
    public bool IsGrid { get; private set; }
    public int ItemCount => _view.Count;
    public bool CanGoBack => _back.Count > 0;
    public bool CanGoForward => _fwd.Count > 0;

    public event Action<FilePane>? Activated;
    public event Action<FilePane>? SelectionChangedEx;
    public event Action<FilePane>? PathChanged;
    public event Action<FilePane, string>? CommandRequested;
    public event Action<FilePane, FileItem>? OpenRequested;
    public event Action<FilePane, List<string>, string, bool>? FilesDropped;

    readonly List<string> _back = [], _fwd = [];
    List<FileItem> _all = [], _view = [];
    string _filter = "", _sortKey = "Name";
    bool _asc = true, _initialized;
    int _gen;
    FileSystemWatcher? _watcher;
    readonly DispatcherTimer _reloadTimer;
    Point? _pressPoint;
    FileItem? _pressItem;

    public FilePane()
    {
        InitializeComponent();
        _reloadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        _reloadTimer.Tick += async (_, _) => { _reloadTimer.Stop(); await Reload(true); };

        AddHandler(PointerPressedEvent, (_, _) => Activated?.Invoke(this), RoutingStrategies.Tunnel, true);
        AddHandler(GotFocusEvent, (_, _) => Activated?.Invoke(this), RoutingStrategies.Bubble, true);

        foreach (var lb in new[] { ListView, IconView })
        {
            lb.SelectionChanged += (_, _) => SelectionChangedEx?.Invoke(this);
            lb.AddHandler(PointerPressedEvent, OnListPressed, RoutingStrategies.Tunnel, true);
            lb.AddHandler(PointerMovedEvent, OnListMoved, RoutingStrategies.Tunnel, true);
            lb.AddHandler(PointerReleasedEvent, (_, _) => { _pressPoint = null; }, RoutingStrategies.Tunnel, true);
        }
        SizeChanged += (_, e) => AdaptColumns(e.NewSize.Width);
        // Keep the current folder (last breadcrumb) visible whenever the bar or its content resizes.
        Crumbs.SizeChanged += (_, _) => CrumbScroll.Offset = new Vector(double.MaxValue, 0);
        CrumbScroll.SizeChanged += (_, _) => CrumbScroll.Offset = new Vector(double.MaxValue, 0);
        ListArea.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        ListArea.AddHandler(DragDrop.DropEvent, OnDrop);
    }

    ListBox ActiveList => IsGrid ? IconView : ListView;

    public List<FileItem> SelectedItems =>
        ActiveList.SelectedItems?.Cast<FileItem>().ToList() ?? [];

    public bool IsActive { set => Card.Classes.Set("active", value); }

    /// <summary>Shows back/forward/up buttons inside the pane (used in dual-pane mode).</summary>
    public bool ShowNav { set => PaneNav.IsVisible = value; }

    /// <summary>Drops less important columns as the pane narrows so file names stay readable.</summary>
    void AdaptColumns(double width)
    {
        Card.Classes.Set("compact", width < 820);
        Card.Classes.Set("noKind", width < 700);
        Card.Classes.Set("noDate", width < 500);
        Card.Classes.Set("noSize", width < 330);
        CountText.IsVisible = width >= 460;
    }

    void UpdatePaneNav()
    {
        PaneBack.IsEnabled = CanGoBack;
        PaneFwd.IsEnabled = CanGoForward;
        PaneUp.IsEnabled = CurrentPath != ThisPC;
    }

    void PaneBack_Click(object? s, RoutedEventArgs e) { Activated?.Invoke(this); GoBack(); }
    void PaneFwd_Click(object? s, RoutedEventArgs e) { Activated?.Invoke(this); GoForward(); }
    void PaneUp_Click(object? s, RoutedEventArgs e) { Activated?.Invoke(this); GoUp(); }

    // ---------------- Navigation ----------------

    public async void Navigate(string path, bool record = true, string? select = null)
    {
        if (path != ThisPC)
        {
            try { path = Path.GetFullPath(path); } catch { return; }
            if (!Directory.Exists(path))
            {
                MainWindow.Current?.Status($"Folder not found: {path}");
                return;
            }
        }
        if (record && _initialized && !string.Equals(CurrentPath, path, StringComparison.OrdinalIgnoreCase))
        {
            _back.Add(CurrentPath);
            _fwd.Clear();
        }
        _initialized = true;
        CurrentPath = path;
        _filter = "";
        BuildCrumbs();
        UpdatePaneNav();
        SetupWatcher();
        PathChanged?.Invoke(this);
        await Reload(false, select);
    }

    public void GoBack()
    {
        if (_back.Count == 0) return;
        _fwd.Add(CurrentPath);
        var p = _back[^1]; _back.RemoveAt(_back.Count - 1);
        Navigate(p, false);
    }

    public void GoForward()
    {
        if (_fwd.Count == 0) return;
        _back.Add(CurrentPath);
        var p = _fwd[^1]; _fwd.RemoveAt(_fwd.Count - 1);
        Navigate(p, false);
    }

    public void GoUp()
    {
        if (CurrentPath == ThisPC) return;
        var parent = Directory.GetParent(CurrentPath);
        var from = CurrentPath;
        Navigate(parent?.FullName ?? ThisPC, true, from);
    }

    public async Task Reload(bool keepSelection = true, string? select = null)
    {
        int gen = ++_gen;
        var path = CurrentPath;
        bool hidden = Settings.Current.ShowHidden;
        var keep = keepSelection ? SelectedItems.Select(i => i.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase) : null;
        if (select is not null) (keep ??= new(StringComparer.OrdinalIgnoreCase)).Add(select);

        string? error = null;
        var items = await Task.Run(() =>
        {
            try { return path == ThisPC ? LoadDrives() : LoadDir(path, hidden); }
            catch (Exception ex) { error = ex is UnauthorizedAccessException ? "You don't have access to this folder" : ex.Message; return []; }
        });
        if (gen != _gen) return;

        _all = items;
        Apply(keep);
        EmptyText.Text = error ?? (_filter.Length > 0 ? "No matches" : "This folder is empty");
        if (keep is { Count: > 0 } && select is not null) ScrollTo(select);

        foreach (var it in items)
        {
            IconCache.Request(it, () => gen != _gen);
            if (IsGrid && !it.IsDirectory && FileTypes.HasThumbnail(it.Extension))
                IconCache.RequestThumb(it, () => gen != _gen);
        }
    }

    static List<FileItem> LoadDir(string path, bool hidden)
    {
        var opts = new EnumerationOptions
        {
            IgnoreInaccessible = true, RecurseSubdirectories = false,
            AttributesToSkip = hidden ? 0 : FileAttributes.Hidden | FileAttributes.System,
        };
        var list = new List<FileItem>();
        foreach (var fsi in new DirectoryInfo(path).EnumerateFileSystemInfos("*", opts))
        {
            bool dir = fsi is DirectoryInfo;
            var ext = dir ? "" : fsi.Extension.ToLowerInvariant();
            list.Add(new FileItem
            {
                Name = fsi.Name, FullPath = fsi.FullName, IsDirectory = dir,
                Size = dir ? 0 : ((FileInfo)fsi).Length, Modified = fsi.LastWriteTime,
                Extension = ext, TypeName = dir ? "Folder" : Shell.TypeName(ext),
            });
        }
        return list;
    }

    static List<FileItem> LoadDrives()
    {
        var list = new List<FileItem>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady) continue;
                var label = string.IsNullOrWhiteSpace(d.VolumeLabel)
                    ? d.DriveType switch { DriveType.Fixed => "Local Disk", DriveType.Removable => "USB Drive", DriveType.Network => "Network Drive", DriveType.CDRom => "CD Drive", _ => "Drive" }
                    : d.VolumeLabel;
                list.Add(new FileItem
                {
                    Name = $"{label} ({d.Name.TrimEnd('\\')})", FullPath = d.RootDirectory.FullName, IsDirectory = true, IsDrive = true,
                    TypeName = d.DriveFormat, Size = d.TotalSize,
                    Detail = $"{Fmt.Size(d.AvailableFreeSpace)} free of {Fmt.Size(d.TotalSize)}",
                });
            }
            catch { }
        }
        return list;
    }

    // ---------------- Filtering & sorting ----------------

    public void SetFilter(string filter)
    {
        _filter = filter.Trim();
        Apply(SelectedItems.Select(i => i.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase));
    }

    void Apply(HashSet<string>? select)
    {
        IEnumerable<FileItem> q = _all;
        if (_filter.Length > 0)
        {
            var terms = _filter.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            q = q.Where(i => terms.All(t => i.Name.Contains(t, StringComparison.OrdinalIgnoreCase)));
        }
        Func<FileItem, IComparable> key = _sortKey switch
        {
            "Date" => i => i.Modified,
            "Type" => i => i.TypeName,
            "Size" => i => i.Size,
            _ => i => i.Name,
        };
        var dirsFirst = q.OrderByDescending(i => i.IsDirectory);
        var sorted = _sortKey == "Name"
            ? (_asc ? dirsFirst.ThenBy(i => i.Name, NaturalComparer.Instance) : dirsFirst.ThenByDescending(i => i.Name, NaturalComparer.Instance))
            : (_asc ? dirsFirst.ThenBy(key) : dirsFirst.ThenByDescending(key));
        _view = sorted.ToList();

        ListView.ItemsSource = _view;
        IconView.ItemsSource = _view;
        if (select is { Count: > 0 })
        {
            foreach (var it in _view)
                if (select.Contains(it.FullPath)) ActiveList.SelectedItems?.Add(it);
        }
        EmptyState.IsVisible = _view.Count == 0;
        CountText.Text = _view.Count == 1 ? "1 item" : $"{_view.Count:N0} items";
        UpdateHeaders();
        SelectionChangedEx?.Invoke(this);
    }

    void UpdateHeaders()
    {
        string Arrow(string k) => _sortKey == k ? (_asc ? "  ↑" : "  ↓") : "";
        HdrName.Content = "Name" + Arrow("Name");
        HdrDate.Content = "Date Modified" + Arrow("Date");
        HdrType.Content = "Kind" + Arrow("Type");
        HdrSize.Content = "Size" + Arrow("Size");
    }

    void Sort_Click(object? sender, RoutedEventArgs e)
    {
        var k = (string)((Button)sender!).Tag!;
        if (_sortKey == k) _asc = !_asc; else { _sortKey = k; _asc = k is "Name" or "Type"; }
        Apply(SelectedItems.Select(i => i.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase));
    }

    // ---------------- Selection helpers ----------------

    public void SelectAll() => ActiveList.SelectAll();
    public void ClearSelection() => ActiveList.SelectedItems?.Clear();
    public void FocusList() => ActiveList.Focus();

    public void SelectPath(string path)
    {
        var it = _view.FirstOrDefault(i => string.Equals(i.FullPath, path, StringComparison.OrdinalIgnoreCase));
        if (it is null) return;
        ActiveList.SelectedItems?.Clear();
        ActiveList.SelectedItems?.Add(it);
        ActiveList.ScrollIntoView(it);
    }

    void ScrollTo(string path)
    {
        var it = _view.FirstOrDefault(i => string.Equals(i.FullPath, path, StringComparison.OrdinalIgnoreCase));
        if (it is not null) Dispatcher.UIThread.Post(() => ActiveList.ScrollIntoView(it), DispatcherPriority.Background);
    }

    public void SetGrid(bool grid)
    {
        if (IsGrid == grid) return;
        var sel = SelectedItems;
        IsGrid = grid;
        ListView.IsVisible = !grid;
        Header.IsVisible = !grid;
        IconView.IsVisible = grid;
        foreach (var s in sel) ActiveList.SelectedItems?.Add(s);
        if (grid)
        {
            int gen = _gen;
            foreach (var it in _all)
                if (it.Thumb is null && !it.IsDirectory && FileTypes.HasThumbnail(it.Extension))
                    IconCache.RequestThumb(it, () => gen != _gen);
        }
    }

    // ---------------- Breadcrumbs & path editing ----------------

    void BuildCrumbs()
    {
        Crumbs.Children.Clear();
        AddCrumb("This PC", ThisPC, CurrentPath == ThisPC);
        if (CurrentPath != ThisPC)
        {
            var parts = new List<(string Name, string Path)>();
            for (var di = new DirectoryInfo(CurrentPath); di is not null; di = di.Parent)
                parts.Insert(0, (di.Parent is null ? di.FullName.TrimEnd('\\') : di.Name, di.FullName));
            foreach (var (name, path) in parts)
            {
                Crumbs.Children.Add(new TextBlock { Text = "›", Classes = { "sub" }, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new Thickness(1, 0) });
                AddCrumb(name, path, string.Equals(path, CurrentPath, StringComparison.OrdinalIgnoreCase));
            }
        }
        Dispatcher.UIThread.Post(() => CrumbScroll.Offset = new Vector(double.MaxValue, 0), DispatcherPriority.Background);
    }

    void AddCrumb(string text, string path, bool last)
    {
        var b = new Button { Content = text, Classes = { "crumb" } };
        if (last) b.FontWeight = Avalonia.Media.FontWeight.SemiBold;
        b.Click += (_, _) => Navigate(path);
        DragDrop.SetAllowDrop(b, true);
        b.Tag = path;
        Crumbs.Children.Add(b);
    }

    public void EditPath()
    {
        PathBox.Text = CurrentPath == ThisPC ? "" : CurrentPath;
        PathBox.IsVisible = true;
        CrumbArea.IsVisible = false;
        PathBox.Focus();
        PathBox.SelectAll();
    }

    void EndEditPath()
    {
        PathBox.IsVisible = false;
        CrumbArea.IsVisible = true;
    }

    void Crumbs_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is not Button) EditPath();
    }

    void PathBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var p = Environment.ExpandEnvironmentVariables(PathBox.Text?.Trim().Trim('"') ?? "");
            EndEditPath();
            if (p.Length == 0) Navigate(ThisPC);
            else if (File.Exists(p)) Navigate(Path.GetDirectoryName(p)!, true, p);
            else Navigate(p);
            FocusList();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape) { EndEditPath(); FocusList(); e.Handled = true; }
    }

    void PathBox_LostFocus(object? sender, RoutedEventArgs e) => EndEditPath();

    // ---------------- Open / context menu ----------------

    void List_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((e.Source as Control)?.DataContext is FileItem it) OpenRequested?.Invoke(this, it);
    }

    void Menu_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string cmd }) CommandRequested?.Invoke(this, cmd);
    }

    // ---------------- Watcher ----------------

    void SetupWatcher()
    {
        _watcher?.Dispose();
        _watcher = null;
        if (CurrentPath == ThisPC) return;
        try
        {
            var w = new FileSystemWatcher(CurrentPath)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite,
            };
            FileSystemEventHandler h = (_, _) => Dispatcher.UIThread.Post(() => { _reloadTimer.Stop(); _reloadTimer.Start(); });
            w.Created += h; w.Deleted += h; w.Changed += h;
            w.Renamed += (_, _) => Dispatcher.UIThread.Post(() => { _reloadTimer.Stop(); _reloadTimer.Start(); });
            w.EnableRaisingEvents = true;
            _watcher = w;
        }
        catch { }
    }

    // ---------------- Drag & drop ----------------

    void OnListPressed(object? sender, PointerPressedEventArgs e)
    {
        var pt = e.GetCurrentPoint(this);
        if (!pt.Properties.IsLeftButtonPressed) { _pressPoint = null; return; }
        _pressPoint = pt.Position;
        _pressItem = (e.Source as Control)?.DataContext as FileItem;
    }

    async void OnListMoved(object? sender, PointerEventArgs e)
    {
        if (_pressPoint is not { } start || _pressItem is null || _pressItem.IsDrive) return;
        var pos = e.GetPosition(this);
        if (Math.Abs(pos.X - start.X) < 8 && Math.Abs(pos.Y - start.Y) < 8) return;
        _pressPoint = null;

        var sel = SelectedItems;
        if (!sel.Contains(_pressItem)) sel = [_pressItem];
        var paths = sel.Where(i => !i.IsDrive).Select(i => i.FullPath).ToList();
        if (paths.Count == 0) return;

        var data = new DataObject();
        data.Set(DragFormat, string.Join("\n", paths));
        data.Set(DataFormats.FileNames, paths);
        await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy | DragDropEffects.Move);
    }

    static List<string> DroppedPaths(IDataObject data)
    {
        if (data.Contains(DragFormat) && data.Get(DragFormat) is string s)
            return s.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
        var files = data.GetFiles();
        if (files is not null)
            return files.Select(f => f.TryGetLocalPath()).Where(p => p is not null).Select(p => p!).ToList();
        return data.GetFileNames()?.ToList() ?? [];
    }

    string? DropTarget(DragEventArgs e)
    {
        if ((e.Source as Control)?.DataContext is FileItem { IsDirectory: true } f) return f.FullPath;
        return CurrentPath == ThisPC ? null : CurrentPath;
    }

    void OnDragOver(object? sender, DragEventArgs e)
    {
        var target = DropTarget(e);
        if (target is null) { e.DragEffects = DragDropEffects.None; return; }
        e.DragEffects = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? DragDropEffects.Move : DragDropEffects.Copy;
    }

    void OnDrop(object? sender, DragEventArgs e)
    {
        var target = DropTarget(e);
        if (target is null) return;
        var paths = DroppedPaths(e.Data)
            .Where(p => !string.Equals(p, target, StringComparison.OrdinalIgnoreCase))
            .Where(p => !string.Equals(Path.GetDirectoryName(p), target, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (paths.Count == 0) return;
        FilesDropped?.Invoke(this, paths, target, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
    }
}

/// <summary>Sorts "file2" before "file10" like Windows Explorer does.</summary>
public sealed class NaturalComparer : IComparer<string>
{
    public static readonly NaturalComparer Instance = new();

    [System.Runtime.InteropServices.DllImport("shlwapi.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern int StrCmpLogicalW(string a, string b);

    public int Compare(string? x, string? y) => StrCmpLogicalW(x ?? "", y ?? "");
}

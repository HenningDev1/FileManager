using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FileManager.Controls;
using FileManager.Models;
using FileManager.Services;
using FileManager.Views;

namespace FileManager;

public partial class MainWindow : Window
{
    public static MainWindow Current { get; private set; } = null!;

    static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    FilePane _active = null!;
    bool _dual, _preview;
    readonly List<string> _clip = [];
    bool _clipCut;
    string? _tool;
    readonly Dictionary<string, Button> _side = new(StringComparer.OrdinalIgnoreCase);
    readonly DispatcherTimer _filterTimer, _statusTimer;
    SearchView? _searchView;
    DuplicatesView? _dupView;
    DashboardView? _dashView;

    public MainWindow()
    {
        InitializeComponent();
        Current = this;
        LoadAppIcon();

        _filterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _filterTimer.Tick += (_, _) => { _filterTimer.Stop(); _active.SetFilter(SearchBox.Text ?? ""); };
        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _statusTimer.Tick += (_, _) => { _statusTimer.Stop(); UpdateStatus(); };

        foreach (var p in new[] { Pane1, Pane2 })
        {
            p.Activated += SetActive;
            p.SelectionChangedEx += OnPaneSelection;
            p.PathChanged += OnPanePath;
            p.CommandRequested += (pane, cmd) => { SetActive(pane); RunCommand(cmd); };
            p.OpenRequested += OpenItem;
            p.FilesDropped += OnFilesDropped;
        }

        var s = Settings.Current;
        _active = Pane1;
        BuildSidebar();
        Pane1.Navigate(Valid(s.LastPath) ?? Home, false);
        Pane2.Navigate(Valid(s.LastPath2) ?? FilePane.ThisPC, false);
        SetDual(s.DualPane);
        SetPreview(s.ShowPreview);
        SetGrid(s.GridView);
        SetActive(Pane1);

        AddHandler(KeyDownEvent, OnKeyDownTunnel, RoutingStrategies.Tunnel);
        SizeChanged += (_, e) => AdaptToolbar(e.NewSize.Width);
        Closing += (_, _) =>
        {
            s.LastPath = Pane1.CurrentPath == FilePane.ThisPC ? null : Pane1.CurrentPath;
            s.LastPath2 = Pane2.CurrentPath == FilePane.ThisPC ? null : Pane2.CurrentPath;
            Settings.Save();
        };
        Opened += (_, _) => Pane1.FocusList();
    }

    static string? Valid(string? p) => p is not null && Directory.Exists(p) ? p : null;

    void LoadAppIcon()
    {
        try
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://FileManager/Assets/icon.ico")));
            AppIconImage.Source = new Bitmap(AssetLoader.Open(new Uri("avares://FileManager/Assets/icon.png")));
        }
        catch { }
    }

    // ================= Title bar =================

    void Drag_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source != sender) return;
        var pt = e.GetCurrentPoint(this);
        if (!pt.Properties.IsLeftButtonPressed) return;
        if (e.ClickCount == 2)
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else
            BeginMoveDrag(e);
    }

    double _scale = 1;

    /// <summary>
    /// Dynamic scaling: picks an interface scale (automatic for small/low-resolution screens, or the
    /// user's choice) and adapts sidebar, preview and toolbar to the space that's actually available.
    /// </summary>
    void AdaptToolbar(double windowWidth)
    {
        if (windowWidth <= 0) return;
        var setting = Settings.Current.UiScale;
        double scale = setting > 0 ? setting : windowWidth < 1000 ? 0.85 : windowWidth < 1200 ? 0.9 : 1.0;
        if (Math.Abs(scale - _scale) > 0.001 || ScaleHost.LayoutTransform is null)
        {
            _scale = scale;
            ScaleHost.LayoutTransform = new ScaleTransform(scale, scale);
        }
        double width = windowWidth / _scale;   // width in scaled layout units

        // Keep clear of the Windows caption buttons, which are not scaled.
        ToolbarGrid.Margin = new Thickness(12, 12, 146 / _scale, 6);

        bool narrow = width < 1350;
        RootGrid.ColumnDefinitions[0].Width = new GridLength(width < 1100 ? 180 : narrow ? 200 : 236);
        if (_preview)
            BrowserGrid.ColumnDefinitions[4].Width = new GridLength(width < 1100 ? 230 : narrow ? 260 : 300);

        bool browser = _tool is null;
        ActionGroup.IsVisible = browser && width >= (_dual ? 1400 : 1280);
        ViewGroup.IsVisible = browser;
        NavGroup.IsVisible = browser;
        SearchBox.Width = width < 1100 ? 160 : 240;
    }

    public void ApplyScale() => AdaptToolbar(Bounds.Width);

    // ================= Sidebar =================

    void BuildSidebar()
    {
        SidebarPanel.Children.Clear();
        _side.Clear();
        Section("Favorites");
        Nav("home", "Home", Home);
        Nav("desktop", "Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        Nav("doc", "Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        Nav("download", "Downloads", Path.Combine(Home, "Downloads"));
        Nav("picture", "Pictures", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
        Nav("music", "Music", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));
        Nav("video", "Videos", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));

        Section("Locations");
        Nav("pc", "This PC", FilePane.ThisPC);
        foreach (var d in DriveInfo.GetDrives())
        {
            try { if (d.IsReady && d.TotalSize > 0) AddDrive(d); } catch { }
        }

        Section("Tools");
        Tool("search", "Search", "search");
        Tool("dup", "Duplicates", "dup");
        Tool("chart", "Storage", "dash");
        UpdateSidebarActive();
    }

    void Section(string text) =>
        SidebarPanel.Children.Add(new TextBlock { Text = text, Classes = { "section" } });

    Button SideButton(string icon, string text, Control? extra = null)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        var ico = new Ico { Kind = icon, Margin = new Thickness(0, 0, 10, 0) };
        Dialogs.Res(ico, Ico.ForegroundProperty, "AccentBrush");
        row.Children.Add(ico);
        var tb = new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(tb, 1);
        row.Children.Add(tb);
        Control content = row;
        if (extra is not null) content = new StackPanel { Spacing = 4, Children = { row, extra } };
        var b = new Button { Content = content };
        b.Classes.Add("side");
        SidebarPanel.Children.Add(b);
        return b;
    }

    void Nav(string icon, string text, string path)
    {
        if (path != FilePane.ThisPC && !Directory.Exists(path)) return;
        var b = SideButton(icon, text);
        b.Click += (_, _) => { ShowBrowser(); _active.Navigate(path); _active.FocusList(); };
        _side[path] = b;
        DragDrop.SetAllowDrop(b, path != FilePane.ThisPC);
        b.AddHandler(DragDrop.DropEvent, (_, e) => DropOnSidebar(e, path));
    }

    void AddDrive(DriveInfo d)
    {
        long used = d.TotalSize - d.AvailableFreeSpace;
        double pct = used * 100.0 / d.TotalSize;
        var label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? (d.DriveType == DriveType.Fixed ? "Local Disk" : d.DriveType.ToString()) : d.VolumeLabel;
        var bar = new ProgressBar { Value = pct, Maximum = 100, Height = 4, MinHeight = 4, Margin = new Thickness(26, 2, 0, 0) };
        if (pct > 90) bar.Foreground = new SolidColorBrush(Color.Parse("#FF3B30"));
        var free = new TextBlock { Text = $"{Fmt.Size(d.AvailableFreeSpace)} free of {Fmt.Size(d.TotalSize)}", Margin = new Thickness(26, 0, 0, 0) };
        free.Classes.Add("sub");
        var b = SideButton("drive", $"{label} ({d.Name.TrimEnd('\\')})", new StackPanel { Spacing = 3, Children = { bar, free } });
        var root = d.RootDirectory.FullName;
        b.Click += (_, _) => { ShowBrowser(); _active.Navigate(root); };
        _side[root] = b;
    }

    void Tool(string icon, string text, string key)
    {
        var b = SideButton(icon, text);
        b.Click += (_, _) => ShowTool(key);
        _side["tool:" + key] = b;
    }

    void UpdateSidebarActive()
    {
        var key = _tool is not null ? "tool:" + _tool : _active?.CurrentPath ?? "";
        foreach (var (k, b) in _side)
            b.Classes.Set("active", string.Equals(Path.TrimEndingDirectorySeparator(k), Path.TrimEndingDirectorySeparator(key), StringComparison.OrdinalIgnoreCase));
    }

    async void DropOnSidebar(DragEventArgs e, string path)
    {
        var files = e.Data.GetFiles()?.Select(f => f.TryGetLocalPath()).Where(p => p is not null).Select(p => p!).ToList();
        if (e.Data.Get("x-filemanager-paths") is string s) files = s.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (files is null || files.Count == 0) return;
        await RunOp(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? "Moving…" : "Copying…",
            () => e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? FileOps.Move(files, path) : FileOps.Copy(files, path));
    }

    // ================= Views =================

    void ShowTool(string key, string? query = null)
    {
        Control view = key switch
        {
            "search" => _searchView ??= new SearchView(),
            "dup" => _dupView ??= new DuplicatesView(),
            _ => _dashView ??= new DashboardView(),
        };
        _tool = key;
        ToolHost.Content = view;
        ToolHost.IsVisible = true;
        BrowserGrid.IsVisible = false;
        BatchBar.IsVisible = false;
        ViewTitle.Text = "";
        AdaptToolbar(Bounds.Width);
        var cur = _active.CurrentPath == FilePane.ThisPC ? Home : _active.CurrentPath;
        switch (view)
        {
            case SearchView sv: sv.Activate(cur, query); break;
            case DuplicatesView dv: dv.SuggestRoot(cur); break;
            case DashboardView db: db.Activate(); break;
        }
        UpdateSidebarActive();
        UpdateStatus();
    }

    public void ShowDuplicatesFor(string folder)
    {
        ShowTool("dup");
        _dupView?.ForceRoot(folder);
    }

    public void ShowBrowser()
    {
        if (_tool is null) return;
        _tool = null;
        ToolHost.IsVisible = false;
        BrowserGrid.IsVisible = true;
        AdaptToolbar(Bounds.Width);
        UpdateSidebarActive();
        OnPaneSelection(_active);
    }

    /// <summary>Opens a folder (and optionally selects an item in it) in the active pane.</summary>
    public void OpenInBrowser(string folder, string? select = null)
    {
        ShowBrowser();
        _active.Navigate(folder, true, select);
        _active.FocusList();
    }

    void SetActive(FilePane p)
    {
        _active = p;
        Pane1.IsActive = _dual && p == Pane1;
        Pane2.IsActive = _dual && p == Pane2;
        UpdateNav();
        UpdateSidebarActive();
        OnPaneSelection(p);
    }

    FilePane Other(FilePane p) => p == Pane1 ? Pane2 : Pane1;

    void SetDual(bool on)
    {
        _dual = on;
        Settings.Current.DualPane = on;
        DualToggle.IsChecked = on;
        BrowserGrid.ColumnDefinitions[1].Width = new GridLength(on ? 10 : 0);
        BrowserGrid.ColumnDefinitions[2].Width = on ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        BrowserGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        Split1.IsVisible = on;
        Pane2.IsVisible = on;
        Pane1.ShowNav = on;
        Pane2.ShowNav = on;
        AdaptToolbar(Bounds.Width);
        if (!on && _active == Pane2) SetActive(Pane1);
        else SetActive(_active);
    }

    void SetPreview(bool on)
    {
        _preview = on;
        Settings.Current.ShowPreview = on;
        PreviewToggle.IsChecked = on;
        BrowserGrid.ColumnDefinitions[3].Width = new GridLength(on ? 10 : 0);
        BrowserGrid.ColumnDefinitions[4].Width = new GridLength(on ? 300 : 0);
        Split2.IsVisible = on;
        Preview.IsVisible = on;
        AdaptToolbar(Bounds.Width);
        if (on) Preview.Show(_active.SelectedItems);
    }

    void SetGrid(bool grid)
    {
        Settings.Current.GridView = grid;
        ListToggle.IsChecked = !grid;
        GridToggle.IsChecked = grid;
        Pane1.SetGrid(grid);
        Pane2.SetGrid(grid);
    }

    // ================= Pane events =================

    void OnPaneSelection(FilePane p)
    {
        if (p != _active) return;
        var sel = p.SelectedItems;
        if (_preview && _tool is null) Preview.Show(sel);
        BatchBar.IsVisible = _tool is null && sel.Count >= 2;
        BatchText.Text = $"{sel.Count:N0} selected";
        UpdateStatus();
    }

    void OnPanePath(FilePane p)
    {
        if (p != _active) return;
        UpdateNav();
        UpdateSidebarActive();
        if (!string.IsNullOrEmpty(SearchBox.Text)) SearchBox.Text = "";
    }

    void UpdateNav()
    {
        BackBtn.IsEnabled = _active.CanGoBack;
        FwdBtn.IsEnabled = _active.CanGoForward;
        UpBtn.IsEnabled = _active.CurrentPath != FilePane.ThisPC;
        Title = _active.CurrentPath == FilePane.ThisPC ? "File Manager" : $"{Path.GetFileName(Path.TrimEndingDirectorySeparator(_active.CurrentPath))} — File Manager";
    }

    public void Status(string text, bool sticky = false)
    {
        StatusText.Text = text;
        _statusTimer.Stop();
        if (!sticky) _statusTimer.Start();
    }

    void UpdateStatus()
    {
        if (_tool is not null) { StatusText.Text = ""; StatusRight.Text = ""; return; }
        var sel = _active.SelectedItems;
        var text = $"{_active.ItemCount:N0} items";
        if (sel.Count > 0)
        {
            var size = sel.Where(i => !i.IsDirectory).Sum(i => i.Size);
            text += $"  ·  {sel.Count:N0} selected" + (size > 0 ? $" ({Fmt.Size(size)})" : "");
        }
        if (_clip.Count > 0) text += $"  ·  {_clip.Count} on clipboard{(_clipCut ? " (cut)" : "")}";
        StatusText.Text = text;
        try
        {
            if (_active.CurrentPath != FilePane.ThisPC)
            {
                var d = new DriveInfo(Path.GetPathRoot(_active.CurrentPath)!);
                StatusRight.Text = $"{Fmt.Size(d.AvailableFreeSpace)} free on {d.Name.TrimEnd('\\')}";
            }
            else StatusRight.Text = "";
        }
        catch { StatusRight.Text = ""; }
    }

    void OpenItem(FilePane pane, FileItem it)
    {
        if (it.IsDirectory) { pane.Navigate(it.FullPath); pane.FocusList(); }
        else Shell.Open(it.FullPath);
    }

    async void OnFilesDropped(FilePane pane, List<string> paths, string dest, bool move)
    {
        await RunOp(move ? $"Moving {paths.Count} item(s)…" : $"Copying {paths.Count} item(s)…",
            () => move ? FileOps.Move(paths, dest) : FileOps.Copy(paths, dest));
    }

    async Task RunOp(string label, Func<Task> op)
    {
        Status(label, true);
        try
        {
            await op();
            Status("Done");
        }
        catch (Exception ex)
        {
            Status("Failed");
            await Dialogs.Info(this, "Couldn't finish", ex.Message);
        }
        await Pane1.Reload();
        if (_dual) await Pane2.Reload();
    }

    // ================= Toolbar handlers =================

    void Back_Click(object? s, RoutedEventArgs e) { ShowBrowser(); _active.GoBack(); }
    void Forward_Click(object? s, RoutedEventArgs e) { ShowBrowser(); _active.GoForward(); }
    void Up_Click(object? s, RoutedEventArgs e) { ShowBrowser(); _active.GoUp(); }
    void Cmd_Click(object? s, RoutedEventArgs e) { if (s is Control { Tag: string cmd }) RunCommand(cmd); }
    void ListMode_Click(object? s, RoutedEventArgs e) => SetGrid(false);
    void GridMode_Click(object? s, RoutedEventArgs e) => SetGrid(true);
    void Dual_Click(object? s, RoutedEventArgs e) => SetDual(DualToggle.IsChecked == true);
    void Preview_Click(object? s, RoutedEventArgs e) => SetPreview(PreviewToggle.IsChecked == true);

    async void Settings_Click(object? s, RoutedEventArgs e)
    {
        bool hidden = Settings.Current.ShowHidden;
        await new SettingsWindow().ShowDialog(this);
        Settings.Save();
        if (hidden != Settings.Current.ShowHidden)
        {
            await Pane1.Reload();
            await Pane2.Reload();
        }
    }

    void Search_TextChanged(object? s, TextChangedEventArgs e)
    {
        if (_tool is not null) return;
        _filterTimer.Stop();
        _filterTimer.Start();
    }

    void Search_KeyDown(object? s, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(SearchBox.Text))
        {
            var q = SearchBox.Text!;
            SearchBox.Text = "";
            ShowTool("search", q);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            SearchBox.Text = "";
            _active.FocusList();
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            _active.FocusList();
            e.Handled = true;
        }
    }

    // ================= Keyboard =================

    void OnKeyDownTunnel(object? sender, KeyEventArgs e)
    {
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        bool alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        bool inText = FocusManager?.GetFocusedElement() is TextBox;

        if (ctrl && shift && e.Key == Key.F) { ShowTool("search"); e.Handled = true; return; }
        if (ctrl && e.Key == Key.F) { if (_tool is not null) ShowBrowser(); SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true; return; }
        if (e.Key == Key.F6) { ShowBrowser(); SetDual(!_dual); e.Handled = true; return; }
        if (ctrl && e.Key == Key.P) { SetPreview(!_preview); e.Handled = true; return; }
        if (ctrl && e.Key == Key.L) { ShowBrowser(); _active.EditPath(); e.Handled = true; return; }
        if (inText || _tool is not null) return;

        if (alt && e.Key == Key.Left) { _active.GoBack(); e.Handled = true; return; }
        if (alt && e.Key == Key.Right) { _active.GoForward(); e.Handled = true; return; }
        if (alt && e.Key == Key.Up) { _active.GoUp(); e.Handled = true; return; }

        string? cmd = e.Key switch
        {
            Key.Back => "up",
            Key.Enter => "open",
            Key.Delete => shift ? "deleteperm" : "delete",
            Key.F2 => "rename",
            Key.F5 => "refresh",
            Key.C when ctrl && shift => "copypath",
            Key.C when ctrl => "copy",
            Key.X when ctrl => "cut",
            Key.V when ctrl => "paste",
            Key.A when ctrl => "selectall",
            Key.R when ctrl => "batchrename",
            Key.N when ctrl && shift => "newfolder",
            Key.Tab when _dual && !ctrl => "switchpane",
            Key.Escape => "clearsel",
            _ => null,
        };
        if (cmd is null) return;
        e.Handled = true;
        RunCommand(cmd);
    }

    // ================= Commands =================

    public async void RunCommand(string cmd)
    {
        var p = _active;
        var sel = p.SelectedItems.Where(i => !i.IsDrive).ToList();
        var paths = sel.Select(i => i.FullPath).ToList();
        var dir = p.CurrentPath;
        bool real = dir != FilePane.ThisPC;

        try
        {
            switch (cmd)
            {
                case "up": p.GoUp(); break;
                case "switchpane": { var o = Other(p); SetActive(o); o.FocusList(); break; }
                case "open":
                {
                    var all = p.SelectedItems;
                    if (all.Count == 1) OpenItem(p, all[0]);
                    else foreach (var f in all.Where(i => !i.IsDirectory).Take(15)) Shell.Open(f.FullPath);
                    break;
                }
                case "openother":
                {
                    var d = p.SelectedItems.FirstOrDefault(i => i.IsDirectory);
                    if (d is null) return;
                    if (!_dual) SetDual(true);
                    Other(p).Navigate(d.FullPath);
                    break;
                }
                case "copy":
                case "cut":
                    if (paths.Count == 0) return;
                    _clip.Clear(); _clip.AddRange(paths); _clipCut = cmd == "cut";
                    await SetSystemClipboard(paths);
                    Status($"{paths.Count} item(s) {(_clipCut ? "cut" : "copied")} — paste with Ctrl+V");
                    break;
                case "paste":
                {
                    if (!real) return;
                    var (src, cut) = await GetPasteSource();
                    if (src.Count == 0) { Status("Clipboard is empty"); return; }
                    await RunOp(cut ? $"Moving {src.Count} item(s)…" : $"Copying {src.Count} item(s)…",
                        () => cut ? FileOps.Move(src, dir) : FileOps.Copy(src, dir));
                    if (cut) _clip.Clear();
                    break;
                }
                case "copyother":
                case "moveother":
                {
                    if (paths.Count == 0) return;
                    var other = Other(p);
                    if (!_dual)
                    {
                        SetDual(true);
                        Status("Dual pane opened — pick the destination on the other side, then run it again.");
                        return;
                    }
                    if (other.CurrentPath == FilePane.ThisPC) { Status("Open a folder in the other pane first"); return; }
                    bool move = cmd == "moveother";
                    await RunOp(move ? $"Moving {paths.Count} item(s)…" : $"Copying {paths.Count} item(s)…",
                        () => move ? FileOps.Move(paths, other.CurrentPath) : FileOps.Copy(paths, other.CurrentPath));
                    break;
                }
                case "newfolder":
                {
                    if (!real) return;
                    var name = await Dialogs.Prompt(this, "New Folder", "Folder name", "New Folder");
                    if (string.IsNullOrWhiteSpace(name)) return;
                    var np = FileOps.Unique(Path.Combine(dir, name.Trim()));
                    Directory.CreateDirectory(np);
                    await p.Reload(false, np);
                    p.SelectPath(np);
                    break;
                }
                case "newfile":
                {
                    if (!real) return;
                    var name = await Dialogs.Prompt(this, "New Text File", "File name", "New Text Document.txt", true);
                    if (string.IsNullOrWhiteSpace(name)) return;
                    var np = FileOps.Unique(Path.Combine(dir, name.Trim()));
                    await File.WriteAllTextAsync(np, "");
                    await p.Reload(false, np);
                    p.SelectPath(np);
                    break;
                }
                case "rename":
                {
                    if (sel.Count == 0) return;
                    if (sel.Count > 1) { RunCommand("batchrename"); return; }
                    var it = sel[0];
                    var nn = await Dialogs.Prompt(this, "Rename", "New name", it.Name, !it.IsDirectory);
                    if (string.IsNullOrWhiteSpace(nn) || nn == it.Name) return;
                    var target = Path.Combine(Path.GetDirectoryName(it.FullPath)!, nn.Trim());
                    FileOps.Rename(it.FullPath, target);
                    await p.Reload(false, target);
                    p.SelectPath(target);
                    break;
                }
                case "batchrename":
                {
                    if (sel.Count == 0) { Status("Select the files you want to rename"); return; }
                    var w = new BatchRenameWindow(sel.Select(i => i.FullPath).ToList());
                    await w.ShowDialog(this);
                    if (w.Applied) { await p.Reload(false); Status(w.ResultMessage); }
                    break;
                }
                case "delete":
                case "deleteperm":
                {
                    if (paths.Count == 0) return;
                    bool perm = cmd == "deleteperm";
                    var what = paths.Count == 1 ? $"\"{sel[0].Name}\"" : $"{paths.Count:N0} items";
                    bool ok = await Dialogs.Confirm(this,
                        perm ? "Delete permanently?" : "Move to Recycle Bin?",
                        perm ? $"{what} will be permanently deleted. This can't be undone." : $"{what} will be moved to the Recycle Bin.",
                        perm ? "Delete" : "Move to Recycle Bin", danger: true);
                    if (!ok) return;
                    await RunOp("Deleting…", () => FileOps.Delete(paths, !perm));
                    break;
                }
                case "zip":
                {
                    if (paths.Count == 0 || !real) return;
                    string? zp = null;
                    await RunOp("Compressing…", async () => zp = await FileOps.Zip(paths, dir));
                    if (zp is not null) p.SelectPath(zp);
                    break;
                }
                case "extract":
                {
                    var zips = paths.Where(x => x.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).ToList();
                    if (zips.Count == 0) { Status("Select a .zip file to extract"); return; }
                    await RunOp("Extracting…", () => FileOps.Extract(zips));
                    break;
                }
                case "copypath":
                    if (Clipboard is not null)
                        await Clipboard.SetTextAsync(string.Join(Environment.NewLine, paths.Count > 0 ? paths : [real ? dir : ""]));
                    Status("Path copied");
                    break;
                case "explorer": Shell.Reveal(paths.FirstOrDefault() ?? (real ? dir : Home)); break;
                case "terminal": Shell.Terminal(real ? dir : Home); break;
                case "properties": Shell.Properties(paths.FirstOrDefault() ?? (real ? dir : Home)); break;
                case "selectall": p.SelectAll(); break;
                case "clearsel": p.ClearSelection(); break;
                case "refresh": await p.Reload(); BuildSidebar(); break;
            }
        }
        catch (Exception ex)
        {
            await Dialogs.Info(this, "Something went wrong", ex.Message);
        }
        UpdateStatus();
    }

    async Task SetSystemClipboard(List<string> paths)
    {
        try
        {
            if (Clipboard is null) return;
            var items = new List<IStorageItem>();
            foreach (var path in paths)
            {
                IStorageItem? si = Directory.Exists(path)
                    ? await StorageProvider.TryGetFolderFromPathAsync(path)
                    : await StorageProvider.TryGetFileFromPathAsync(path);
                if (si is not null) items.Add(si);
            }
            var data = new DataObject();
            data.Set(DataFormats.Files, items);
            await Clipboard.SetDataObjectAsync(data);
        }
        catch { /* internal clipboard still works */ }
    }

    async Task<(List<string> Paths, bool Cut)> GetPasteSource()
    {
        List<string> sys = [];
        try
        {
            if (Clipboard is not null)
            {
                var obj = await Clipboard.GetDataAsync(DataFormats.Files);
                if (obj is IEnumerable<IStorageItem> items)
                    sys = items.Select(i => i.TryGetLocalPath()).Where(x => x is not null).Select(x => x!).ToList();
                else if (obj is IEnumerable<string> names)
                    sys = names.ToList();
            }
        }
        catch { }

        if (sys.Count > 0 && !(sys.Count == _clip.Count && sys.All(x => _clip.Contains(x, StringComparer.OrdinalIgnoreCase))))
            return (sys, false);
        return (_clip.ToList(), _clipCut);
    }
}

using System.IO.Compression;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using FileManager.Models;
using FileManager.Services;
using Windows.Data.Pdf;

namespace FileManager.Views;

/// <summary>Quick-look panel: images, PDFs (page by page), videos (frame scrubbing), audio, text/code, archives, folders.</summary>
public partial class PreviewPanel : UserControl
{
    int _gen;
    FileItem? _item;
    PdfDocument? _pdf;
    uint _page;
    WinRtMedia.Video? _video;
    CancellationTokenSource? _cts;
    readonly DispatcherTimer _scrubTimer;
    bool _settingScrub;

    public PreviewPanel()
    {
        InitializeComponent();
        _scrubTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(110) };
        _scrubTimer.Tick += async (_, _) => { _scrubTimer.Stop(); await ScrubTo(Scrub.Value); };
    }

    public void Show(IReadOnlyList<FileItem> sel)
    {
        int gen = ++_gen;
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _pdf = null; _video = null; _item = null;
        PdfBar.IsVisible = VideoBar.IsVisible = TextHost.IsVisible = ListHost.IsVisible = ExtraBtn.IsVisible = Badge.IsVisible = false;
        PropsPanel.Children.Clear(); ListHost.Children.Clear(); Frames.Children.Clear();
        PreviewImage.Source = null; TextPreview.Text = "";
        OpenBtn.IsVisible = true; OpenText.Text = "Open"; OpenIco.Kind = "external";

        if (sel.Count == 0) { Empty("Select a file to preview"); return; }
        Scroll.IsVisible = true; EmptyState.IsVisible = false;

        if (sel.Count > 1)
        {
            NameText.Text = $"{sel.Count:N0} items selected";
            int dirs = sel.Count(i => i.IsDirectory);
            KindText.Text = $"{sel.Count - dirs:N0} files · {dirs:N0} folders";
            Prop("Total size", Fmt.Size(sel.Where(i => !i.IsDirectory).Sum(i => i.Size)) + (dirs > 0 ? " (files only)" : ""));
            PreviewImage.Source = sel[0].Icon;
            OpenBtn.IsVisible = false;
            return;
        }

        var it = sel[0];
        _item = it;
        NameText.Text = it.Name;
        KindText.Text = it.TypeName;
        PreviewImage.Source = it.Thumb ?? it.Icon;

        if (it.IsDrive) { ShowDrive(it); return; }
        if (it.IsDirectory) { LoadFolder(it, gen); return; }

        Prop("Size", $"{Fmt.Size(it.Size)} ({it.Size:N0} bytes)");
        Prop("Modified", Fmt.Date(it.Modified));
        try { Prop("Created", Fmt.Date(File.GetCreationTime(it.FullPath))); } catch { }
        Prop("Where", it.Folder);

        var ext = it.Extension;
        if (FileTypes.Images.Contains(ext)) LoadImage(it, gen);
        else if (ext == ".pdf") LoadPdf(it, gen);
        else if (FileTypes.Video.Contains(ext)) LoadVideo(it, gen);
        else if (FileTypes.Audio.Contains(ext)) LoadAudio(it, gen);
        else
        {
            LoadThumb(it, gen);
            if (ext == ".zip") LoadZip(it, gen);
            else if (FileTypes.Text.Contains(ext) || (ext.Length == 0 || !FileTypes.HasThumbnail(ext)) && it.Size < 2_000_000) LoadText(it, gen);
        }
    }

    void Empty(string text)
    {
        Scroll.IsVisible = false;
        EmptyState.IsVisible = true;
        EmptyText.Text = text;
    }

    void Prop(string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("88,*") };
        var l = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Top };
        l.Classes.Add("sub");
        var v = new SelectableTextBlock { Text = value, TextWrapping = TextWrapping.Wrap };
        Dialogs.Res(v, SelectableTextBlock.FontSizeProperty, "AppFontSizeSmall");
        Grid.SetColumn(v, 1);
        g.Children.Add(l); g.Children.Add(v);
        PropsPanel.Children.Add(g);
    }

    async void LoadThumb(FileItem it, int gen, int size = 512)
    {
        try
        {
            var b = await Shell.PreviewWorker.Run(() => Shell.GetImage(it.FullPath, size, false));
            if (gen == _gen && b is not null && (PreviewImage.Source is null || PreviewImage.Source == it.Icon || PreviewImage.Source == it.Thumb))
                PreviewImage.Source = b;
        }
        catch { }
    }

    async void LoadImage(FileItem it, int gen)
    {
        try
        {
            var bmp = await Task.Run(() =>
            {
                using var fs = File.OpenRead(it.FullPath);
                return it.Size < 4_000_000 ? new Bitmap(fs) : Bitmap.DecodeToWidth(fs, 1000);
            });
            if (gen != _gen) return;
            PreviewImage.Source = bmp;
        }
        catch { LoadThumb(it, gen); }

        var info = await WinRtMedia.GetImageInfo(it.FullPath);
        if (gen != _gen || info is null) return;
        if (info.Width > 0) Prop("Dimensions", $"{info.Width} × {info.Height} px");
        if (info.Taken is { } t) Prop("Taken", Fmt.Date(t.LocalDateTime));
        Prop("Camera", info.Camera);
    }

    async void LoadPdf(FileItem it, int gen)
    {
        LoadThumb(it, gen);
        try
        {
            var doc = await WinRtMedia.OpenPdf(it.FullPath);
            if (gen != _gen) return;
            _pdf = doc; _page = 0;
            Prop("Pages", doc.PageCount.ToString());
            PdfBar.IsVisible = doc.PageCount > 0;
            await RenderPdf(gen);
        }
        catch { /* keep the shell thumbnail */ }
    }

    async Task RenderPdf(int gen)
    {
        if (_pdf is null) return;
        PageText.Text = $"Page {_page + 1} of {_pdf.PageCount}";
        try
        {
            var bmp = await WinRtMedia.RenderPdfPage(_pdf, _page, 900);
            if (gen == _gen) PreviewImage.Source = bmp;
        }
        catch { }
    }

    async void PdfPrev_Click(object? s, RoutedEventArgs e)
    {
        if (_pdf is null || _page == 0) return;
        _page--; await RenderPdf(_gen);
    }

    async void PdfNext_Click(object? s, RoutedEventArgs e)
    {
        if (_pdf is null || _page + 1 >= _pdf.PageCount) return;
        _page++; await RenderPdf(_gen);
    }

    async void LoadVideo(FileItem it, int gen)
    {
        OpenText.Text = "Play"; OpenIco.Kind = "play";
        LoadThumb(it, gen);
        try
        {
            var v = await WinRtMedia.OpenVideo(it.FullPath);
            if (gen != _gen) return;
            _video = v;
            Prop("Duration", Fmt.Duration(v.Duration));
            if (v.Width > 0) Prop("Resolution", $"{v.Width} × {v.Height}");
            if (v.Bitrate > 0) Prop("Bitrate", $"{v.Bitrate / 1000:N0} kbps");
            BadgeText.Text = Fmt.Duration(v.Duration); Badge.IsVisible = true;
            if (v.Duration <= TimeSpan.Zero) return;

            VideoBar.IsVisible = true;
            _settingScrub = true; Scrub.Value = 0; _settingScrub = false;
            TimeText.Text = $"0:00 / {Fmt.Duration(v.Duration)}";

            const int n = 5;
            for (int i = 0; i < n; i++)
            {
                var pos = (i + 0.5) / n;
                var bmp = await WinRtMedia.Frame(v, TimeSpan.FromTicks((long)(v.Duration.Ticks * pos)), 200, false);
                if (gen != _gen) return;
                if (bmp is null) continue;
                var b = new Button
                {
                    Padding = new Thickness(0), Margin = new Thickness(i == 0 ? 0 : 3, 0, 0, 0), CornerRadius = new CornerRadius(5),
                    Background = Brushes.Transparent, ClipToBounds = true,
                    Content = new Image { Source = bmp, Stretch = Stretch.UniformToFill, Height = 36 },
                };
                ToolTip.SetTip(b, Fmt.Duration(TimeSpan.FromTicks((long)(v.Duration.Ticks * pos))));
                b.Click += (_, _) => Scrub.Value = pos * 1000;
                Frames.Children.Add(b);
            }
        }
        catch { /* codec not available in Windows — shell thumbnail stays */ }
    }

    void Scrub_ValueChanged(object? s, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_settingScrub || _video is null) return;
        _scrubTimer.Stop(); _scrubTimer.Start();
        TimeText.Text = $"{Fmt.Duration(TimeSpan.FromTicks((long)(_video.Duration.Ticks * Scrub.Value / 1000)))} / {Fmt.Duration(_video.Duration)}";
    }

    async Task ScrubTo(double value)
    {
        var v = _video; int gen = _gen;
        if (v is null) return;
        try
        {
            var bmp = await WinRtMedia.Frame(v, TimeSpan.FromTicks((long)(v.Duration.Ticks * value / 1000)), 720, true);
            if (gen == _gen && bmp is not null) PreviewImage.Source = bmp;
        }
        catch { }
    }

    async void LoadAudio(FileItem it, int gen)
    {
        OpenText.Text = "Play"; OpenIco.Kind = "play";
        LoadThumb(it, gen);
        var a = await WinRtMedia.GetAudioInfo(it.FullPath);
        if (gen != _gen || a is null) return;
        Prop("Title", a.Title); Prop("Artist", a.Artist); Prop("Album", a.Album);
        if (a.Duration > TimeSpan.Zero) { Prop("Duration", Fmt.Duration(a.Duration)); BadgeText.Text = Fmt.Duration(a.Duration); Badge.IsVisible = true; }
        if (a.Bitrate > 0) Prop("Bitrate", $"{a.Bitrate / 1000:N0} kbps");
    }

    async void LoadText(FileItem it, int gen)
    {
        try
        {
            var text = await Task.Run(() =>
            {
                using var fs = new FileStream(it.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var buf = new byte[(int)Math.Min(64 * 1024, fs.Length)];
                int n = fs.ReadAtLeast(buf, buf.Length, false);
                bool utf16 = n >= 2 && ((buf[0] == 0xFF && buf[1] == 0xFE) || (buf[0] == 0xFE && buf[1] == 0xFF));
                if (!utf16 && Array.IndexOf(buf, (byte)0, 0, n) >= 0) return null;
                using var sr = new StreamReader(new MemoryStream(buf, 0, n), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                var lines = sr.ReadToEnd().Replace("\r", "").Replace("\t", "    ").Split('\n');
                var shown = string.Join('\n', lines.Take(400));
                return lines.Length > 400 || fs.Length > buf.Length ? shown + "\n…" : shown;
            });
            if (gen != _gen || text is null) return;
            TextPreview.Text = text;
            TextHost.IsVisible = true;
        }
        catch { }
    }

    async void LoadZip(FileItem it, int gen)
    {
        try
        {
            var (count, total, entries) = await Task.Run(() =>
            {
                using var z = ZipFile.OpenRead(it.FullPath);
                return (z.Entries.Count, z.Entries.Sum(e => e.Length), z.Entries.Take(150).Select(e => (e.FullName, e.Length)).ToList());
            });
            if (gen != _gen) return;
            Prop("Entries", $"{count:N0}");
            Prop("Unpacked", Fmt.Size(total));
            OpenText.Text = "Open"; ExtraBtn.Content = "Extract here"; ExtraBtn.IsVisible = true;
            foreach (var (name, len) in entries)
            {
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                var n = new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis };
                Dialogs.Res(n, TextBlock.FontSizeProperty, "AppFontSizeSmall");
                var s = new TextBlock { Text = name.EndsWith('/') ? "" : Fmt.Size(len), Margin = new Thickness(8, 0, 0, 0) };
                s.Classes.Add("sub");
                Grid.SetColumn(s, 1);
                g.Children.Add(n); g.Children.Add(s);
                ListHost.Children.Add(g);
            }
            if (count > 150) ListHost.Children.Add(new TextBlock { Text = $"… and {count - 150:N0} more", Classes = { "sub" } });
            ListHost.IsVisible = true;
        }
        catch { }
    }

    async void LoadFolder(FileItem it, int gen)
    {
        LoadThumb(it, gen, 256);
        OpenText.Text = "Open"; OpenIco.Kind = "folder";
        try { Prop("Modified", Fmt.Date(Directory.GetLastWriteTime(it.FullPath))); } catch { }
        Prop("Where", it.Folder);
        var (dirs, files) = await Task.Run(() =>
        {
            int d = 0, f = 0;
            try
            {
                foreach (var e in new DirectoryInfo(it.FullPath).EnumerateFileSystemInfos("*", new EnumerationOptions { IgnoreInaccessible = true }))
                {
                    if (e is DirectoryInfo) d++; else f++;
                    if (d + f > 100_000) break;
                }
            }
            catch { }
            return (d, f);
        });
        if (gen != _gen) return;
        Prop("Contains", $"{dirs:N0} folders, {files:N0} files");
        ExtraBtn.Content = "Calculate size";
        ExtraBtn.IsVisible = true;
    }

    void ShowDrive(FileItem it)
    {
        OpenIco.Kind = "drive";
        try
        {
            var d = new DriveInfo(it.FullPath);
            Prop("Capacity", Fmt.Size(d.TotalSize));
            Prop("Used", $"{Fmt.Size(d.TotalSize - d.AvailableFreeSpace)} ({(d.TotalSize - d.AvailableFreeSpace) * 100.0 / d.TotalSize:0}%)");
            Prop("Free", Fmt.Size(d.AvailableFreeSpace));
            Prop("Format", d.DriveFormat);
        }
        catch { }
        LoadThumb(it, _gen, 256);
    }

    void Open_Click(object? s, RoutedEventArgs e)
    {
        if (_item is null) return;
        if (_item.IsDirectory) MainWindow.Current.OpenInBrowser(_item.FullPath);
        else Shell.Open(_item.FullPath);
    }

    async void Extra_Click(object? s, RoutedEventArgs e)
    {
        var it = _item; int gen = _gen;
        if (it is null) return;
        if (it.Extension == ".zip")
        {
            MainWindow.Current.RunCommand("extract");
            return;
        }
        if (!it.IsDirectory) return;
        ExtraBtn.IsEnabled = false;
        ExtraBtn.Content = "Calculating…";
        var ct = _cts?.Token ?? default;
        long size = await Task.Run(() =>
        {
            long total = 0;
            try
            {
                foreach (var f in new DirectoryInfo(it.FullPath).EnumerateFiles("*", new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true, AttributesToSkip = 0 }))
                {
                    if (ct.IsCancellationRequested) break;
                    if ((f.Attributes & Shell.CloudMask) == 0) total += f.Length;
                }
            }
            catch { }
            return total;
        });
        ExtraBtn.IsEnabled = true;
        if (gen != _gen) return;
        ExtraBtn.IsVisible = false;
        Prop("Total size", Fmt.Size(size));
    }
}

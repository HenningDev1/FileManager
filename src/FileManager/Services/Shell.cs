using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using FileManager.Models;

namespace FileManager.Services;

/// <summary>A dedicated STA thread for Windows shell COM calls (thumbnails, icons).</summary>
public sealed class StaWorker
{
    readonly BlockingCollection<Action> _queue = new();

    public StaWorker(string name)
    {
        var t = new Thread(() =>
        {
            foreach (var a in _queue.GetConsumingEnumerable())
            {
                try { a(); } catch { /* keep worker alive */ }
            }
        }) { IsBackground = true, Name = name };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
    }

    public void Post(Action a) => _queue.Add(a);

    public Task<T> Run<T>(Func<T> f)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Add(() =>
        {
            try { tcs.SetResult(f()); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return tcs.Task;
    }
}

public static class Shell
{
    public static readonly StaWorker IconWorker = new("fm-icons");
    public static readonly StaWorker PreviewWorker = new("fm-preview");

    // ---------- Thumbnails & icons via IShellItemImageFactory ----------

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm);
    }

    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public IntPtr bmBits; }

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFOHEADER
    {
        public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHCreateItemFromParsingName(string path, IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory? ppv);

    [DllImport("gdi32.dll")] static extern int GetObject(IntPtr h, int c, out BITMAP bm);
    [DllImport("gdi32.dll")] static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER bmi, uint usage);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);

    const int SIIGBF_RESIZETOFIT = 0x0, SIIGBF_BIGGERSIZEOK = 0x1, SIIGBF_ICONONLY = 0x4;

    /// <summary>Must be called on an STA thread (use IconWorker / PreviewWorker).</summary>
    public static Bitmap? GetImage(string path, int size, bool iconOnly)
    {
        IShellItemImageFactory? f = null;
        IntPtr hbm = IntPtr.Zero;
        try
        {
            if (SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out f) != 0 || f is null)
                return null;
            int flags = iconOnly ? SIIGBF_ICONONLY | SIIGBF_BIGGERSIZEOK : SIIGBF_RESIZETOFIT;
            if (f.GetImage(new SIZE { cx = size, cy = size }, flags, out hbm) != 0 || hbm == IntPtr.Zero)
                return null;
            return FromHBitmap(hbm);
        }
        catch { return null; }
        finally
        {
            if (hbm != IntPtr.Zero) DeleteObject(hbm);
            if (f is not null) Marshal.ReleaseComObject(f);
        }
    }

    static Bitmap? FromHBitmap(IntPtr hbm)
    {
        if (GetObject(hbm, Marshal.SizeOf<BITMAP>(), out var bm) == 0) return null;
        int w = bm.bmWidth, h = Math.Abs(bm.bmHeight);
        if (w <= 0 || h <= 0) return null;
        var bmi = new BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32,
        };
        var buf = new byte[w * h * 4];
        var dc = GetDC(IntPtr.Zero);
        try { if (GetDIBits(dc, hbm, 0, (uint)h, buf, ref bmi, 0) == 0) return null; }
        finally { ReleaseDC(IntPtr.Zero, dc); }

        bool anyAlpha = false;
        for (int i = 3; i < buf.Length; i += 4) if (buf[i] != 0) { anyAlpha = true; break; }
        if (!anyAlpha) for (int i = 3; i < buf.Length; i += 4) buf[i] = 255;

        var wb = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var fb = wb.Lock())
        {
            for (int y = 0; y < h; y++)
                Marshal.Copy(buf, y * w * 4, fb.Address + y * fb.RowBytes, w * 4);
        }
        return wb;
    }

    // ---------- Type names ----------

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFILEINFO
    {
        public IntPtr hIcon; public int iIcon; public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SHGetFileInfo(string path, uint attr, ref SHFILEINFO psfi, uint cb, uint flags);

    static readonly ConcurrentDictionary<string, string> TypeNames = new(StringComparer.OrdinalIgnoreCase);

    public static string TypeName(string ext) => TypeNames.GetOrAdd(ext, e =>
    {
        if (string.IsNullOrEmpty(e)) return "File";
        try
        {
            var info = new SHFILEINFO();
            SHGetFileInfo("file" + e, 0x80, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), 0x400 | 0x10);
            if (!string.IsNullOrWhiteSpace(info.szTypeName)) return info.szTypeName;
        }
        catch { }
        return e.TrimStart('.').ToUpperInvariant() + " File";
    });

    // ---------- Actions ----------

    public static void Open(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch { }
    }

    public static void Reveal(string path)
    {
        try
        {
            if (File.Exists(path) || (Directory.Exists(path) && Path.GetPathRoot(path) != path))
                Process.Start("explorer.exe", $"/select,\"{path}\"");
            else
                Process.Start("explorer.exe", $"\"{path}\"");
        }
        catch { }
    }

    public static void Terminal(string dir)
    {
        try { Process.Start(new ProcessStartInfo("wt.exe", $"-d \"{dir.TrimEnd('\\')}\\.\"") { UseShellExecute = true }); }
        catch
        {
            try { Process.Start(new ProcessStartInfo("powershell.exe") { WorkingDirectory = dir, UseShellExecute = true }); }
            catch { }
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern bool SHObjectProperties(IntPtr hwnd, uint type, string name, string? page);

    public static void Properties(string path)
    {
        try { SHObjectProperties(IntPtr.Zero, 0x2, path, null); } catch { }
    }

    public static bool IsLink(string dir)
    {
        try { return new DirectoryInfo(dir).LinkTarget is not null; }
        catch { return true; }
    }

    /// <summary>Cloud placeholders (OneDrive etc.) don't occupy local disk space.</summary>
    public const FileAttributes CloudMask = FileAttributes.Offline | (FileAttributes)0x00400000 | (FileAttributes)0x00040000;
}

/// <summary>Caches small shell icons per extension and loads them in the background.</summary>
public static class IconCache
{
    static readonly ConcurrentDictionary<string, Bitmap?> Cache = new(StringComparer.OrdinalIgnoreCase);
    static readonly HashSet<string> PerFile = new(StringComparer.OrdinalIgnoreCase) { ".exe", ".ico", ".lnk", ".url", ".msc", ".appref-ms", ".scr", ".cpl" };

    static string Key(string path, bool dir, bool drive, string ext) =>
        drive ? path : dir ? "<dir>" : PerFile.Contains(ext) ? path : ext.Length == 0 ? "<noext>" : ext;

    public static void Request(FileItem item, Func<bool> stale) =>
        Request(item.FullPath, item.IsDirectory, item.IsDrive, item.Extension, b => item.Icon = b, stale);

    public static void Request(string path, bool dir, bool drive, string ext, Action<Bitmap?> apply, Func<bool> stale)
    {
        var key = Key(path, dir, drive, ext);
        if (Cache.TryGetValue(key, out var bmp)) { apply(bmp); return; }
        Shell.IconWorker.Post(() =>
        {
            if (stale()) return;
            if (!Cache.TryGetValue(key, out var b))
            {
                b = Shell.GetImage(path, 32, iconOnly: true);
                Cache[key] = b;
            }
            Dispatcher.UIThread.Post(() => apply(b));
        });
    }

    public static void RequestThumb(FileItem item, Func<bool> stale)
    {
        Shell.IconWorker.Post(() =>
        {
            if (stale()) return;
            var b = Shell.GetImage(item.FullPath, 128, iconOnly: false);
            if (b is not null) Dispatcher.UIThread.Post(() => item.Thumb = b);
        });
    }
}

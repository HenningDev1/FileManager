using System.IO.Compression;
using VB = Microsoft.VisualBasic.FileIO;

namespace FileManager.Services;

/// <summary>
/// File operations. Copy/move/delete go through the Windows shell so you get the native
/// progress dialog, conflict handling ("Replace or skip") and the Recycle Bin.
/// </summary>
public static class FileOps
{
    static Task RunSta(Action a)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var t = new Thread(() =>
        {
            try { a(); tcs.SetResult(); }
            catch (Exception ex) { tcs.SetException(ex); }
        }) { IsBackground = true };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        return tcs.Task;
    }

    static string Norm(string p) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p));

    static bool SameDir(string src, string destDir)
    {
        var parent = Path.GetDirectoryName(Norm(src));
        return parent is not null && string.Equals(Norm(parent), Norm(destDir), StringComparison.OrdinalIgnoreCase);
    }

    static bool IsInside(string dest, string src)
    {
        var d = Norm(dest); var s = Norm(src);
        return d.Equals(s, StringComparison.OrdinalIgnoreCase) || d.StartsWith(s + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    public static Task Copy(IReadOnlyList<string> sources, string destDir) => RunSta(() =>
    {
        foreach (var s in sources)
        {
            var name = Path.GetFileName(Norm(s));
            var target = Path.Combine(destDir, name);
            if (SameDir(s, destDir)) target = UniqueCopyName(target);
            if (Directory.Exists(s))
            {
                if (IsInside(destDir, s)) throw new IOException($"Can't copy \"{name}\" into itself.");
                VB.FileSystem.CopyDirectory(s, target, VB.UIOption.AllDialogs, VB.UICancelOption.DoNothing);
            }
            else if (File.Exists(s))
                VB.FileSystem.CopyFile(s, target, VB.UIOption.AllDialogs, VB.UICancelOption.DoNothing);
        }
    });

    public static Task Move(IReadOnlyList<string> sources, string destDir) => RunSta(() =>
    {
        foreach (var s in sources)
        {
            if (SameDir(s, destDir)) continue;
            var name = Path.GetFileName(Norm(s));
            var target = Path.Combine(destDir, name);
            if (Directory.Exists(s))
            {
                if (IsInside(destDir, s)) throw new IOException($"Can't move \"{name}\" into itself.");
                VB.FileSystem.MoveDirectory(s, target, VB.UIOption.AllDialogs, VB.UICancelOption.DoNothing);
            }
            else if (File.Exists(s))
                VB.FileSystem.MoveFile(s, target, VB.UIOption.AllDialogs, VB.UICancelOption.DoNothing);
        }
    });

    public static Task Delete(IReadOnlyList<string> paths, bool recycle) => RunSta(() =>
    {
        var mode = recycle ? VB.RecycleOption.SendToRecycleBin : VB.RecycleOption.DeletePermanently;
        foreach (var p in paths)
        {
            if (Directory.Exists(p))
                VB.FileSystem.DeleteDirectory(p, VB.UIOption.OnlyErrorDialogs, mode, VB.UICancelOption.DoNothing);
            else if (File.Exists(p))
                VB.FileSystem.DeleteFile(p, VB.UIOption.OnlyErrorDialogs, mode, VB.UICancelOption.DoNothing);
        }
    });

    public static void Rename(string from, string to)
    {
        if (string.Equals(from, to, StringComparison.Ordinal)) return;
        bool dir = Directory.Exists(from);
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
        {
            // Case-only rename needs a hop through a temporary name.
            var tmp = from + ".fm-" + Guid.NewGuid().ToString("N")[..6];
            if (dir) { Directory.Move(from, tmp); Directory.Move(tmp, to); }
            else { File.Move(from, tmp); File.Move(tmp, to); }
            return;
        }
        if (File.Exists(to) || Directory.Exists(to)) throw new IOException($"\"{Path.GetFileName(to)}\" already exists.");
        if (dir) Directory.Move(from, to); else File.Move(from, to);
    }

    /// <summary>Renames many items safely (two-phase, so swapping names inside the set works).</summary>
    public static (int ok, List<string> errors) RenameMany(IReadOnlyList<(string from, string to)> ops)
    {
        var errors = new List<string>();
        var staged = new List<(string tmp, string to, string from)>();
        foreach (var (from, to) in ops)
        {
            if (string.Equals(from, to, StringComparison.Ordinal)) continue;
            try
            {
                var tmp = Path.Combine(Path.GetDirectoryName(from)!, ".fm-rename-" + Guid.NewGuid().ToString("N")[..10]);
                if (Directory.Exists(from)) Directory.Move(from, tmp); else File.Move(from, tmp);
                staged.Add((tmp, to, from));
            }
            catch (Exception ex) { errors.Add($"{Path.GetFileName(from)}: {ex.Message}"); }
        }
        int ok = 0;
        foreach (var (tmp, to, from) in staged)
        {
            try
            {
                if (Directory.Exists(tmp)) Directory.Move(tmp, to); else File.Move(tmp, to);
                ok++;
            }
            catch (Exception ex)
            {
                errors.Add($"{Path.GetFileName(from)}: {ex.Message}");
                try { if (Directory.Exists(tmp)) Directory.Move(tmp, from); else File.Move(tmp, from); } catch { }
            }
        }
        return (ok, errors);
    }

    public static Task<string> Zip(IReadOnlyList<string> paths, string destDir) => Task.Run(() =>
    {
        string baseName = paths.Count == 1
            ? (Directory.Exists(paths[0]) ? Path.GetFileName(Norm(paths[0])) : Path.GetFileNameWithoutExtension(paths[0]))
            : "Archive";
        var zipPath = Unique(Path.Combine(destDir, baseName + ".zip"));
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var p in paths)
        {
            if (Directory.Exists(p))
            {
                var root = Path.GetFileName(Norm(p));
                foreach (var f in Directory.EnumerateFiles(p, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
                {
                    var entry = Path.Combine(root, Path.GetRelativePath(p, f)).Replace('\\', '/');
                    zip.CreateEntryFromFile(f, entry, CompressionLevel.Optimal);
                }
            }
            else if (File.Exists(p) && !string.Equals(p, zipPath, StringComparison.OrdinalIgnoreCase))
                zip.CreateEntryFromFile(p, Path.GetFileName(p), CompressionLevel.Optimal);
        }
        return zipPath;
    });

    public static Task<List<string>> Extract(IReadOnlyList<string> zips) => Task.Run(() =>
    {
        var outDirs = new List<string>();
        foreach (var z in zips)
        {
            var dest = Unique(Path.Combine(Path.GetDirectoryName(z)!, Path.GetFileNameWithoutExtension(z)));
            ZipFile.ExtractToDirectory(z, dest);
            outDirs.Add(dest);
        }
        return outDirs;
    });

    /// <summary>"name.ext" → "name (2).ext" if taken.</summary>
    public static string Unique(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path)!;
        var stem = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        if (Directory.Exists(path)) { stem = Path.GetFileName(path); ext = ""; }
        for (int i = 2; ; i++)
        {
            var c = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!File.Exists(c) && !Directory.Exists(c)) return c;
        }
    }

    static string UniqueCopyName(string path)
    {
        var dir = Path.GetDirectoryName(path)!;
        bool isDir = Directory.Exists(path);
        var stem = isDir ? Path.GetFileName(path) : Path.GetFileNameWithoutExtension(path);
        var ext = isDir ? "" : Path.GetExtension(path);
        var c = Path.Combine(dir, $"{stem} - Copy{ext}");
        for (int i = 2; File.Exists(c) || Directory.Exists(c); i++)
            c = Path.Combine(dir, $"{stem} - Copy ({i}){ext}");
        return c;
    }
}

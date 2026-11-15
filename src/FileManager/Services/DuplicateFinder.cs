using System.Collections.Concurrent;
using System.IO.Enumeration;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using FileManager.Models;

namespace FileManager.Services;

/// <summary>
/// Finds duplicate files by content (size → partial hash → full SHA-256) and recognises
/// copy-style names such as "photo_1.jpg", "photo (2).jpg", "photo - Copy.jpg", "Kopie von photo.jpg".
/// It never deletes anything; it only marks which copies look unnecessary.
/// </summary>
public static class DuplicateFinder
{
    const int Chunk = 16 * 1024;

    static readonly Regex[] Suffixes =
    [
        new(@"\s*-\s*(copy|kopie|copie|copia|kopia)(\s*\(\d+\))?$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\s+(copy|kopie)(\s*\d+)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\s*\(\d{1,3}\)$", RegexOptions.Compiled),
        new(@"[_\-\s]\d{1,2}$", RegexOptions.Compiled),
        new(@"[_\-](copy|kopie)$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    ];
    static readonly Regex Prefix = new(@"^(copy of|kopie von|copie de)\s+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Returns the name with copy markers removed, and the marker that was found (if any).</summary>
    public static (string Normalized, string? Suffix) Normalize(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        string? found = null;
        for (int pass = 0; pass < 3; pass++)
        {
            bool changed = false;
            var pm = Prefix.Match(stem);
            if (pm.Success && pm.Length < stem.Length) { found ??= pm.Value.Trim(); stem = stem[pm.Length..]; changed = true; }
            foreach (var r in Suffixes)
            {
                var m = r.Match(stem);
                if (m.Success && m.Index > 0) { found ??= m.Value.Trim(); stem = stem[..m.Index]; changed = true; break; }
            }
            if (!changed) break;
        }
        return ((stem + ext).ToLowerInvariant(), found);
    }

    // Every Steam library (on any drive) keeps its games in "<library>\steamapps\...".
    // Games ship many identical asset files on purpose, so they are always skipped.
    // Epic Games installs into "...\Epic Games\<game>" by default; custom locations are listed in the launcher's manifests.
    static readonly string[] GameFolderNames = ["steamapps", "Epic Games"];

    static bool IsGameFolder(ReadOnlySpan<char> name)
    {
        foreach (var n in GameFolderNames)
            if (name.Equals(n, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    static bool IsGamePath(ReadOnlySpan<char> dir, List<string> epicDirs)
    {
        foreach (var n in GameFolderNames)
            if (dir.Contains($"\\{n}\\", StringComparison.OrdinalIgnoreCase) || dir.EndsWith($"\\{n}", StringComparison.OrdinalIgnoreCase))
                return true;
        foreach (var e in epicDirs)
            if (dir.StartsWith(e, StringComparison.OrdinalIgnoreCase) && (dir.Length == e.Length || dir[e.Length] == '\\'))
                return true;
        return false;
    }

    /// <summary>Install folders of all Epic Games titles, read from the Epic launcher's .item manifests.</summary>
    static List<string> EpicInstallDirs()
    {
        var list = new List<string>();
        try
        {
            var manifests = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Epic", "EpicGamesLauncher", "Data", "Manifests");
            if (!Directory.Exists(manifests)) return list;
            foreach (var f in Directory.EnumerateFiles(manifests, "*.item"))
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(f));
                    if (doc.RootElement.TryGetProperty("InstallLocation", out var loc) && loc.GetString() is { Length: > 3 } p)
                        list.Add(Path.TrimEndingDirectorySeparator(Path.GetFullPath(p)));
                }
                catch { }
            }
        }
        catch { }
        return list;
    }

    readonly record struct F(string Path, long Size, DateTime Modified);

    public static Task<List<DupGroup>> FindAsync(IReadOnlyList<string> roots, long minSize, bool includeNameMatches,
        bool includeHidden, Action<string> progress, CancellationToken ct) => Task.Run(() =>
    {
        // 1) Collect files
        var files = new List<F>();
        var epicDirs = EpicInstallDirs();
        var opts = new EnumerationOptions
        {
            RecurseSubdirectories = true, IgnoreInaccessible = true,
            AttributesToSkip = includeHidden ? 0 : FileAttributes.Hidden | FileAttributes.System,
        };
        foreach (var root in roots)
        {
            var en = new FileSystemEnumerable<F>(root, (ref FileSystemEntry e) => new F(e.ToFullPath(), e.Length, e.LastWriteTimeUtc.LocalDateTime), opts)
            {
                ShouldIncludePredicate = (ref FileSystemEntry e) => !e.IsDirectory && e.Length >= Math.Max(1, minSize) && (e.Attributes & Shell.CloudMask) == 0
                    && !IsGamePath(e.Directory, epicDirs),
                ShouldRecursePredicate = (ref FileSystemEntry e) => !IsGameFolder(e.FileName) && !IsGamePath(e.ToFullPath(), epicDirs)
                    && ((e.Attributes & FileAttributes.ReparsePoint) == 0 || !Shell.IsLink(e.ToFullPath())),
            };
            foreach (var f in en)
            {
                ct.ThrowIfCancellationRequested();
                files.Add(f);
                if (files.Count % 5000 == 0) progress($"Collecting files… {files.Count:N0}");
            }
        }

        // 2) Same size
        var sameSize = files.GroupBy(f => f.Size).Where(g => g.Count() > 1).SelectMany(g => g).ToList();
        progress($"Found {files.Count:N0} files · comparing {sameSize.Count:N0} with matching sizes…");

        // 3) Partial hash (first + last 16 KB)
        var partial = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int done = 0;
        Parallel.ForEach(sameSize, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, f =>
        {
            var h = PartialHash(f.Path, f.Size);
            if (h is not null) partial[f.Path] = h;
            if (Interlocked.Increment(ref done) % 500 == 0) progress($"Quick-checking files… {done:N0} / {sameSize.Count:N0}");
        });

        var candidates = sameSize
            .Where(f => partial.ContainsKey(f.Path))
            .GroupBy(f => (f.Size, partial[f.Path]))
            .Where(g => g.Count() > 1)
            .ToList();

        // 4) Full hash for files larger than what the partial hash already covered
        var needFull = candidates.Where(g => g.Key.Size > 2 * Chunk).SelectMany(g => g).ToList();
        var full = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        done = 0;
        Parallel.ForEach(needFull, new ParallelOptions { MaxDegreeOfParallelism = 3, CancellationToken = ct }, f =>
        {
            var h = FullHash(f.Path);
            if (h is not null) full[f.Path] = h;
            if (Interlocked.Increment(ref done) % 50 == 0) progress($"Verifying content… {done:N0} / {needFull.Count:N0}");
        });

        var groups = new List<DupGroup>();
        var inContentGroup = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in candidates)
        {
            IEnumerable<IGrouping<string, F>> exact = g.Key.Size > 2 * Chunk
                ? g.Where(f => full.ContainsKey(f.Path)).GroupBy(f => full[f.Path])
                : g.GroupBy(_ => "");
            foreach (var set in exact.Where(s => s.Count() > 1))
            {
                var grp = BuildGroup(set.ToList(), nameOnly: false);
                groups.Add(grp);
                foreach (var f in set) inContentGroup.Add(f.Path);
            }
        }

        // 5) Optional: copy-style names next to an original, but with different content
        if (includeNameMatches)
        {
            var byName = files
                .Where(f => !inContentGroup.Contains(f.Path))
                .GroupBy(f => (Dir: Path.GetDirectoryName(f.Path)!.ToLowerInvariant(), Name: Normalize(Path.GetFileName(f.Path)).Normalized));
            foreach (var g in byName)
            {
                if (g.Count() < 2) continue;
                var list = g.ToList();
                bool hasOriginal = list.Any(f => Path.GetFileName(f.Path).Equals(g.Key.Name, StringComparison.OrdinalIgnoreCase));
                bool hasCopy = list.Any(f => Normalize(Path.GetFileName(f.Path)).Suffix is not null);
                if (hasOriginal && hasCopy) groups.Add(BuildGroup(list, nameOnly: true));
            }
        }

        return groups.OrderByDescending(g => g.IsNameOnly ? 0 : 1).ThenByDescending(g => g.Wasted).ThenByDescending(g => g.Size).ToList();
    }, ct);

    static DupGroup BuildGroup(List<F> files, bool nameOnly)
    {
        var grp = new DupGroup { Size = files.Max(f => f.Size), IsNameOnly = nameOnly };
        var ranked = files
            .Select(f => (f, n: Normalize(Path.GetFileName(f.Path))))
            .OrderBy(x => x.n.Suffix is null ? 0 : 1)
            .ThenBy(x => x.f.Modified)
            .ThenBy(x => Path.GetFileName(x.f.Path).Length)
            .ToList();
        var keeperDir = Path.GetDirectoryName(ranked[0].f.Path);
        for (int i = 0; i < ranked.Count; i++)
        {
            var (f, n) = ranked[i];
            bool keeper = i == 0;
            string reason;
            if (keeper) reason = nameOnly ? "Original name" : "Suggested to keep";
            else if (nameOnly) reason = $"Similar name ({n.Suffix}) but different content";
            else if (n.Suffix is not null) reason = $"Copy-style name ({n.Suffix}), identical content";
            else if (string.Equals(Path.GetDirectoryName(f.Path), keeperDir, StringComparison.OrdinalIgnoreCase)) reason = "Identical content, different name";
            else reason = "Identical content in another folder";

            bool suggested = !keeper && !nameOnly;
            grp.Files.Add(new DupEntry
            {
                FullPath = f.Path, Group = grp, Size = f.Size, Modified = f.Modified, Suffix = n.Suffix,
                IsKeeper = keeper, Reason = reason, Suggested = suggested,
            });
        }
        return grp;
    }

    static string? PartialHash(string path, long size)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.RandomAccess);
            var buf = new byte[(int)Math.Min(size, 2 * Chunk)];
            int read = fs.ReadAtLeast(buf.AsSpan(0, (int)Math.Min(size, Chunk)), (int)Math.Min(size, Chunk), false);
            if (size > 2 * Chunk)
            {
                fs.Seek(-Chunk, SeekOrigin.End);
                read += fs.ReadAtLeast(buf.AsSpan(Chunk, Chunk), Chunk, false);
            }
            else if (size > Chunk)
            {
                read += fs.ReadAtLeast(buf.AsSpan(Chunk, (int)(size - Chunk)), (int)(size - Chunk), false);
            }
            return Convert.ToHexString(SHA256.HashData(buf.AsSpan(0, read)));
        }
        catch { return null; }
    }

    static string? FullHash(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 20, FileOptions.SequentialScan);
            return Convert.ToHexString(SHA256.HashData(fs));
        }
        catch { return null; }
    }
}

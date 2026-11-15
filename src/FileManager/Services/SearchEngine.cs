using System.Collections.Concurrent;
using System.IO.Enumeration;
using FileManager.Models;

namespace FileManager.Services;

public sealed class SearchQuery
{
    public string Text { get; init; } = "";
    public int Kind { get; init; }              // 0 = all, 1 = files, 2 = folders
    public HashSet<string>? Extensions { get; init; }
    public long MinSize { get; init; }
    public List<string> Roots { get; init; } = [];
    public bool IncludeHidden { get; init; }
    public int Limit { get; init; } = 20000;
}

/// <summary>
/// Fast parallel file-name search. Uses the low-allocation FileSystemEnumerable API,
/// matches on the raw name span, and fans out across sub-folders on all CPU cores.
/// </summary>
public static class SearchEngine
{
    readonly record struct Hit(string Path, bool IsDir, long Length, DateTime Modified);

    public static Task<int> RunAsync(SearchQuery q, ConcurrentQueue<FileItem> output, CancellationToken ct) => Task.Run(() =>
    {
        var terms = q.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        int count = 0;
        var skip = q.IncludeHidden ? 0 : FileAttributes.Hidden | FileAttributes.System;

        bool NameMatches(ReadOnlySpan<char> name)
        {
            foreach (var t in terms)
            {
                if (t.IndexOfAny(['*', '?']) >= 0)
                {
                    if (!FileSystemName.MatchesSimpleExpression(t, name, ignoreCase: true)) return false;
                }
                else if (!name.Contains(t, StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
        }

        bool Include(ref FileSystemEntry e)
        {
            if (e.IsDirectory ? q.Kind == 1 : q.Kind == 2) return false;
            if (!e.IsDirectory && e.Length < q.MinSize) return false;
            if (q.MinSize > 0 && e.IsDirectory) return false;
            if (!NameMatches(e.FileName)) return false;
            if (q.Extensions is not null)
            {
                if (e.IsDirectory) return false;
                var ext = Path.GetExtension(e.FileName);
                if (!q.Extensions.Contains(ext.ToString())) return false;
            }
            return true;
        }

        void Emit(in Hit h)
        {
            var ext = h.IsDir ? "" : Path.GetExtension(h.Path).ToLowerInvariant();
            output.Enqueue(new FileItem
            {
                Name = Path.GetFileName(h.Path), FullPath = h.Path, IsDirectory = h.IsDir, Size = h.Length,
                Modified = h.Modified, Extension = ext, TypeName = h.IsDir ? "Folder" : Shell.TypeName(ext),
            });
            Interlocked.Increment(ref count);
        }

        IEnumerable<Hit> Enumerate(string dir, bool recurse)
        {
            var opts = new EnumerationOptions
            {
                RecurseSubdirectories = recurse, IgnoreInaccessible = true, AttributesToSkip = skip,
                ReturnSpecialDirectories = false, BufferSize = 64 * 1024,
            };
            return new FileSystemEnumerable<Hit>(dir,
                (ref FileSystemEntry e) => new Hit(e.ToFullPath(), e.IsDirectory, e.Length, e.LastWriteTimeUtc.LocalDateTime), opts)
            {
                ShouldIncludePredicate = (ref FileSystemEntry e) => Include(ref e),
                ShouldRecursePredicate = (ref FileSystemEntry e) =>
                    (e.Attributes & FileAttributes.ReparsePoint) == 0 || !Shell.IsLink(e.ToFullPath()),
            };
        }

        // Breadth-first split until there are enough folders to parallelise over.
        var frontier = q.Roots.Where(Directory.Exists).ToList();
        for (int depth = 0; depth < 3 && frontier.Count is > 0 and < 64; depth++)
        {
            var next = new List<string>();
            foreach (var d in frontier)
            {
                if (ct.IsCancellationRequested || count >= q.Limit) return count;
                try
                {
                    foreach (var h in Enumerate(d, false)) Emit(h);
                    var opts = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = skip };
                    foreach (var sub in new FileSystemEnumerable<string>(d, (ref FileSystemEntry e) => e.ToFullPath(), opts)
                             { ShouldIncludePredicate = (ref FileSystemEntry e) => e.IsDirectory && ((e.Attributes & FileAttributes.ReparsePoint) == 0 || !Shell.IsLink(e.ToFullPath())) })
                        next.Add(sub);
                }
                catch { /* inaccessible */ }
            }
            frontier = next;
        }

        try
        {
            Parallel.ForEach(frontier, new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Environment.ProcessorCount },
                (dir, state) =>
                {
                    try
                    {
                        // The folder itself was already matched by the previous level, so only enumerate children.
                        foreach (var h in Enumerate(dir, true))
                        {
                            if (ct.IsCancellationRequested || count >= q.Limit) { state.Stop(); return; }
                            Emit(h);
                        }
                    }
                    catch { }
                });
        }
        catch (OperationCanceledException) { }
        return count;
    }, ct);
}

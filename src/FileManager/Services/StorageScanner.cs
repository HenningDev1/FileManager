using System.Collections.Concurrent;
using System.IO.Enumeration;
using FileManager.Models;

namespace FileManager.Services;

/// <summary>TreeSize-style folder size scanner. Parallel, skips junctions and cloud-only placeholders.</summary>
public sealed class StorageScanner
{
    public long FilesScanned, BytesScanned, DeniedFolders;

    readonly object _topLock = new();
    readonly PriorityQueue<(string Path, long Size), long> _top = new();
    long _topMin;
    const int TopCount = 150;

    public readonly ConcurrentDictionary<string, (long Size, long Count)> Types = new(StringComparer.OrdinalIgnoreCase);
    CancellationToken _ct;

    readonly record struct Entry(string Name, bool IsDir, long Length, FileAttributes Attr);

    static readonly EnumerationOptions Opts = new()
    {
        RecurseSubdirectories = false, IgnoreInaccessible = false, AttributesToSkip = 0, ReturnSpecialDirectories = false, BufferSize = 64 * 1024,
    };

    public DirNode Scan(string root, CancellationToken ct)
    {
        _ct = ct;
        var node = new DirNode { Name = root, FullPath = root };
        ScanDir(node, 0);
        return node;
    }

    void ScanDir(DirNode node, int depth)
    {
        if (_ct.IsCancellationRequested) return;
        var subs = new List<DirNode>();
        long size = 0, files = 0;
        var localTypes = new Dictionary<string, (long, long)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var en = new FileSystemEnumerable<Entry>(node.FullPath,
                (ref FileSystemEntry e) => new Entry(e.FileName.ToString(), e.IsDirectory, e.Length, e.Attributes), Opts);
            foreach (var e in en)
            {
                if (e.IsDir)
                {
                    var p = Path.Combine(node.FullPath, e.Name);
                    if ((e.Attr & FileAttributes.ReparsePoint) != 0 && Shell.IsLink(p)) continue;
                    subs.Add(new DirNode { Name = e.Name, FullPath = p, Parent = node });
                }
                else
                {
                    long len = (e.Attr & Shell.CloudMask) != 0 ? 0 : e.Length;
                    size += len; files++;
                    var ext = Path.GetExtension(e.Name);
                    if (ext.Length == 0 || ext.Length > 12) ext = "(no extension)";
                    localTypes[ext] = localTypes.TryGetValue(ext, out var t) ? (t.Item1 + len, t.Item2 + 1) : (len, 1);
                    if (len > Volatile.Read(ref _topMin)) AddTop(Path.Combine(node.FullPath, e.Name), len);
                }
            }
        }
        catch
        {
            node.Denied = true;
            Interlocked.Increment(ref DeniedFolders);
        }

        Interlocked.Add(ref FilesScanned, files);
        Interlocked.Add(ref BytesScanned, size);
        foreach (var (k, v) in localTypes)
            Types.AddOrUpdate(k, v, (_, o) => (o.Size + v.Item1, o.Count + v.Item2));

        if (depth < 4 && subs.Count > 1)
            Parallel.ForEach(subs, s => ScanDir(s, depth + 1));
        else
            foreach (var s in subs) ScanDir(s, depth + 1);

        node.Size = size + subs.Sum(s => s.Size);
        node.Files = files + subs.Sum(s => s.Files);
        node.Folders = subs.Count + subs.Sum(s => s.Folders);
        if (files > 0 && subs.Count > 0)
            subs.Add(new DirNode { Name = $"[{files:N0} files in this folder]", FullPath = node.FullPath, Parent = node, IsFilesNode = true, Size = size, Files = files });
        subs.Sort((a, b) => b.Size.CompareTo(a.Size));
        node.Children = subs;
    }

    void AddTop(string path, long len)
    {
        lock (_topLock)
        {
            if (_top.Count < TopCount) _top.Enqueue((path, len), len);
            else if (len > _top.Peek().Size) _top.EnqueueDequeue((path, len), len);
            Volatile.Write(ref _topMin, _top.Count < TopCount ? 0 : _top.Peek().Size);
        }
    }

    public List<(string Path, long Size)> TopFiles()
    {
        lock (_topLock) return _top.UnorderedItems.Select(x => x.Element).OrderByDescending(x => x.Size).ToList();
    }
}

using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;

namespace FileManager.Models;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }
}

/// <summary>A file, folder or drive shown in a list.</summary>
public sealed class FileItem : Observable
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public bool IsDirectory { get; init; }
    public bool IsDrive { get; init; }
    public long Size { get; init; }
    public DateTime Modified { get; init; }
    public string Extension { get; init; } = "";
    public string TypeName { get; init; } = "";
    public string? Detail { get; init; }

    public string SizeText => IsDrive ? Detail ?? "" : IsDirectory ? "" : Fmt.Size(Size);
    public string ModifiedText => IsDrive || Modified == default ? "" : Fmt.Date(Modified);
    public string Folder => Path.GetDirectoryName(FullPath) ?? "";

    Bitmap? _icon, _thumb;
    public Bitmap? Icon { get => _icon; set { if (Set(ref _icon, value)) Raise(nameof(Display)); } }
    public Bitmap? Thumb { get => _thumb; set { if (Set(ref _thumb, value)) Raise(nameof(Display)); } }
    public Bitmap? Display => _thumb ?? _icon;
}

/// <summary>A folder node in the storage dashboard tree.</summary>
public sealed class DirNode : Observable
{
    public string Name { get; init; } = "";
    public string FullPath { get; init; } = "";
    public DirNode? Parent { get; init; }
    public bool IsFilesNode { get; init; }
    public long Size { get; set; }
    public long Files { get; set; }
    public long Folders { get; set; }
    public bool Denied { get; set; }
    public List<DirNode> Children { get; set; } = [];

    public double Percent => Parent is null || Parent.Size == 0 ? 100 : Size * 100.0 / Parent.Size;
    public string PercentText => Percent.ToString("0.0") + " %";
    public string SizeText => Fmt.Size(Size);
    public string Info => IsFilesNode ? $"{Files:N0} files" : $"{Files:N0} files · {Folders:N0} folders";
    public string IconKind => IsFilesNode ? "doc" : "folder";

    bool _expanded;
    public bool IsExpanded { get => _expanded; set => Set(ref _expanded, value); }
}

public sealed class TypeStat
{
    public string Ext { get; init; } = "";
    public long Size { get; init; }
    public long Count { get; init; }
    public double Percent { get; init; }
    public string SizeText => Fmt.Size(Size);
    public string CountText => $"{Count:N0} files";
}

public sealed class DriveCard
{
    public string Title { get; init; } = "";
    public string Root { get; init; } = "";
    public double UsedPercent { get; init; }
    public string FreeText { get; init; } = "";
}

public sealed class DupGroup
{
    public List<DupEntry> Files { get; } = [];
    public long Size { get; set; }
    public bool IsNameOnly { get; set; }
    public long Wasted => IsNameOnly ? 0 : Size * (Files.Count - 1);
}

public sealed class DupHeader
{
    public required DupGroup Group { get; init; }
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public bool IsNameOnly => Group.IsNameOnly;
}

public sealed class DupEntry : Observable
{
    public required string FullPath { get; init; }
    public required DupGroup Group { get; init; }
    public string Name => Path.GetFileName(FullPath);
    public string Folder => Path.GetDirectoryName(FullPath) ?? "";
    public long Size { get; init; }
    public DateTime Modified { get; init; }
    public bool IsKeeper { get; set; }
    public bool NotKeeper => !IsKeeper;
    public string Reason { get; set; } = "";
    public string? Suffix { get; init; }
    public bool Suggested { get; set; }
    public string SizeText => Fmt.Size(Size);
    public string ModifiedText => Fmt.Date(Modified);

    bool _checked;
    public bool IsChecked { get => _checked; set => Set(ref _checked, value); }

    Bitmap? _icon;
    public Bitmap? Icon { get => _icon; set => Set(ref _icon, value); }
}

public sealed class RenamePreview
{
    public string Old { get; init; } = "";
    public string New { get; init; } = "";
    public string? Error { get; init; }
    public bool HasError => Error is not null;
    public bool Changed => Old != New;
}

public static class Fmt
{
    static readonly string[] Units = ["B", "KB", "MB", "GB", "TB", "PB"];

    public static string Size(long bytes)
    {
        if (bytes < 1024) return bytes + " B";
        double v = bytes; int i = 0;
        while (v >= 1024 && i < Units.Length - 1) { v /= 1024; i++; }
        return (v >= 100 ? v.ToString("0") : v >= 10 ? v.ToString("0.#") : v.ToString("0.##")) + " " + Units[i];
    }

    public static string Date(DateTime d) => d.ToString("g");

    public static string Duration(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
}

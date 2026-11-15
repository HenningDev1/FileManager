namespace FileManager.Services;

public static class FileTypes
{
    static HashSet<string> Set(params string[] e) => new(e, StringComparer.OrdinalIgnoreCase);

    public static readonly HashSet<string> Images = Set(".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".ico", ".jfif");
    public static readonly HashSet<string> ShellImages = Set(".heic", ".heif", ".tif", ".tiff", ".raw", ".cr2", ".cr3", ".nef", ".arw", ".dng", ".psd", ".svg", ".avif");
    public static readonly HashSet<string> Video = Set(".mp4", ".mkv", ".mov", ".avi", ".wmv", ".webm", ".m4v", ".flv", ".mpg", ".mpeg", ".3gp", ".ts", ".mts", ".m2ts");
    public static readonly HashSet<string> Audio = Set(".mp3", ".wav", ".flac", ".m4a", ".aac", ".ogg", ".wma", ".opus", ".aiff");
    public static readonly HashSet<string> Archives = Set(".zip", ".7z", ".rar", ".tar", ".gz", ".bz2", ".xz", ".iso", ".cab");
    public static readonly HashSet<string> Documents = Set(".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".odt", ".ods", ".odp", ".rtf", ".txt", ".md", ".csv", ".epub", ".pages", ".key", ".numbers");
    public static readonly HashSet<string> Code = Set(".cs", ".axaml", ".xaml", ".js", ".ts", ".tsx", ".jsx", ".py", ".java", ".c", ".cpp", ".h", ".hpp", ".go", ".rs", ".rb", ".php", ".swift", ".kt", ".lua", ".luau", ".sh", ".ps1", ".psm1", ".bat", ".cmd", ".sql", ".html", ".htm", ".css", ".scss", ".vue", ".svelte", ".csproj", ".sln", ".slnx", ".props", ".targets");
    public static readonly HashSet<string> Text = Set(".txt", ".md", ".markdown", ".log", ".json", ".xml", ".yaml", ".yml", ".ini", ".cfg", ".conf", ".toml", ".csv", ".tsv", ".env", ".gitignore", ".gitattributes", ".editorconfig", ".srt", ".vtt", ".nfo", ".reg", ".manifest", ".config");

    static FileTypes() { Text.UnionWith(Code); }

    public static bool HasThumbnail(string ext) =>
        Images.Contains(ext) || ShellImages.Contains(ext) || Video.Contains(ext) || ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    public static HashSet<string>? Category(int index) => index switch
    {
        1 => Documents,
        2 => new HashSet<string>(Images.Concat(ShellImages), StringComparer.OrdinalIgnoreCase),
        3 => Video,
        4 => Audio,
        5 => Archives,
        6 => Code,
        _ => null,
    };
}

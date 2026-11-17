using System.Text.Json;

namespace FileManager.Services;

public sealed class AppSettings
{
    public string Theme { get; set; } = "Light";
    public string? Accent { get; set; }
    public string Font { get; set; } = "Inter";
    public double FontSize { get; set; } = 13;
    public double Radius { get; set; } = 12;
    /// <summary>Interface scale; 0 = automatic (shrinks a little on small screens).</summary>
    public double UiScale { get; set; }
    public bool ShowHidden { get; set; }
    public bool DualPane { get; set; }
    public bool ShowPreview { get; set; } = true;
    public bool GridView { get; set; }
    public string? LastPath { get; set; }
    public string? LastPath2 { get; set; }
}

public static class Settings
{
    public static AppSettings Current { get; private set; } = new();

    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FileManager");

    static string FilePath => Path.Combine(Folder, "settings.json");

    public static void Load()
    {
        try
        {
            if (File.Exists(FilePath))
                Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { Current = new(); }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* settings are best effort */ }
    }
}

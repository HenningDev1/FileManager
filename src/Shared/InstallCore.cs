using System.Diagnostics;
using Microsoft.Win32;

namespace FileManager.Shared;

/// <summary>
/// Install / uninstall logic shared by the setup program and the app itself
/// (the app handles "--uninstall" so Windows "Apps &amp; features" can remove it).
/// Per-user install: no admin rights needed.
/// </summary>
public static class InstallCore
{
    public const string AppName = "File Manager";
    public const string ExeName = "FileManager.exe";
    public const string Publisher = "HenningDev1";
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\FileManager";

    public static string DefaultDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppName);

    static string StartMenuLink => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk");

    static string DesktopLink => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk");

    static string SettingsDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FileManager");

    public sealed record InstallInfo(string Dir, string Version);

    public static InstallInfo? Detect()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(UninstallKey);
            if (k is null) return null;
            var dir = k.GetValue("InstallLocation") as string;
            var ver = k.GetValue("DisplayVersion") as string;
            return new InstallInfo(string.IsNullOrWhiteSpace(dir) ? DefaultDir : dir, ver ?? "unknown");
        }
        catch { return null; }
    }

    public static void Install(string dir, string version, Func<Stream> payload, bool desktopShortcut, bool startMenu,
        IProgress<(double Value, string Text)> progress)
    {
        progress.Report((0.05, "Closing running instances…"));
        CloseRunning(dir);

        Directory.CreateDirectory(dir);
        var exe = Path.Combine(dir, ExeName);

        progress.Report((0.1, "Copying files…"));
        using (var src = payload())
        using (var dst = new FileStream(exe, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var buf = new byte[1 << 20];
            long total = src.CanSeek ? src.Length : 0, done = 0;
            int n;
            while ((n = src.Read(buf, 0, buf.Length)) > 0)
            {
                dst.Write(buf, 0, n);
                done += n;
                if (total > 0) progress.Report((0.1 + 0.75 * done / total, $"Copying files… {done * 100 / total}%"));
            }
        }

        progress.Report((0.88, "Creating shortcuts…"));
        DeleteQuietly(DesktopLink);
        DeleteQuietly(StartMenuLink);
        if (startMenu) CreateShortcut(StartMenuLink, exe, dir);
        if (desktopShortcut) CreateShortcut(DesktopLink, exe, dir);

        progress.Report((0.95, "Registering…"));
        using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
        {
            k.SetValue("DisplayName", AppName);
            k.SetValue("DisplayVersion", version);
            k.SetValue("Publisher", Publisher);
            k.SetValue("DisplayIcon", exe + ",0");
            k.SetValue("InstallLocation", dir);
            k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
            k.SetValue("UninstallString", $"\"{exe}\" --uninstall");
            k.SetValue("NoModify", 1, RegistryValueKind.DWord);
            k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            k.SetValue("EstimatedSize", (int)(new FileInfo(exe).Length / 1024), RegistryValueKind.DWord);
        }
        progress.Report((1, "Done"));
    }

    /// <summary>Removes the app. Files that are in use (e.g. the running uninstaller) are removed right after exit.</summary>
    public static void Uninstall(bool removeSettings)
    {
        var dir = Detect()?.Dir ?? DefaultDir;
        CloseRunning(dir);
        DeleteQuietly(DesktopLink);
        DeleteQuietly(StartMenuLink);
        try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
        if (removeSettings) { try { Directory.Delete(SettingsDir, true); } catch { } }

        if (Directory.Exists(dir))
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)) DeleteQuietly(f);
            try { Directory.Delete(dir, true); } catch { }
        }
        if (Directory.Exists(dir))
        {
            // Something is still in use (usually our own exe) – remove the folder a moment after we exit.
            try
            {
                Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping 127.0.0.1 -n 3 > nul & rmdir /s /q \"{dir}\"")
                {
                    CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden,
                });
            }
            catch { }
        }
    }

    static void CloseRunning(string dir)
    {
        var self = Environment.ProcessId;
        foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExeName)))
        {
            try
            {
                if (p.Id == self) continue;
                var file = p.MainModule?.FileName;
                if (file is null || !file.StartsWith(dir, StringComparison.OrdinalIgnoreCase)) continue;
                p.CloseMainWindow();
                if (!p.WaitForExit(3000)) p.Kill();
            }
            catch { }
            finally { p.Dispose(); }
        }
    }

    static void CreateShortcut(string link, string target, string workDir)
    {
        try
        {
            var t = Type.GetTypeFromProgID("WScript.Shell");
            if (t is null) return;
            dynamic shell = Activator.CreateInstance(t)!;
            dynamic sc = shell.CreateShortcut(link);
            sc.TargetPath = target;
            sc.WorkingDirectory = workDir;
            sc.IconLocation = target + ",0";
            sc.Description = AppName;
            sc.Save();
        }
        catch { }
    }

    static void DeleteQuietly(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}

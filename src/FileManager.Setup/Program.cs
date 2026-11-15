using Avalonia;

namespace FileManager.Setup;

internal static class Program
{
    public static string[] Args { get; private set; } = [];

    [STAThread]
    public static void Main(string[] args)
    {
        Args = args;
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace().StartWithClassicDesktopLifetime(args);
    }
}

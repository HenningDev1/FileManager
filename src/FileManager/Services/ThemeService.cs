using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace FileManager.Services;

public sealed record ThemeDef(
    string Name, bool Dark,
    string Bg, string Sidebar, string Surface, string Control,
    string Text, string Sub, string Border, string Accent);

public static class ThemeService
{
    public static readonly ThemeDef[] Themes =
    [
        new("Light",    false, "#F5F5F7", "#ECECEF", "#FFFFFF", "#E8E8EC", "#1D1D1F", "#6E6E73", "#E2E2E6", "#007AFF"),
        new("Dark",     true,  "#1C1C1E", "#232325", "#2A2A2D", "#3A3A3D", "#F5F5F7", "#A1A1A6", "#38383B", "#0A84FF"),
        new("Midnight", true,  "#0E1117", "#141923", "#1A202C", "#262E3D", "#E8ECF4", "#8A94A8", "#283042", "#7C8CFF"),
        new("Graphite", true,  "#242424", "#2B2B2B", "#323232", "#444444", "#F2F2F2", "#A8A8A8", "#3F3F3F", "#64D2FF"),
        new("Ocean",    true,  "#0B1A24", "#0F2230", "#13293A", "#1C3A50", "#E3F2FB", "#84A6BC", "#1D3A4F", "#2EC5F0"),
        new("Sand",     false, "#F4EFE8", "#ECE5DA", "#FFFCF7", "#E8E0D4", "#2B2520", "#7A6E62", "#E2D8CA", "#C2410C"),
        new("Mint",     false, "#F0F6F4", "#E4EEEA", "#FFFFFF", "#DFEAE5", "#10201B", "#5B6E67", "#D6E3DE", "#0F9D76"),
        new("Rose",     false, "#FBF3F5", "#F4E6EA", "#FFFFFF", "#F0E1E6", "#2A1A1F", "#7D646C", "#EBD9DF", "#E11D63"),
    ];

    public static readonly (string Name, string Hex)[] Accents =
    [
        ("Blue", "#007AFF"), ("Purple", "#AF52DE"), ("Pink", "#FF2D55"), ("Red", "#FF3B30"),
        ("Orange", "#FF9500"), ("Yellow", "#FFCC00"), ("Green", "#34C759"), ("Teal", "#30B0C7"),
        ("Indigo", "#5856D6"), ("Graphite", "#8E8E93"),
    ];

    public static ThemeDef Current { get; private set; } = Themes[0];

    public static bool TryParse(string? hex, out Color c)
    {
        c = default;
        return !string.IsNullOrWhiteSpace(hex) && Color.TryParse(hex.Trim(), out c);
    }

    public static void Apply(AppSettings s)
    {
        var app = Application.Current;
        if (app is null) return;
        var t = Themes.FirstOrDefault(x => x.Name == s.Theme) ?? Themes[0];
        Current = t;
        var r = app.Resources;
        app.RequestedThemeVariant = t.Dark ? ThemeVariant.Dark : ThemeVariant.Light;

        var accent = TryParse(s.Accent, out var ac) ? ac : Color.Parse(t.Accent);
        var surface = Color.Parse(t.Surface);

        r["BgBrush"] = Brush(t.Bg);
        r["SidebarBrush"] = Brush(t.Sidebar);
        r["SurfaceBrush"] = Brush(t.Surface);
        r["ControlBrush"] = Brush(t.Control);
        r["ElevatedBrush"] = new SolidColorBrush(t.Dark ? Mix(surface, Colors.White, 0.06) : surface);
        r["TextBrush"] = Brush(t.Text);
        r["SubTextBrush"] = Brush(t.Sub);
        r["BorderBrush"] = Brush(t.Border);
        r["HoverBrush"] = new SolidColorBrush(t.Dark ? Color.FromArgb(0x16, 255, 255, 255) : Color.FromArgb(0x0E, 0, 0, 0));
        r["PressedBrush"] = new SolidColorBrush(t.Dark ? Color.FromArgb(0x24, 255, 255, 255) : Color.FromArgb(0x18, 0, 0, 0));
        r["SideActiveBrush"] = new SolidColorBrush(t.Dark ? Color.FromArgb(0x22, 255, 255, 255) : Color.FromArgb(0x14, 0, 0, 0));

        r["AccentColor"] = accent;
        r["AccentBrush"] = new SolidColorBrush(accent);
        r["AccentHoverBrush"] = new SolidColorBrush(Mix(accent, Colors.White, 0.14));
        r["AccentSoftBrush"] = new SolidColorBrush(Color.FromArgb((byte)(t.Dark ? 0x40 : 0x26), accent.R, accent.G, accent.B));
        r["AccentSoftStrongBrush"] = new SolidColorBrush(Color.FromArgb((byte)(t.Dark ? 0x5C : 0x3A), accent.R, accent.G, accent.B));
        r["OnAccentBrush"] = new SolidColorBrush(Luminance(accent) > 0.62 ? Color.Parse("#1D1D1F") : Colors.White);

        // Feed the accent into Fluent's own resources so built-in controls follow the theme.
        r["SystemAccentColor"] = accent;
        r["SystemAccentColorLight1"] = Mix(accent, Colors.White, 0.15);
        r["SystemAccentColorLight2"] = Mix(accent, Colors.White, 0.30);
        r["SystemAccentColorLight3"] = Mix(accent, Colors.White, 0.45);
        r["SystemAccentColorDark1"] = Mix(accent, Colors.Black, 0.15);
        r["SystemAccentColorDark2"] = Mix(accent, Colors.Black, 0.30);
        r["SystemAccentColorDark3"] = Mix(accent, Colors.Black, 0.45);
        foreach (var key in new[] { "SliderTrackValueFill", "SliderTrackValueFillPointerOver", "SliderTrackValueFillPressed",
                     "ToggleSwitchFillOn", "ToggleSwitchFillOnPointerOver", "ToggleSwitchFillOnPressed",
                     "CheckBoxCheckBackgroundFillChecked", "CheckBoxCheckBackgroundFillCheckedPointerOver",
                     "CheckBoxCheckBackgroundStrokeChecked", "CheckBoxCheckBackgroundStrokeCheckedPointerOver",
                     "SliderThumbBackground", "SliderThumbBackgroundPointerOver", "SliderThumbBackgroundPressed" })
            r[key] = new SolidColorBrush(accent);

        r["AppFont"] = new FontFamily(s.Font == "Inter" || string.IsNullOrWhiteSpace(s.Font) ? "fonts:Inter#Inter" : s.Font);
        var fs = Math.Clamp(s.FontSize, 10, 20);
        r["AppFontSize"] = fs;
        r["AppFontSizeSmall"] = fs - 1.5;
        r["AppFontSizeHeader"] = fs + 2.5;
        r["AppFontSizeTitle"] = fs + 9;
        r["CardRadius"] = new CornerRadius(Math.Clamp(s.Radius, 0, 24));
        r["ItemRadius"] = new CornerRadius(Math.Clamp(s.Radius / 2, 0, 10));
    }

    static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));

    public static Color Mix(Color a, Color b, double t) => Color.FromArgb(
        a.A,
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t));

    static double Luminance(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
}

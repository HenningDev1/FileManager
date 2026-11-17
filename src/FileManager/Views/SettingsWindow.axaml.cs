using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using FileManager.Controls;
using FileManager.Services;

namespace FileManager.Views;

public partial class SettingsWindow : Window
{
    bool _loading = true;
    AppSettings S => Settings.Current;

    public SettingsWindow()
    {
        InitializeComponent();
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"File Manager {v?.Major}.{v?.Minor}.{v?.Build}";

        var fonts = new List<string> { "Inter" };
        try { fonts.AddRange(FontManager.Current.SystemFonts.Select(f => f.Name).Where(n => n != "Inter").Distinct().OrderBy(n => n)); } catch { }
        FontBox.ItemsSource = fonts;
        Load();
        _loading = false;
    }

    void Load()
    {
        BuildThemes();
        BuildAccents();
        AccentHex.Text = S.Accent ?? "";
        FontBox.SelectedItem = S.Font;
        SizeSlider.Value = S.FontSize;
        SizeText.Text = $"{S.FontSize:0.#}";
        RadiusSlider.Value = S.Radius;
        RadiusText.Text = $"{S.Radius:0}";
        HiddenSwitch.IsChecked = S.ShowHidden;
        ScaleBox.SelectedIndex = Math.Max(0, Array.IndexOf(Scales, S.UiScale));
    }

    static readonly double[] Scales = [0, 0.8, 0.9, 1.0, 1.1, 1.25];

    void Scale_Changed(object? s, SelectionChangedEventArgs e)
    {
        if (_loading || ScaleBox.SelectedIndex < 0) return;
        S.UiScale = Scales[ScaleBox.SelectedIndex];
        MainWindow.Current?.ApplyScale();
    }

    void Apply()
    {
        if (_loading) return;
        ThemeService.Apply(S);
    }

    void BuildThemes()
    {
        ThemeList.Children.Clear();
        foreach (var t in ThemeService.Themes)
        {
            var swatch = new Border
            {
                Width = 96, Height = 60, CornerRadius = new CornerRadius(10), ClipToBounds = true,
                Background = new SolidColorBrush(Color.Parse(t.Bg)),
                BorderBrush = new SolidColorBrush(Color.Parse(t.Border)), BorderThickness = new Thickness(1),
                Child = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("28,*"),
                    Children =
                    {
                        new Border { Background = new SolidColorBrush(Color.Parse(t.Sidebar)) },
                        WithColumn(new StackPanel
                        {
                            Margin = new Thickness(8), Spacing = 5,
                            Children =
                            {
                                new Border { Height = 8, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(Color.Parse(t.Surface)) },
                                new Border { Height = 8, Width = 40, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(Color.Parse(t.Accent)) },
                                new Border { Height = 8, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(Color.Parse(t.Control)) },
                            },
                        }, 1),
                    },
                },
            };
            var b = new Button
            {
                Padding = new Thickness(6), Margin = new Thickness(0, 0, 8, 8), Background = Brushes.Transparent,
                Content = new StackPanel { Spacing = 6, Children = { swatch, new TextBlock { Text = t.Name, HorizontalAlignment = HorizontalAlignment.Center } } },
            };
            if (t.Name == S.Theme)
            {
                b.BorderThickness = new Thickness(2);
                Dialogs.Res(b, Button.BorderBrushProperty, "AccentBrush");
            }
            b.Click += (_, _) =>
            {
                S.Theme = t.Name;
                ThemeService.Apply(S);
                BuildThemes();
                BuildAccents();
            };
            ThemeList.Children.Add(b);
        }
    }

    static Control WithColumn(Control c, int col) { Grid.SetColumn(c, col); return c; }

    void BuildAccents()
    {
        AccentList.Children.Clear();
        AddAccent(null, ThemeService.Current.Accent, "Theme default");
        foreach (var (name, hex) in ThemeService.Accents) AddAccent(hex, hex, name);
    }

    void AddAccent(string? value, string hex, string name)
    {
        bool selected = string.Equals(S.Accent ?? "", value ?? "", StringComparison.OrdinalIgnoreCase);
        var dot = new Border
        {
            Width = 26, Height = 26, CornerRadius = new CornerRadius(13),
            Background = new SolidColorBrush(Color.Parse(hex)),
        };
        if (value is null)
            dot.Child = new Ico { Kind = "refresh", Width = 14, Height = 14, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        else if (selected)
            dot.Child = new Ico { Kind = "check", Width = 14, Height = 14, Foreground = Brushes.White, StrokeWidth = 2.6, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var ring = new Border
        {
            Padding = new Thickness(2), CornerRadius = new CornerRadius(16), Child = dot,
            BorderThickness = new Thickness(2),
            BorderBrush = selected ? new SolidColorBrush(Color.Parse(hex)) : Brushes.Transparent,
        };
        var b = new Button { Padding = new Thickness(2), Margin = new Thickness(0, 0, 6, 6), Background = Brushes.Transparent, Content = ring };
        ToolTip.SetTip(b, name);
        b.Click += (_, _) =>
        {
            S.Accent = value;
            _loading = true; AccentHex.Text = value ?? ""; _loading = false;
            ThemeService.Apply(S);
            BuildAccents();
        };
        AccentList.Children.Add(b);
    }

    void AccentHex_Changed(object? s, TextChangedEventArgs e)
    {
        if (_loading) return;
        var t = AccentHex.Text?.Trim();
        if (string.IsNullOrEmpty(t)) return;
        if (!t.StartsWith('#')) t = "#" + t;
        if (ThemeService.TryParse(t, out _) && t.Length is 7 or 9)
        {
            S.Accent = t;
            Apply();
            BuildAccents();
        }
    }

    void Font_Changed(object? s, SelectionChangedEventArgs e)
    {
        if (FontBox.SelectedItem is string f) { S.Font = f; Apply(); }
    }

    void Size_Changed(object? s, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        S.FontSize = Math.Round(SizeSlider.Value * 2) / 2;
        SizeText.Text = $"{S.FontSize:0.#}";
        Apply();
    }

    void Radius_Changed(object? s, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        S.Radius = Math.Round(RadiusSlider.Value);
        RadiusText.Text = $"{S.Radius:0}";
        Apply();
    }

    void Hidden_Changed(object? s, RoutedEventArgs e)
    {
        if (!_loading) S.ShowHidden = HiddenSwitch.IsChecked == true;
    }

    void Reset_Click(object? s, RoutedEventArgs e)
    {
        var d = new AppSettings();
        S.Theme = d.Theme; S.Accent = d.Accent; S.Font = d.Font; S.FontSize = d.FontSize; S.Radius = d.Radius; S.UiScale = d.UiScale;
        MainWindow.Current?.ApplyScale();
        _loading = true; Load(); _loading = false;
        ThemeService.Apply(S);
    }

    void Done_Click(object? s, RoutedEventArgs e)
    {
        Settings.Save();
        Close();
    }

    void Drag(object? s, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
    }
}

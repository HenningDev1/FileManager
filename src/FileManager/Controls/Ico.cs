using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using ShapePath = Avalonia.Controls.Shapes.Path;

namespace FileManager.Controls;

/// <summary>Thin line icon in a 24×24 grid (SF-Symbols-like), coloured by the inherited Foreground.</summary>
public class Ico : Viewbox
{
    public static readonly StyledProperty<string?> KindProperty = AvaloniaProperty.Register<Ico, string?>(nameof(Kind));
    public static readonly StyledProperty<IBrush?> ForegroundProperty = TextElement.ForegroundProperty.AddOwner<Ico>();
    public static readonly StyledProperty<double> StrokeWidthProperty = AvaloniaProperty.Register<Ico, double>(nameof(StrokeWidth), 1.8);

    public string? Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public double StrokeWidth { get => GetValue(StrokeWidthProperty); set => SetValue(StrokeWidthProperty, value); }

    readonly ShapePath _path = new()
    {
        Width = 24, Height = 24, Stretch = Stretch.None,
        StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round, StrokeThickness = 1.8,
    };

    public Ico()
    {
        Width = 16; Height = 16;
        Child = _path;
        _path.Bind(Shape.StrokeProperty, this.GetObservable(ForegroundProperty));
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == KindProperty) _path.Data = Icons.Get(Kind);
        else if (change.Property == StrokeWidthProperty) _path.StrokeThickness = StrokeWidth;
    }
}

public static class Icons
{
    static readonly Dictionary<string, string> Data = new()
    {
        ["back"] = "M15 5 L8 12 L15 19",
        ["forward"] = "M9 5 L16 12 L9 19",
        ["up"] = "M12 19 V5 M6 11 L12 5 L18 11",
        ["down"] = "M12 5 V19 M6 13 L12 19 L18 13",
        ["refresh"] = "M20 12 A8 8 0 1 1 17.7 6.3 M20 4 V9 H15",
        ["search"] = "M4 10.5 A6.5 6.5 0 1 0 17 10.5 A6.5 6.5 0 1 0 4 10.5 M15.3 15.3 L20 20",
        ["folder"] = "M3 7 A2 2 0 0 1 5 5 H9 L11 7 H19 A2 2 0 0 1 21 9 V17 A2 2 0 0 1 19 19 H5 A2 2 0 0 1 3 17 Z",
        ["folderplus"] = "M3 7 A2 2 0 0 1 5 5 H9 L11 7 H19 A2 2 0 0 1 21 9 V17 A2 2 0 0 1 19 19 H5 A2 2 0 0 1 3 17 Z M12 10 V16 M9 13 H15",
        ["copy"] = "M9 9 H18 A2 2 0 0 1 20 11 V18 A2 2 0 0 1 18 20 H11 A2 2 0 0 1 9 18 Z M15 9 V6 A2 2 0 0 0 13 4 H6 A2 2 0 0 0 4 6 V13 A2 2 0 0 0 6 15 H9",
        ["cut"] = "M4 6.5 A2.5 2.5 0 1 0 9 6.5 A2.5 2.5 0 1 0 4 6.5 M4 17.5 A2.5 2.5 0 1 0 9 17.5 A2.5 2.5 0 1 0 4 17.5 M8.6 8.2 L20 19 M8.6 15.8 L20 5",
        ["paste"] = "M9 3 H15 V6 H9 Z M9 4.5 H7 A2 2 0 0 0 5 6.5 V19 A2 2 0 0 0 7 21 H17 A2 2 0 0 0 19 19 V6.5 A2 2 0 0 0 17 4.5 H15",
        ["trash"] = "M4 7 H20 M10 11 V17 M14 11 V17 M6 7 L7 19 A2 2 0 0 0 9 21 H15 A2 2 0 0 0 17 19 L18 7 M9 7 V4 H15 V7",
        ["rename"] = "M4 20 H8 L19 9 A2.1 2.1 0 0 0 15 5 L4 16 Z M13.5 6.5 L17.5 10.5",
        ["batch"] = "M4 6 H13 M4 11 H11 M4 16 H8 M13 19 V17 L19 11 L21 13 L15 19 Z",
        ["list"] = "M9 6 H20 M9 12 H20 M9 18 H20 M4.5 6 H4.6 M4.5 12 H4.6 M4.5 18 H4.6",
        ["grid"] = "M4 4 H10 V10 H4 Z M14 4 H20 V10 H14 Z M4 14 H10 V20 H4 Z M14 14 H20 V20 H14 Z",
        ["dual"] = "M3 7 A2 2 0 0 1 5 5 H19 A2 2 0 0 1 21 7 V17 A2 2 0 0 1 19 19 H5 A2 2 0 0 1 3 17 Z M12 5 V19",
        ["preview"] = "M3 7 A2 2 0 0 1 5 5 H19 A2 2 0 0 1 21 7 V17 A2 2 0 0 1 19 19 H5 A2 2 0 0 1 3 17 Z M15 5 V19",
        ["settings"] = "M4 7 H13 M17 7 H20 M4 17 H7 M11 17 H20 M15 5 V9 M9 15 V19",
        ["home"] = "M4 11 L12 4 L20 11 M6 9.5 V19 A1 1 0 0 0 7 20 H17 A1 1 0 0 0 18 19 V9.5 M10 20 V14 H14 V20",
        ["desktop"] = "M3 6 A2 2 0 0 1 5 4 H19 A2 2 0 0 1 21 6 V14 A2 2 0 0 1 19 16 H5 A2 2 0 0 1 3 14 Z M8 20 H16 M12 16 V20",
        ["pc"] = "M3 6 A2 2 0 0 1 5 4 H19 A2 2 0 0 1 21 6 V14 A2 2 0 0 1 19 16 H5 A2 2 0 0 1 3 14 Z M8 20 H16 M12 16 V20",
        ["doc"] = "M7 3 H14 L19 8 V19 A2 2 0 0 1 17 21 H7 A2 2 0 0 1 5 19 V5 A2 2 0 0 1 7 3 Z M14 3 V8 H19 M9 13 H15 M9 17 H13",
        ["download"] = "M12 4 V15 M7 10 L12 15 L17 10 M5 20 H19",
        ["picture"] = "M3 7 A2 2 0 0 1 5 5 H19 A2 2 0 0 1 21 7 V17 A2 2 0 0 1 19 19 H5 A2 2 0 0 1 3 17 Z M3 16 L8 11 L13 16 M12 15 L15 12 L21 17 M15.5 8.5 H15.6",
        ["music"] = "M9 18 V6 L20 4 V16 M9 18 A3 3 0 1 1 3 18 A3 3 0 1 1 9 18 M20 16 A3 3 0 1 1 14 16 A3 3 0 1 1 20 16",
        ["video"] = "M3 8 A2 2 0 0 1 5 6 H13 A2 2 0 0 1 15 8 V16 A2 2 0 0 1 13 18 H5 A2 2 0 0 1 3 16 Z M15 10.5 L21 7 V17 L15 13.5",
        ["drive"] = "M3 14 H21 V18 A2 2 0 0 1 19 20 H5 A2 2 0 0 1 3 18 Z M3 14 L6 5 H18 L21 14 M17 17 H17.1",
        ["chart"] = "M12 3 A9 9 0 1 0 21 12 H12 Z M15 2.5 A9 9 0 0 1 21.5 9 H15 Z",
        ["dup"] = "M9 3 H15 L19 7 V16 A2 2 0 0 1 17 18 H9 A2 2 0 0 1 7 16 V5 A2 2 0 0 1 9 3 Z M15 3 V7 H19 M7 7 H6 A2 2 0 0 0 4 9 V19 A2 2 0 0 0 6 21 H13 A2 2 0 0 0 15 19 V18",
        ["zip"] = "M7 3 H17 A2 2 0 0 1 19 5 V19 A2 2 0 0 1 17 21 H7 A2 2 0 0 1 5 19 V5 A2 2 0 0 1 7 3 Z M12 3 V5 M12 7 V9 M12 11 V13 M10.5 15 H13.5 V18 H10.5 Z",
        ["external"] = "M14 4 H20 V10 M20 4 L11 13 M18 14 V18 A2 2 0 0 1 16 20 H6 A2 2 0 0 1 4 18 V8 A2 2 0 0 1 6 6 H10",
        ["move"] = "M5 12 H19 M13 6 L19 12 L13 18",
        ["swap"] = "M7 4 L3 8 L7 12 M3 8 H17 M17 12 L21 16 L17 20 M21 16 H7",
        ["play"] = "M7 4.5 L19 12 L7 19.5 Z",
        ["x"] = "M6 6 L18 18 M18 6 L6 18",
        ["check"] = "M5 12.5 L10 17.5 L19 7",
        ["terminal"] = "M4 6 L10 12 L4 18 M12 18 H20",
        ["info"] = "M3 12 A9 9 0 1 0 21 12 A9 9 0 1 0 3 12 M12 11 V16 M12 8 H12.1",
        ["warning"] = "M12 3 L22 20 H2 Z M12 10 V14 M12 17 H12.1",
        ["filter"] = "M4 5 H20 L14 12.5 V19 L10 17 V12.5 Z",
        ["file"] = "M7 3 H14 L19 8 V19 A2 2 0 0 1 17 21 H7 A2 2 0 0 1 5 19 V5 A2 2 0 0 1 7 3 Z M14 3 V8 H19",
        ["select"] = "M4 4 H8 M4 4 V8 M20 4 H16 M20 4 V8 M4 20 H8 M4 20 V16 M20 20 H16 M20 20 V16 M9 12 L11 14 L15 10",
    };

    public static Geometry? Get(string? kind) =>
        kind is not null && Data.TryGetValue(kind, out var d) ? Geometry.Parse(d) : null;
}

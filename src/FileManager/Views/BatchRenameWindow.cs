using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using FileManager.Models;
using FileManager.Services;

namespace FileManager.Views;

/// <summary>Rename many files at once with a live preview: find/replace (or regex), name pattern with counters, case, extension.</summary>
public sealed class BatchRenameWindow : Window
{
    readonly List<string> _paths;
    readonly TextBox _find = new() { Watermark = "Find (optional)" };
    readonly TextBox _replace = new() { Watermark = "Replace with" };
    readonly CheckBox _regex = new() { Content = "Regular expression" };
    readonly TextBox _pattern = new() { Text = "{name}" };
    readonly TextBox _start = new() { Text = "1", Width = 70 };
    readonly TextBox _digits = new() { Text = "2", Width = 70 };
    readonly ComboBox _case = new() { ItemsSource = new[] { "Keep case", "lowercase", "UPPERCASE", "Title Case" }, SelectedIndex = 0, MinWidth = 140 };
    readonly TextBox _ext = new() { Watermark = "keep", Width = 90 };
    readonly ListBox _preview = new() { Height = 260, Classes = { "plain" } };
    readonly TextBlock _summary = new() { Classes = { "sub" } };
    readonly Button _apply;
    List<RenamePreview> _items = [];

    public bool Applied { get; private set; }
    public string ResultMessage { get; private set; } = "";

    public BatchRenameWindow(List<string> paths)
    {
        _paths = paths;
        Title = "Batch Rename";
        Width = 720;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaTitleBarHeightHint = 40;
        Dialogs.Res(this, BackgroundProperty, "SurfaceBrush");

        var title = new TextBlock { Text = $"Rename {paths.Count} items" };
        title.Classes.Add("header");

        var help = Dialogs.Text("Pattern tokens: {name} original name · {n} counter · {date} modified date · {parent} folder name. Example: Holiday_{n}", true);

        Control Field(string label, Control c)
        {
            var l = new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 4) };
            l.Classes.Add("sub");
            return new StackPanel { Children = { l, c } };
        }

        var row1 = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,*,12,Auto") };
        row1.Children.Add(Field("Find", _find));
        var f2 = Field("Replace with", _replace); Grid.SetColumn(f2, 2); row1.Children.Add(f2);
        _regex.VerticalAlignment = VerticalAlignment.Bottom; _regex.Margin = new Thickness(0, 0, 0, 4);
        Grid.SetColumn(_regex, 4); row1.Children.Add(_regex);

        var row2 = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,Auto,12,Auto,12,Auto,12,Auto") };
        row2.Children.Add(Field("Name pattern", _pattern));
        void Put(Control c, int col) { Grid.SetColumn(c, col); row2.Children.Add(c); }
        Put(Field("Start at", _start), 2);
        Put(Field("Digits", _digits), 4);
        Put(Field("Case", _case), 6);
        Put(Field("New extension", _ext), 8);

        _preview.ItemTemplate = new FuncDataTemplate<RenamePreview>((p, _) =>
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*") };
            var oldT = new TextBlock { Text = p?.Old, TextTrimming = TextTrimming.CharacterEllipsis, Opacity = 0.7 };
            var arrow = new TextBlock { Text = "→", Margin = new Thickness(10, 0) };
            var newT = new TextBlock
            {
                Text = p is null ? "" : p.HasError ? $"{p.New}  ⚠ {p.Error}" : p.New,
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontWeight = p?.Changed == true ? FontWeight.SemiBold : FontWeight.Normal,
                Foreground = p?.HasError == true ? new SolidColorBrush(Color.Parse("#FF3B30")) : null,
            };
            Grid.SetColumn(arrow, 1); Grid.SetColumn(newT, 2);
            g.Children.Add(oldT); g.Children.Add(arrow); g.Children.Add(newT);
            return g;
        });

        var previewCard = new Border { Child = _preview, Padding = new Thickness(4) };
        previewCard.Classes.Add("card");

        var cancel = Dialogs.Btn("Cancel");
        _apply = Dialogs.Btn("Rename", "accent");
        cancel.Click += (_, _) => Close();
        _apply.Click += (_, _) => DoApply();

        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        footer.Children.Add(_summary);
        var btns = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { cancel, _apply } };
        Grid.SetColumn(btns, 1);
        footer.Children.Add(btns);
        _summary.VerticalAlignment = VerticalAlignment.Center;

        Content = new StackPanel
        {
            Margin = new Thickness(24, 18, 24, 20), Spacing = 14,
            Children = { title, row1, row2, help, previewCard, footer },
        };

        foreach (var tb in new[] { _find, _replace, _pattern, _start, _digits, _ext })
            tb.TextChanged += (_, _) => Recompute();
        _regex.IsCheckedChanged += (_, _) => Recompute();
        _case.SelectionChanged += (_, _) => Recompute();
        KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Escape) Close(); };
        Recompute();
    }

    void Recompute()
    {
        _items = Compute();
        _preview.ItemsSource = _items;
        int changed = _items.Count(i => i.Changed && !i.HasError);
        int errors = _items.Count(i => i.HasError);
        _summary.Text = errors > 0 ? $"{changed} will be renamed · {errors} problem(s) to fix" : $"{changed} of {_items.Count} will be renamed";
        _apply.IsEnabled = changed > 0 && errors == 0;
    }

    List<RenamePreview> Compute()
    {
        var list = new List<RenamePreview>();
        int.TryParse(_start.Text, out int start);
        if (!int.TryParse(_digits.Text, out int digits)) digits = 1;
        digits = Math.Clamp(digits, 1, 8);
        var pattern = string.IsNullOrEmpty(_pattern.Text) ? "{name}" : _pattern.Text!;
        var find = _find.Text ?? "";
        var rep = _replace.Text ?? "";
        Regex? rx = null;
        string? rxError = null;
        if (_regex.IsChecked == true && find.Length > 0)
        {
            try { rx = new Regex(find, RegexOptions.IgnoreCase); } catch (Exception ex) { rxError = "Invalid regex: " + ex.Message; }
        }
        var invalid = Path.GetInvalidFileNameChars();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sources = new HashSet<string>(_paths, StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < _paths.Count; i++)
        {
            var p = _paths[i];
            bool isDir = Directory.Exists(p);
            var oldName = Path.GetFileName(p);
            var stem = isDir ? oldName : Path.GetFileNameWithoutExtension(p);
            var ext = isDir ? "" : Path.GetExtension(p);
            string? error = rxError;

            if (find.Length > 0 && rxError is null)
                stem = rx is not null ? rx.Replace(stem, rep) : stem.Replace(find, rep, StringComparison.OrdinalIgnoreCase);

            DateTime mod = default;
            try { mod = isDir ? Directory.GetLastWriteTime(p) : File.GetLastWriteTime(p); } catch { }
            var parent = Path.GetFileName(Path.GetDirectoryName(p) ?? "");
            var s = pattern
                .Replace("{name}", stem)
                .Replace("{n}", (start + i).ToString().PadLeft(digits, '0'))
                .Replace("{date}", mod.ToString("yyyy-MM-dd"))
                .Replace("{parent}", parent);
            s = _case.SelectedIndex switch
            {
                1 => s.ToLowerInvariant(),
                2 => s.ToUpperInvariant(),
                3 => System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(s.ToLowerInvariant()),
                _ => s,
            };
            if (!isDir && !string.IsNullOrWhiteSpace(_ext.Text)) ext = "." + _ext.Text!.Trim().TrimStart('.');
            var newName = (s + ext).Trim();

            if (error is null)
            {
                if (newName.Length == 0 || s.Trim().Length == 0) error = "empty name";
                else if (newName.IndexOfAny(invalid) >= 0) error = "invalid character";
                else if (!names.Add(newName)) error = "duplicate name";
                else
                {
                    var target = Path.Combine(Path.GetDirectoryName(p)!, newName);
                    if (!string.Equals(target, p, StringComparison.OrdinalIgnoreCase) && !sources.Contains(target) && (File.Exists(target) || Directory.Exists(target)))
                        error = "already exists";
                }
            }
            list.Add(new RenamePreview { Old = oldName, New = newName, Error = error });
        }
        return list;
    }

    void DoApply()
    {
        var ops = new List<(string, string)>();
        for (int i = 0; i < _paths.Count; i++)
        {
            if (!_items[i].Changed) continue;
            ops.Add((_paths[i], Path.Combine(Path.GetDirectoryName(_paths[i])!, _items[i].New)));
        }
        var (ok, errors) = FileOps.RenameMany(ops);
        Applied = true;
        ResultMessage = errors.Count == 0 ? $"Renamed {ok} item(s)" : $"Renamed {ok} item(s), {errors.Count} failed: {errors[0]}";
        Close();
    }
}

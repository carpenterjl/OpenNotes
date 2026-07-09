using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using OpenNotes.Models;
using OpenNotes.Models.Blocks;
using OpenNotes.ViewModels.Blocks;
using SharpVectors.Converters;
using SharpVectors.Renderers.Wpf;

namespace OpenNotes.Converters;

[ValueConversion(typeof(int), typeof(Visibility))]
public class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var visible = value is int count && count > 0;
        if (parameter is string s && s.Equals("Invert", StringComparison.OrdinalIgnoreCase))
            visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

[ValueConversion(typeof(bool), typeof(Visibility))]
public class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var b = value is bool bv && bv;
        if (Invert) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility v && v == Visibility.Visible ? !Invert : Invert;
}

[ValueConversion(typeof(string), typeof(Color))]
public class StringToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        try
        {
            if (value is string hex && !string.IsNullOrEmpty(hex))
                return (Color)ColorConverter.ConvertFromString(hex);
        }
        catch { /* fall through */ }
        return Colors.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

[ValueConversion(typeof(string), typeof(SolidColorBrush))]
public class StringToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        try
        {
            if (value is string hex && !string.IsNullOrEmpty(hex))
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                return new SolidColorBrush(color);
            }
        }
        catch { /* fall through */ }
        return new SolidColorBrush(Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

[ValueConversion(typeof(TaskStatus), typeof(SolidColorBrush))]
public class StatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is TaskStatus status)
        {
            return status switch
            {
                TaskStatus.Completed => new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
                TaskStatus.InProgress => new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3)),
                TaskStatus.Blocked => new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36)),
                TaskStatus.Waiting => new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00)),
                TaskStatus.Cancelled => new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E)),
                _ => new SolidColorBrush(Color.FromRgb(0x78, 0x90, 0x9C)),
            };
        }
        return new SolidColorBrush(Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

[ValueConversion(typeof(TaskPriority), typeof(SolidColorBrush))]
public class PriorityToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is TaskPriority priority)
        {
            return priority switch
            {
                TaskPriority.Critical => new SolidColorBrush(Color.FromRgb(0xBF, 0x61, 0x6A)),
                TaskPriority.High => new SolidColorBrush(Color.FromRgb(0xD0, 0x87, 0x70)),
                TaskPriority.Medium => new SolidColorBrush(Color.FromRgb(0xEB, 0xCB, 0x8B)),
                TaskPriority.Low => new SolidColorBrush(Color.FromRgb(0x88, 0xC0, 0xD0)),
                _ => new SolidColorBrush(Color.FromRgb(0x5A, 0x5E, 0x78)),
            };
        }
        return new SolidColorBrush(Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

[ValueConversion(typeof(bool), typeof(string))]
public class BoolToCheckConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? "✓" : "○";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

[ValueConversion(typeof(TaskStatus), typeof(bool))]
[ValueConversion(typeof(object), typeof(Visibility))]
public class NullToVisibilityConverter : IValueConverter
{
    public bool InvertWhenNull { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var isNull = value is null || (value is string s && string.IsNullOrEmpty(s));
        if (InvertWhenNull) isNull = !isNull;
        return isNull ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class StatusToCompletedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is TaskStatus.Completed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? TaskStatus.Completed : TaskStatus.NotStarted;
}

[ValueConversion(typeof(int), typeof(Visibility))]
public class ZeroCountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int count && count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Visible when the bound enum value's name equals the ConverterParameter string.</summary>
public class EnumToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value?.ToString() == parameter?.ToString() ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Two-way enum &lt;-&gt; bool for radio-button-style toggles (ConverterParameter = enum name).</summary>
public class EnumToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value?.ToString() == parameter?.ToString();

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true && parameter is string s ? Enum.Parse(targetType, s) : Binding.DoNothing;
}

public class BlockTypeNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            TextBlockViewModel => "Text",
            MarkdownBlockViewModel => "Markdown",
            LatexBlockViewModel => "LaTeX",
            CodeBlockViewModel => "Code",
            MermaidBlockViewModel => "Mermaid Diagram",
            ImageBlockViewModel => "Image",
            ChecklistBlockViewModel => "Checklist",
            _ => "Block"
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Parses an SVG markup string into a WPF <see cref="ImageSource"/> via SharpVectors, so a
/// snapshot node's captured SVG (Mermaid/code) can be shown with a plain <c>&lt;Image&gt;</c>. Results are
/// cached by markup string since rendering is comparatively expensive.</summary>
[ValueConversion(typeof(string), typeof(ImageSource))]
public class SvgStringToImageSourceConverter : IValueConverter
{
    private static readonly Dictionary<string, ImageSource?> _cache = new();

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string svg || string.IsNullOrWhiteSpace(svg)) return null;

        if (_cache.TryGetValue(svg, out var cached)) return cached;

        ImageSource? source = null;
        try
        {
            var settings = new WpfDrawingSettings { IncludeRuntime = false, TextAsGeometry = true };
            var reader = new FileSvgReader(settings, false);
            using var sr = new StringReader(svg);
            var drawing = reader.Read(sr);
            if (drawing is not null)
            {
                source = new DrawingImage(drawing);
                source.Freeze();
            }
        }
        catch
        {
            source = null; // malformed SVG → render nothing rather than crash
        }

        _cache[svg] = source;
        return source;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Loads a local image file path into a frozen <see cref="ImageSource"/> for display in a
/// plain <c>&lt;Image&gt;</c> (used by canvas image snapshot nodes). Returns null for missing paths.</summary>
[ValueConversion(typeof(string), typeof(ImageSource))]
public class PathToImageSourceConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try
        {
            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            // WPF caches decoded bitmaps by URI app-wide; canvas assets are re-extracted to the
            // SAME path with new content (stable per-node asset names), so the cache must be
            // bypassed or re-rendered Mermaid/image nodes keep showing stale pixels.
            bmp.CreateOptions = System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreImageCache;
            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Renders a short "icon Type: preview…" label for a <see cref="ContentBlock"/>, used by
/// the canvas "Insert Block" menu.</summary>
[ValueConversion(typeof(ContentBlock), typeof(string))]
public class BlockPreviewConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        static string Clip(string s)
        {
            s = (s ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            return s.Length > 40 ? s[..40] + "…" : s;
        }

        return value switch
        {
            TextBlock t => $"📄 Text: {Clip(t.Content)}",
            MarkdownBlock m => $"📝 Markdown: {Clip(m.Markdown)}",
            LatexBlock l => $"∑ LaTeX: {Clip(l.Formula)}",
            MermaidBlock d => $"📊 Mermaid: {Clip(d.Definition)}",
            CodeBlock c => $"💻 Code ({c.Language}): {Clip(c.Code)}",
            ImageBlock i => $"🖼 Image: {Clip(i.Caption ?? i.FilePath)}",
            ChecklistBlock cl => $"☑ Checklist: {cl.Items.Count} item(s)",
            ContentBlock b => b.GetType().Name,
            _ => "Block"
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

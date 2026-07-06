using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using OpenNotes.Services;

namespace OpenNotes.Dialogs;

/// <summary>A reusable visual color picker: a saturation/brightness square + hue bar (drag to pick),
/// RGB sliders, a hex box, and preset swatches, all kept in sync through one HSV state. Seeded from an
/// initial hex; exposes the chosen color as <see cref="ResultHex"/> (#RRGGBB) when OK is pressed.
/// The HSV↔RGB math lives in <see cref="ColorMath"/> so it is unit-testable without WPF.</summary>
public partial class ColorPickerDialog : Window
{
    private const double SvSize = 220;
    private const double HueSize = 220;
    private const double ThumbRadius = 7;

    // The 12 preset swatches (label, hex) — the palette's named colors plus a few greys.
    private static readonly (string Name, string Hex)[] Presets =
    [
        ("Blue", "#7B9CDF"), ("Green", "#A3BE8C"), ("Orange", "#D08770"), ("Red", "#BF616A"),
        ("Yellow", "#EBCB8B"), ("Purple", "#B48EAD"), ("Teal", "#5E81AC"), ("Pink", "#E39ABE"),
        ("White", "#FFFFFF"), ("Light grey", "#B0B0B0"), ("Dark grey", "#4A4A4A"), ("Black", "#1E1E2E"),
    ];

    private double _h, _s, _v; // current HSV state (H in [0,360), S/V in [0,1])
    private bool _syncing;     // guards programmatic slider/hex updates from recursing

    /// <summary>The chosen color as <c>#RRGGBB</c>. Only meaningful when <c>ShowDialog</c> returned true.</summary>
    public string ResultHex { get; private set; } = "#000000";

    public ColorPickerDialog(string? initialHex)
    {
        InitializeComponent();
        BuildPresets();

        var start = !string.IsNullOrWhiteSpace(initialHex)
            && CanvasThemeService.TryParseColor(initialHex!.Trim(), out var c)
            ? c
            : Color.FromRgb(0x7B, 0x9C, 0xDF);
        SetColorFromRgb(start);
    }

    private void BuildPresets()
    {
        foreach (var (name, hex) in Presets)
        {
            CanvasThemeService.TryParseColor(hex, out var color);
            var swatch = new Border
            {
                Width = 24,
                Height = 24,
                Margin = new Thickness(0, 0, 6, 6),
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                BorderBrush = (Brush)FindResource("BorderBrush"),
                Background = new SolidColorBrush(color),
                Cursor = Cursors.Hand,
                ToolTip = $"{name} ({hex})",
                Tag = color,
            };
            swatch.MouseLeftButtonUp += (_, _) => SetColorFromRgb((Color)swatch.Tag);
            PresetPanel.Children.Add(swatch);
        }
    }

    // ----- state sync -----

    private void SetColorFromRgb(Color color)
    {
        (_h, _s, _v) = ColorMath.RgbToHsv(color.R, color.G, color.B);
        SyncAll();
    }

    /// <summary>Push the current HSV state out to every control. <paramref name="updateHex"/> is false
    /// when the change originated from the hex box, so typing there isn't clobbered.</summary>
    private void SyncAll(bool updateHex = true)
    {
        _syncing = true;
        try
        {
            var (r, g, b) = ColorMath.HsvToRgb(_h, _s, _v);
            var color = Color.FromRgb(r, g, b);

            // SV square base = full-saturation/full-value hue.
            var (hr, hg, hb) = ColorMath.HsvToRgb(_h, 1, 1);
            SatRect.Fill = new LinearGradientBrush(Colors.White, Color.FromRgb(hr, hg, hb),
                new Point(0, 0), new Point(1, 0));

            Canvas.SetLeft(SvThumb, _s * SvSize - ThumbRadius);
            Canvas.SetTop(SvThumb, (1 - _v) * SvSize - ThumbRadius);
            Canvas.SetTop(HueThumb, _h / 360.0 * HueSize - 2);

            RSlider.Value = r;
            GSlider.Value = g;
            BSlider.Value = b;
            RVal.Text = r.ToString();
            GVal.Text = g.ToString();
            BVal.Text = b.ToString();

            var hex = $"#{r:X2}{g:X2}{b:X2}";
            if (updateHex) HexBox.Text = hex;
            PreviewSwatch.Background = new SolidColorBrush(color);
            ResultHex = hex;
        }
        finally
        {
            _syncing = false;
        }
    }

    // ----- SV square dragging -----

    private void SvCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        SvCanvas.CaptureMouse();
        UpdateSvFrom(e.GetPosition(SvCanvas));
    }

    private void SvCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (SvCanvas.IsMouseCaptured) UpdateSvFrom(e.GetPosition(SvCanvas));
    }

    private void SvCanvas_MouseUp(object sender, MouseButtonEventArgs e) => SvCanvas.ReleaseMouseCapture();

    private void UpdateSvFrom(Point p)
    {
        _s = Math.Clamp(p.X / SvSize, 0, 1);
        _v = Math.Clamp(1 - p.Y / SvSize, 0, 1);
        SyncAll();
    }

    // ----- hue bar dragging -----

    private void HueCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        HueCanvas.CaptureMouse();
        UpdateHueFrom(e.GetPosition(HueCanvas));
    }

    private void HueCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (HueCanvas.IsMouseCaptured) UpdateHueFrom(e.GetPosition(HueCanvas));
    }

    private void HueCanvas_MouseUp(object sender, MouseButtonEventArgs e) => HueCanvas.ReleaseMouseCapture();

    private void UpdateHueFrom(Point p)
    {
        _h = Math.Clamp(p.Y / HueSize, 0, 1) * 360;
        SyncAll();
    }

    // ----- RGB sliders & hex box -----

    private void RgbSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncing) return;
        SetColorFromRgb(Color.FromRgb((byte)RSlider.Value, (byte)GSlider.Value, (byte)BSlider.Value));
    }

    private void HexBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing) return;
        if (!CanvasThemeService.TryParseColor(HexBox.Text.Trim(), out var color)) return; // invalid mid-type
        (_h, _s, _v) = ColorMath.RgbToHsv(color.R, color.G, color.B);
        SyncAll(updateHex: false);
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

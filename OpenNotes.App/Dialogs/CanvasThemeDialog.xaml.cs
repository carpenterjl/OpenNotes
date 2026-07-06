using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenNotes.Services;

namespace OpenNotes.Dialogs;

/// <summary>Edits a canvas document's custom color overrides. Each box holds a hex color or is
/// empty (= use the app theme's default, shown in the preview swatch). OK exposes only the
/// non-empty values via <see cref="ResultColors"/>, keyed by canvas resource name.</summary>
public partial class CanvasThemeDialog : Window
{
    private readonly IReadOnlyDictionary<string, string> _themeDefaults;
    private bool _initialized;

    /// <summary>The committed overrides (resource key → hex). Null until OK is pressed.</summary>
    public Dictionary<string, string>? ResultColors { get; private set; }

    public CanvasThemeDialog(IReadOnlyDictionary<string, string> current,
        IReadOnlyDictionary<string, string> themeDefaults)
    {
        InitializeComponent();
        _themeDefaults = themeDefaults;

        TextBox_Text.Text = current.GetValueOrDefault(CanvasThemeService.TextKey, string.Empty);
        TextBox_Accent.Text = current.GetValueOrDefault(CanvasThemeService.AccentKey, string.Empty);
        TextBox_Surface.Text = current.GetValueOrDefault(CanvasThemeService.SurfaceKey, string.Empty);

        _initialized = true;
        RefreshPreviews();
    }

    private void AnyColorBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_initialized) RefreshPreviews();
    }

    private void RefreshPreviews()
    {
        UpdatePreview(Preview_Text, TextBox_Text.Text, CanvasThemeService.TextKey);
        UpdatePreview(Preview_Accent, TextBox_Accent.Text, CanvasThemeService.AccentKey);
        UpdatePreview(Preview_Surface, TextBox_Surface.Text, CanvasThemeService.SurfaceKey);
    }

    private void UpdatePreview(Border preview, string text, string key)
    {
        var hex = string.IsNullOrWhiteSpace(text) ? _themeDefaults.GetValueOrDefault(key) : text.Trim();
        preview.Background = hex is not null && CanvasThemeService.TryParseColor(hex, out var color)
            ? new SolidColorBrush(color)
            : Brushes.Transparent; // unparseable while typing — blank swatch, validated on OK
    }

    /// <summary>Open the visual color picker for the clicked swatch and write the chosen hex into the
    /// paired box (seeded from the box, or the theme default when the box is empty).</summary>
    private void Swatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag }) return;
        var (box, key) = tag switch
        {
            "Text" => (TextBox_Text, CanvasThemeService.TextKey),
            "Accent" => (TextBox_Accent, CanvasThemeService.AccentKey),
            _ => (TextBox_Surface, CanvasThemeService.SurfaceKey),
        };

        var seed = string.IsNullOrWhiteSpace(box.Text) ? _themeDefaults.GetValueOrDefault(key) : box.Text.Trim();
        var picker = new ColorPickerDialog(seed) { Owner = this };
        if (picker.ShowDialog() == true)
            box.Text = picker.ResultHex; // TextChanged → RefreshPreviews
    }

    private void Defaults_Click(object sender, RoutedEventArgs e)
    {
        TextBox_Text.Text = string.Empty;
        TextBox_Accent.Text = string.Empty;
        TextBox_Surface.Text = string.Empty;
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        var result = new Dictionary<string, string>();
        foreach (var (box, key) in new[]
        {
            (TextBox_Text, CanvasThemeService.TextKey),
            (TextBox_Accent, CanvasThemeService.AccentKey),
            (TextBox_Surface, CanvasThemeService.SurfaceKey),
        })
        {
            var text = box.Text.Trim();
            if (text.Length == 0) continue;
            if (!CanvasThemeService.TryParseColor(text, out _))
            {
                MessageBox.Show($"'{text}' is not a valid color. Use a hex value like #4A7FD4 (or clear the field).",
                    "Invalid color", MessageBoxButton.OK, MessageBoxImage.Warning);
                box.Focus();
                return;
            }
            result[key] = text;
        }

        ResultColors = result;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

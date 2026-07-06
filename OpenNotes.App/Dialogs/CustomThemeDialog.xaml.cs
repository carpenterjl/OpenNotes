using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Win32;
using OpenNotes.Interfaces;
using OpenNotes.Services;

namespace OpenNotes.Dialogs;

/// <summary>Per-item color editor for the standalone Custom theme. One row per editable brush slot
/// (label · hex box · live swatch), seeded from the current effective colors. OK validates every
/// field and pushes each value through <see cref="IThemeService.SetCustomColor"/> (which switches to
/// the Custom theme); Reset clears all overrides back to the Custom base.</summary>
public partial class CustomThemeDialog : Window
{
    private readonly IThemeService _themeService;
    private readonly List<ColorRow> _rows;

    public CustomThemeDialog(IThemeService themeService)
    {
        InitializeComponent();
        _themeService = themeService;

        var overrides = themeService.GetCustomColors();
        _rows = themeService.CustomColorItems
            .Select(item => new ColorRow(item.Key, item.Label,
                overrides.GetValueOrDefault(item.Key) ?? ReadLiveHex(item.Key)))
            .ToList();
        ColorItems.ItemsSource = _rows;
    }

    /// <summary>The current live value of a brush key (#RRGGBB), so the editor opens showing what the
    /// user sees now rather than an empty box.</summary>
    private static string ReadLiveHex(string key) =>
        Application.Current?.Resources[key] is SolidColorBrush b
            ? $"#{b.Color.R:X2}{b.Color.G:X2}{b.Color.B:X2}"
            : "#000000";

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        var invalid = _rows.FirstOrDefault(r => !CanvasThemeService.TryParseColor(r.Hex.Trim(), out _));
        if (invalid is not null)
        {
            MessageBox.Show($"'{invalid.Hex}' is not a valid color for {invalid.Label}. Use a hex value like #7B9CDF.",
                "Invalid color", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        foreach (var row in _rows)
            _themeService.SetCustomColor(row.Key, row.Hex.Trim());

        DialogResult = true;
        Close();
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        _themeService.ResetCustom();
        DialogResult = true;
        Close();
    }

    /// <summary>Open the visual color picker for the clicked row's swatch, seeded with its current hex.</summary>
    private void Swatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ColorRow row }) return;
        var picker = new ColorPickerDialog(row.Hex) { Owner = this };
        if (picker.ShowDialog() == true)
            row.Hex = picker.ResultHex; // OnHexChanged → Swatch updates live
    }

    private const string ThemeFileFilter = "OpenNotes Theme|*.theme.json|JSON|*.json|All Files|*.*";

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // Save exactly what's in the editor (valid hex only), so the user saves what they see.
        var colors = _rows
            .Where(r => CanvasThemeService.TryParseColor(r.Hex.Trim(), out _))
            .ToDictionary(r => r.Key, r => r.Hex.Trim());
        if (colors.Count == 0) return;

        var dlg = new SaveFileDialog { Title = "Save Theme", FileName = "MyTheme.theme.json", Filter = ThemeFileFilter };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            ThemeProfileIo.Save(dlg.FileName, colors);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not save the theme file.\n\n{ex.Message}", "Save failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Load_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "Load Theme", Filter = ThemeFileFilter };
        if (dlg.ShowDialog(this) != true) return;

        var profile = ThemeProfileIo.Load(dlg.FileName);
        if (profile is null)
        {
            MessageBox.Show("That file isn't a valid theme file, so no colors were changed.",
                "Load failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Apply through the engine (unknown keys skipped; missing keys → base default), then refresh
        // the editor rows to show the resulting effective palette.
        _themeService.ImportCustomColors(profile.Colors);
        var effective = _themeService.GetEffectiveCustomColors();
        foreach (var row in _rows)
            if (effective.TryGetValue(row.Key, out var hex))
                row.Hex = hex;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    /// <summary>One editable row; the swatch tracks the hex box live.</summary>
    private partial class ColorRow(string key, string label, string hex) : ObservableObject
    {
        public string Key { get; } = key;
        public string Label { get; } = label;

        [ObservableProperty] private string _hex = hex;

        public Brush Swatch => CanvasThemeService.TryParseColor(Hex?.Trim() ?? string.Empty, out var c)
            ? new SolidColorBrush(c)
            : Brushes.Transparent;

        partial void OnHexChanged(string value) => OnPropertyChanged(nameof(Swatch));
    }
}

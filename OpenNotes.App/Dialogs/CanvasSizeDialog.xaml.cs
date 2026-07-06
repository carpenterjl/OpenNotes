using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace OpenNotes.Dialogs;

public partial class CanvasSizeDialog : Window
{
    public double ResultWidth { get; private set; }
    public double ResultHeight { get; private set; }

    private bool _suppressPresetReset;

    public CanvasSizeDialog(double currentWidth, double currentHeight)
    {
        InitializeComponent();
        WidthBox.Text = currentWidth.ToString(CultureInfo.InvariantCulture);
        HeightBox.Text = currentHeight.ToString(CultureInfo.InvariantCulture);
        PresetCombo.SelectedIndex = 0; // Custom — presets just prefill the boxes below
    }

    // Selecting a preset fills the width/height boxes; the boxes stay editable afterward so a
    // preset can be tweaked without a separate "Custom" mode.
    private void PresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PresetCombo.SelectedItem is not ComboBoxItem { Tag: string tag } || tag == "Custom") return;

        var parts = tag.Split(',');
        if (parts.Length != 2) return;

        _suppressPresetReset = true;
        WidthBox.Text = parts[0];
        HeightBox.Text = parts[1];
        _suppressPresetReset = false;
    }

    // Manually editing a size box after picking a preset falls back to "Custom" in the dropdown.
    private void SizeBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressPresetReset) return;
        PresetCombo.SelectedIndex = 0;
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(WidthBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var w) || w <= 0 ||
            !double.TryParse(HeightBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var h) || h <= 0)
        {
            MessageBox.Show("Enter positive numbers for width and height.", "Invalid size",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        const double maxDimension = 20000;
        ResultWidth = Math.Min(w, maxDimension);
        ResultHeight = Math.Min(h, maxDimension);
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

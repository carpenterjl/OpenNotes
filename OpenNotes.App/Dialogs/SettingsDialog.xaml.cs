using System.Windows;
using System.Windows.Controls;
using OpenNotes.Interfaces;

namespace OpenNotes.Dialogs;

public partial class SettingsDialog : Window
{
    private readonly IThemeService _themeService;

    public SettingsDialog(IThemeService themeService)
    {
        _themeService = themeService;
        InitializeComponent();
        SelectCurrentTheme();
    }

    private void SelectCurrentTheme()
    {
        foreach (ComboBoxItem item in ThemeCombo.Items)
        {
            if (item.Tag?.ToString() == _themeService.CurrentTheme)
            {
                ThemeCombo.SelectedItem = item;
                return;
            }
        }
        ThemeCombo.SelectedIndex = 0;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (ThemeCombo.SelectedItem is ComboBoxItem item && item.Tag is string theme)
            _themeService.ApplyTheme(theme);

        DialogResult = true;
    }

    private void Customize_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CustomThemeDialog(_themeService) { Owner = this };
        if (dialog.ShowDialog() == true)
            SelectCurrentTheme(); // OK/Reset switch to (or refresh) the Custom theme
    }
}

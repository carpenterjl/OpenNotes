using System.Windows;
using Microsoft.Win32;
using OpenNotes.Interfaces;

namespace OpenNotes.Services;

public class DialogService : IDialogService
{
    public Task<bool> ShowConfirmAsync(string title, string message,
        string confirmText = "OK", string cancelText = "Cancel")
    {
        var result = MessageBox.Show(message, title,
            MessageBoxButton.OKCancel, MessageBoxImage.Question);
        return Task.FromResult(result == MessageBoxResult.OK);
    }

    public Task ShowAlertAsync(string title, string message)
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        return Task.CompletedTask;
    }

    public Task<string?> ShowInputAsync(string title, string prompt, string defaultValue = "")
    {
        // Simple input dialog — replace with custom XAML dialog in a later phase
        var dialog = new Dialogs.InputDialog(title, prompt, defaultValue);
        var result = dialog.ShowDialog();
        return Task.FromResult(result == true ? dialog.InputValue : null);
    }

    public Task<string?> ShowMultilineInputAsync(string title, string prompt, string defaultValue = "")
    {
        var dialog = new Dialogs.MultilineInputDialog(title, prompt, defaultValue);
        var result = dialog.ShowDialog();
        return Task.FromResult(result == true ? dialog.InputValue : null);
    }

    public Task<(string Code, string Language)?> ShowCodeInputAsync(
        string title, string code, string language, IReadOnlyList<string> languages)
    {
        var dialog = new Dialogs.CodeInputDialog(title, code, language, languages);
        var result = dialog.ShowDialog();
        return Task.FromResult<(string, string)?>(
            result == true ? (dialog.CodeValue, dialog.LanguageValue) : null);
    }

    public Task<string?> ShowOpenFileAsync(string title, string filter = "All Files|*.*")
    {
        var dlg = new OpenFileDialog { Title = title, Filter = filter };
        return Task.FromResult(dlg.ShowDialog() == true ? dlg.FileName : null);
    }

    public Task<string?> ShowSaveFileAsync(string title, string defaultName = "", string filter = "All Files|*.*")
    {
        var dlg = new SaveFileDialog { Title = title, FileName = defaultName, Filter = filter };
        return Task.FromResult(dlg.ShowDialog() == true ? dlg.FileName : null);
    }

    public Task<string?> ShowOpenFolderAsync(string title)
    {
        var dlg = new OpenFolderDialog { Title = title };
        return Task.FromResult(dlg.ShowDialog() == true ? dlg.FolderName : null);
    }

    public Task<(double Width, double Height)?> ShowCanvasSizeDialogAsync(double currentWidth, double currentHeight)
    {
        var dialog = new Dialogs.CanvasSizeDialog(currentWidth, currentHeight);
        var result = dialog.ShowDialog();
        return Task.FromResult<(double, double)?>(result == true ? (dialog.ResultWidth, dialog.ResultHeight) : null);
    }

    public Task<Dictionary<string, string>?> ShowCanvasThemeDialogAsync(
        IReadOnlyDictionary<string, string> current, IReadOnlyDictionary<string, string> themeDefaults)
    {
        var dialog = new Dialogs.CanvasThemeDialog(current, themeDefaults);
        var result = dialog.ShowDialog();
        return Task.FromResult(result == true ? dialog.ResultColors : null);
    }

    public Task<string?> ShowColorPickerAsync(string? initialHex)
    {
        var dialog = new Dialogs.ColorPickerDialog(initialHex) { Owner = Application.Current?.MainWindow };
        var result = dialog.ShowDialog();
        return Task.FromResult(result == true ? dialog.ResultHex : null);
    }

    public Task ShowDialogAsync<TViewModel>(TViewModel viewModel) where TViewModel : class
    {
        // Resolved in later phase with a ViewModelToView mapping
        return Task.CompletedTask;
    }
}

namespace OpenNotes.Interfaces;

public interface IDialogService
{
    Task<bool> ShowConfirmAsync(string title, string message, string confirmText = "OK", string cancelText = "Cancel");
    Task ShowAlertAsync(string title, string message);
    Task<string?> ShowInputAsync(string title, string prompt, string defaultValue = "");
    Task<string?> ShowMultilineInputAsync(string title, string prompt, string defaultValue = "");

    /// <summary>Multiline code editor with a syntax-language dropdown. Returns the edited code
    /// and chosen language, or null on cancel.</summary>
    Task<(string Code, string Language)?> ShowCodeInputAsync(
        string title, string code, string language, IReadOnlyList<string> languages);
    Task<string?> ShowOpenFileAsync(string title, string filter = "All Files|*.*");
    Task<string?> ShowSaveFileAsync(string title, string defaultName = "", string filter = "All Files|*.*");
    Task<string?> ShowOpenFolderAsync(string title);
    Task<(double Width, double Height)?> ShowCanvasSizeDialogAsync(double currentWidth, double currentHeight);

    /// <summary>Edit a canvas document's custom color overrides. <paramref name="themeDefaults"/>
    /// supplies the app theme's default hex per key (for the preview swatches when a field is
    /// empty). Returns the committed overrides, or null on cancel.</summary>
    Task<Dictionary<string, string>?> ShowCanvasThemeDialogAsync(
        IReadOnlyDictionary<string, string> current, IReadOnlyDictionary<string, string> themeDefaults);

    /// <summary>Show the visual color picker seeded with <paramref name="initialHex"/> (may be null).
    /// Returns the chosen color as <c>#RRGGBB</c>, or null on cancel.</summary>
    Task<string?> ShowColorPickerAsync(string? initialHex);

    Task ShowDialogAsync<TViewModel>(TViewModel viewModel) where TViewModel : class;
}

namespace OpenNotes.Interfaces;

/// <summary>One editable color slot of the standalone "Custom" theme: the WPF brush resource key
/// plus a human-friendly label for the editor and command palette.</summary>
public record ThemeColorItem(string Key, string Label);

public interface IThemeService
{
    string CurrentTheme { get; }
    IReadOnlyList<string> AvailableThemes { get; }

    /// <summary>The color slots a user can override in the Custom theme (ordered for the editor).</summary>
    IReadOnlyList<ThemeColorItem> CustomColorItems { get; }

    event EventHandler<string>? ThemeChanged;

    void ApplyTheme(string themeName);
    void RegisterTheme(string name, Uri resourceUri);

    /// <summary>The current Custom-theme overrides (brush key → hex). Empty if never customized.</summary>
    IReadOnlyDictionary<string, string> GetCustomColors();

    /// <summary>Set one Custom-theme color (brush key → hex) and switch to / refresh the Custom theme.
    /// Seeds the other slots from the active theme on first use so only the chosen slot changes.</summary>
    void SetCustomColor(string key, string hex);

    /// <summary>Clear all Custom-theme overrides, reverting to the Custom theme's base colors.</summary>
    void ResetCustom();

    /// <summary>The effective Custom palette for export: the current overrides, filled in from the
    /// live theme brushes for any of the 16 slots not yet overridden, so a saved file is always a
    /// complete palette even if the user never edited every slot.</summary>
    IReadOnlyDictionary<string, string> GetEffectiveCustomColors();

    /// <summary>Apply a loaded palette to the Custom theme. Entries with an unknown key or an
    /// unparseable hex are skipped; slots absent from <paramref name="colors"/> fall back to the
    /// Custom base default. If no valid entries remain, the theme is left unchanged.</summary>
    void ImportCustomColors(IReadOnlyDictionary<string, string> colors);
}

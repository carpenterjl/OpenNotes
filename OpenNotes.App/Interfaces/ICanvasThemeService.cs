namespace OpenNotes.Interfaces;

/// <summary>Applies a canvas document's custom color overrides (its manifest
/// <c>ThemeColors</c> dictionary, keyed by resource name) as runtime resources in
/// <c>Application.Current.Resources</c>. A direct application-level entry shadows the merged theme
/// dictionaries, so every canvas element referencing the key via <c>DynamicResource</c> re-brushes
/// instantly; removing the entry falls back to the active app theme's default.</summary>
public interface ICanvasThemeService
{
    /// <summary>The overridable canvas resource keys (e.g. "CanvasTextBrush").</summary>
    IReadOnlyList<string> ThemeKeys { get; }

    /// <summary>Set the runtime overrides: keys present (and parseable) become application-level
    /// brushes; known keys absent from <paramref name="overrides"/> are removed so they fall back
    /// to the app theme.</summary>
    void Apply(IReadOnlyDictionary<string, string> overrides);

    /// <summary>Remove all canvas overrides (called when the canvas editor is left).</summary>
    void Reset();

    /// <summary>The app theme's default hex for a canvas key, ignoring any active override
    /// (searches only the merged theme dictionaries). Null when the key can't be resolved.</summary>
    string? GetThemeDefaultHex(string key);

    /// <summary>The effective live hex for a key: a direct override (canvas or app Custom theme) if
    /// present, else the merged theme default. Null when the key can't be resolved.</summary>
    string? GetEffectiveHex(string key);

    /// <summary>Translate canvas color overrides into Mermaid <c>themeVariables</c> hex values
    /// (theme <c>base</c>). Overrides win; missing keys fall back to the app theme's canvas
    /// defaults, so the result always tracks the CURRENT theme. Null only when no theme
    /// dictionary is resolvable (e.g. bare unit tests) — callers then keep Mermaid's stock theme.</summary>
    IReadOnlyDictionary<string, string>? GetMermaidThemeVariables(IReadOnlyDictionary<string, string> overrides);
}

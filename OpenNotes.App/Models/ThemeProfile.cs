namespace OpenNotes.Models;

/// <summary>A saved Custom-theme palette written to a user-selectable <c>.theme.json</c> file.
/// <see cref="Colors"/> maps brush resource keys (e.g. <c>"AccentBrush"</c>) to hex (<c>#RRGGBB</c>).
/// On load, unknown keys are skipped and keys absent from the file fall back to the theme's default,
/// so an older or partial file still imports cleanly.</summary>
public class ThemeProfile
{
    public string Name { get; set; } = "Custom";
    public Dictionary<string, string> Colors { get; set; } = [];
}

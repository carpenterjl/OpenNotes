namespace OpenNotes.Models;

public class AppSettings
{
    public string Theme { get; set; } = "Dark";
    public string AccentColor { get; set; } = "#5B9BD5";
    public double FontSizeBase { get; set; } = 14.0;
    public string FontFamily { get; set; } = "Segoe UI";
    public Guid? LastActiveWorkspaceId { get; set; }
    public List<Guid> RecentWorkspaceIds { get; set; } = [];
    public int MaxUndoHistory { get; set; } = 200;
    public bool MinimizeToTray { get; set; } = true;
    public bool StartWithWindows { get; set; } = false;
    public bool ShowNotifications { get; set; } = true;
    public string DataFolder { get; set; } = string.Empty;
    public Dictionary<string, string> KeyboardShortcuts { get; set; } = [];
    public bool EnableAnimations { get; set; } = true;
    public bool CheckForUpdates { get; set; } = false;

    /// <summary>Per-item color overrides for the standalone "Custom" theme (brush resource key → hex,
    /// e.g. "AccentBrush" → "#7B9CDF"). Empty until the user creates a Custom theme; seeded from the
    /// then-active theme's colors on first use. Applied as direct <c>Application.Current.Resources</c>
    /// entries by <c>ThemeService</c>.</summary>
    public Dictionary<string, string> CustomThemeColors { get; set; } = [];
}

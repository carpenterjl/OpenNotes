using System.IO;
using System.Text.Json;
using OpenNotes.Models;

namespace OpenNotes.Services;

/// <summary>Reads/writes a <see cref="ThemeProfile"/> to a user-chosen JSON file. Kept separate from
/// <c>IThemeService</c> (which owns applying colors) and free of DI so both the Custom Theme dialog
/// and the command palette can share it. <see cref="Load"/> returns null on any parse/IO failure so
/// callers can "fall back to no theme change".</summary>
public static class ThemeProfileIo
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Serialize <paramref name="colors"/> (brush key → hex) as a <see cref="ThemeProfile"/>.</summary>
    public static void Save(string path, IReadOnlyDictionary<string, string> colors, string name = "Custom")
    {
        var profile = new ThemeProfile { Name = name, Colors = new Dictionary<string, string>(colors) };
        File.WriteAllText(path, JsonSerializer.Serialize(profile, Options));
    }

    /// <summary>Parse a profile file, or null if it is missing, malformed, or not a theme profile.</summary>
    public static ThemeProfile? Load(string path)
    {
        try
        {
            var profile = JsonSerializer.Deserialize<ThemeProfile>(File.ReadAllText(path), Options);
            return profile?.Colors is null ? null : profile;
        }
        catch
        {
            return null; // unparseable / incompatible → caller makes no theme change
        }
    }
}

namespace OpenNotes.Interfaces;

/// <summary>How a <see cref="PaletteArg"/>'s value is entered and what suggestions it offers.</summary>
public enum PaletteArgKind
{
    /// <summary>Arbitrary text (e.g. a task name); no suggestion list, committed with Tab/Enter.</summary>
    FreeText,
    /// <summary>One of a fixed set of choices (e.g. a priority or a theme name).</summary>
    Choice,
    /// <summary>A number; <see cref="PaletteArg.Options"/> may offer common presets.</summary>
    Number,
    /// <summary>A date; options offer relative shortcuts (Today/Tomorrow/Next week).</summary>
    Date,
    /// <summary>A live app entity (task/canvas/workspace) resolved from a snapshot; the chosen
    /// option's <see cref="PaletteArgOption.Tag"/> carries the entity id.</summary>
    Entity,
}

/// <summary>A single argument slot of a parameterized <see cref="PaletteCommand"/>, rendered as a
/// <c>&lt;Name&gt;</c> token in the palette with its own autofill suggestion dropdown.</summary>
public class PaletteArg
{
    public string Name { get; set; } = string.Empty;   // "Priority" -> shown as <Priority>
    public bool Required { get; set; }
    public PaletteArgKind Kind { get; set; } = PaletteArgKind.FreeText;

    /// <summary>Produces the current suggestion options (evaluated live so entity lists stay fresh).
    /// Null for pure free-text/number/date args with no presets.</summary>
    public Func<IEnumerable<PaletteArgOption>>? Options { get; set; }
}

/// <summary>A selectable suggestion for a <see cref="PaletteArg"/>. <paramref name="Value"/> is the
/// committed text; <paramref name="Tag"/> optionally carries a backing object (e.g. a Guid/entity).</summary>
public record PaletteArgOption(string Display, string Value, object? Tag = null);

/// <summary>A committed argument value passed to <see cref="PaletteCommand.ExecuteWithArgsAsync"/>.</summary>
public record PaletteArgValue(string Raw, object? Tag = null);

public class PaletteCommand
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string? KeyboardShortcut { get; set; }
    public string? Icon { get; set; }

    /// <summary>Tooltip / one-line help shown on the palette row.</summary>
    public string? Description { get; set; }

    /// <summary>Display string of the argument template, e.g. "&lt;Name&gt; &lt;Priority&gt;". Shown
    /// muted next to the title in search mode. Empty for leaf commands.</summary>
    public string? ArgumentHint { get; set; }

    /// <summary>Ordered argument specs. Null/empty = a leaf command (runs immediately via
    /// <see cref="ExecuteAsync"/>); non-empty = the palette enters guided token entry.</summary>
    public IReadOnlyList<PaletteArg>? Args { get; set; }

    /// <summary>Leaf-command handler (no arguments).</summary>
    public Func<Task>? ExecuteAsync { get; set; }

    /// <summary>Parameterized-command handler; receives the filled argument values positionally.</summary>
    public Func<IReadOnlyList<PaletteArgValue>, Task>? ExecuteWithArgsAsync { get; set; }

    public Func<bool>? CanExecute { get; set; }
}

public interface ICommandPaletteService
{
    IReadOnlyList<PaletteCommand> AllCommands { get; }

    void Register(PaletteCommand command);
    void Unregister(string commandId);
    Task<IReadOnlyList<PaletteCommand>> FilterAsync(string query, CancellationToken ct = default);
    Task ExecuteAsync(string commandId);
    void Show();
    void Hide();
    bool IsVisible { get; }
    event EventHandler<bool>? VisibilityChanged;
}

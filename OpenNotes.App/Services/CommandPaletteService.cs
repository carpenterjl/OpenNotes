using Microsoft.Extensions.Logging;
using OpenNotes.Interfaces;

namespace OpenNotes.Services;

public class CommandPaletteService : ICommandPaletteService
{
    private readonly ILogger<CommandPaletteService> _logger;
    private readonly Dictionary<string, PaletteCommand> _commands = [];
    private bool _isVisible;

    public IReadOnlyList<PaletteCommand> AllCommands => [.. _commands.Values];
    public bool IsVisible => _isVisible;

    public event EventHandler<bool>? VisibilityChanged;

    public CommandPaletteService(ILogger<CommandPaletteService> logger)
    {
        _logger = logger;
    }

    public void Register(PaletteCommand command)
    {
        _commands[command.Id] = command;
    }

    public void Unregister(string commandId)
    {
        _commands.Remove(commandId);
    }

    public Task<IReadOnlyList<PaletteCommand>> FilterAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            IReadOnlyList<PaletteCommand> all = [.. _commands.Values
                .Where(c => c.CanExecute?.Invoke() != false)
                .OrderBy(c => c.Category).ThenBy(c => c.Title)];
            return Task.FromResult(all);
        }

        var lower = query.ToLowerInvariant();
        IReadOnlyList<PaletteCommand> filtered = [.. _commands.Values
            .Where(c => c.CanExecute?.Invoke() != false &&
                        (c.Title.Contains(lower, StringComparison.OrdinalIgnoreCase) ||
                         c.Category?.Contains(lower, StringComparison.OrdinalIgnoreCase) == true))
            .OrderBy(c => c.Title.StartsWith(lower, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(c => c.Title)];

        return Task.FromResult(filtered);
    }

    public async Task ExecuteAsync(string commandId)
    {
        if (!_commands.TryGetValue(commandId, out var cmd))
        {
            _logger.LogWarning("Command '{Id}' not found", commandId);
            return;
        }

        Hide();
        if (cmd.ExecuteAsync is not null)
            await cmd.ExecuteAsync();
    }

    public void Show()
    {
        _isVisible = true;
        VisibilityChanged?.Invoke(this, true);
    }

    public void Hide()
    {
        _isVisible = false;
        VisibilityChanged?.Invoke(this, false);
    }
}

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenNotes.Interfaces;

namespace OpenNotes.Persistence;

public class AutosaveService : IAutosaveService, IHostedService, IDisposable
{
    private readonly ILogger<AutosaveService> _logger;
    private Timer? _timer;
    private volatile bool _isDirty;

    public bool IsEnabled { get; set; } = true;
    public int IntervalSeconds { get; set; } = 30;
    public DateTime? LastSavedAt { get; private set; }
    public bool IsDirty => _isDirty;

    public AutosaveService(ILogger<AutosaveService> logger)
    {
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _timer = new Timer(OnTimerElapsed, null,
            TimeSpan.FromSeconds(IntervalSeconds),
            TimeSpan.FromSeconds(IntervalSeconds));
        _logger.LogInformation("Autosave started with {Interval}s interval", IntervalSeconds);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        return Task.CompletedTask;
    }

    public void MarkDirty() => _isDirty = true;

    public void MarkClean()
    {
        _isDirty = false;
        LastSavedAt = DateTime.UtcNow;
    }

    public async Task SaveNowAsync(CancellationToken ct = default)
    {
        if (!_isDirty)
            return;
        await PerformSaveAsync(ct);
    }

    private async void OnTimerElapsed(object? state)
    {
        if (!IsEnabled || !_isDirty)
            return;
        try
        {
            await PerformSaveAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Autosave failed");
        }
    }

    private async Task PerformSaveAsync(CancellationToken ct)
    {
        _logger.LogDebug("Autosave triggered");
        // Workspace service persists its own metadata on changes.
        // This is a hook for future in-memory dirty state flush.
        MarkClean();
        await Task.CompletedTask;
    }

    public void Dispose() => _timer?.Dispose();
}

using Microsoft.Extensions.Logging;

namespace OpenNotes.Plugins;

// Stub — plugin loading will scan a plugins/ directory for assemblies
public class PluginLoader
{
    private readonly ILogger<PluginLoader> _logger;

    public PluginLoader(ILogger<PluginLoader> logger)
    {
        _logger = logger;
    }

    public Task<IReadOnlyList<IPlugin>> LoadPluginsAsync(string pluginsDirectory, CancellationToken ct = default)
    {
        _logger.LogDebug("Plugin loading not yet implemented");
        return Task.FromResult<IReadOnlyList<IPlugin>>([]);
    }
}

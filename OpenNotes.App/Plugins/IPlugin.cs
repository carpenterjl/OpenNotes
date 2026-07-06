namespace OpenNotes.Plugins;

// Extension point for future plugin system
public interface IPlugin
{
    string Id { get; }
    string Name { get; }
    string Version { get; }
    string Author { get; }
    Task InitializeAsync(IServiceProvider services, CancellationToken ct = default);
    Task ShutdownAsync(CancellationToken ct = default);
}

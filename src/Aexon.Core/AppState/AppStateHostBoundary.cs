namespace Aexon.Core.AppState;

/// <summary>
/// Defines the contract for app state host boundary.
/// </summary>
public interface IAppStateHostBoundary
{
    Task ApplyAsync(AppStateSnapshot snapshot, CancellationToken cancellationToken = default);
}

/// <summary>
/// Publishes app-state snapshots to the host boundary.
/// </summary>
public sealed class AppStateHostBridge
{
    private readonly IAppStateStore _store;
    private readonly IAppStateHostBoundary _boundary;

    public AppStateHostBridge(
        IAppStateStore store,
        IAppStateHostBoundary boundary)
    {
        _store = store;
        _boundary = boundary;
    }

    public Task PublishAsync(CancellationToken cancellationToken = default) =>
        _boundary.ApplyAsync(_store.Current, cancellationToken);
}

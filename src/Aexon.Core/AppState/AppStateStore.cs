namespace Aexon.Core.AppState;

/// <summary>
/// Defines the contract for app state store.
/// </summary>
public interface IAppStateStore
{
    AppStateSnapshot Current { get; }
    event Action<AppStateSnapshot>? Changed;
    AppStateSnapshot Update(Func<AppStateSnapshot, AppStateSnapshot> update);
    void Reset(AppStateSnapshot? snapshot = null);
}

/// <summary>
/// Stores the current app-state snapshot and publishes changes.
/// </summary>
public sealed class AppStateStore : IAppStateStore
{
    private AppStateSnapshot _current;

    public AppStateStore(AppStateSnapshot? initialState = null)
    {
        _current = initialState ?? new AppStateSnapshot();
    }

    public AppStateSnapshot Current => _current;

    public event Action<AppStateSnapshot>? Changed;

    public AppStateSnapshot Update(Func<AppStateSnapshot, AppStateSnapshot> update)
    {
        var next = update(_current) ?? throw new InvalidOperationException("App state update returned null.");
        _current = next;
        Changed?.Invoke(next);
        return next;
    }

    public void Reset(AppStateSnapshot? snapshot = null)
    {
        _current = snapshot ?? new AppStateSnapshot();
        Changed?.Invoke(_current);
    }
}

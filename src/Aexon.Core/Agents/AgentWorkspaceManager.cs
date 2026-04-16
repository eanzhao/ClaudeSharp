namespace Aexon.Core.Agents;

/// <summary>
/// Defines the contract for acquiring an isolated subagent workspace.
/// </summary>
public interface IAgentWorkspaceManager
{
    Task<AgentWorkspaceLease> AcquireAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents an acquired subagent workspace that should be released after use.
/// </summary>
public sealed class AgentWorkspaceLease : IAsyncDisposable
{
    private readonly Func<ValueTask>? _disposeAsync;

    public AgentWorkspaceLease(
        string workingDirectory,
        string rootDirectory,
        bool isIsolated,
        string? description = null,
        Func<ValueTask>? disposeAsync = null,
        string? repositoryRoot = null)
    {
        WorkingDirectory = workingDirectory;
        RootDirectory = rootDirectory;
        IsIsolated = isIsolated;
        Description = description;
        RepositoryRoot = string.IsNullOrWhiteSpace(repositoryRoot)
            ? rootDirectory
            : repositoryRoot;
        _disposeAsync = disposeAsync;
    }

    public string WorkingDirectory { get; }

    public string RootDirectory { get; }

    public bool IsIsolated { get; }

    public string? Description { get; }

    public string RepositoryRoot { get; }

    public static AgentWorkspaceLease Passthrough(
        string workingDirectory,
        string? description = null) =>
        new(
            workingDirectory,
            workingDirectory,
            isIsolated: false,
            description,
            repositoryRoot: workingDirectory);

    public async ValueTask DisposeAsync()
    {
        if (_disposeAsync is not null)
            await _disposeAsync();
    }
}

namespace ClaudeSharp.Core.Agents;

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
        Func<ValueTask>? disposeAsync = null)
    {
        WorkingDirectory = workingDirectory;
        RootDirectory = rootDirectory;
        IsIsolated = isIsolated;
        Description = description;
        _disposeAsync = disposeAsync;
    }

    public string WorkingDirectory { get; }

    public string RootDirectory { get; }

    public bool IsIsolated { get; }

    public string? Description { get; }

    public static AgentWorkspaceLease Passthrough(
        string workingDirectory,
        string? description = null) =>
        new(
            workingDirectory,
            workingDirectory,
            isIsolated: false,
            description);

    public async ValueTask DisposeAsync()
    {
        if (_disposeAsync is not null)
            await _disposeAsync();
    }
}

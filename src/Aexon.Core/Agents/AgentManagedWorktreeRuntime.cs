namespace Aexon.Core.Agents;

public sealed class AgentManagedWorktree
{
    public required string Id { get; init; }
    public string? Name { get; set; }
    public required string SourceWorkingDirectory { get; init; }
    public required string WorkingDirectory { get; init; }
    public required string RootDirectory { get; init; }
    public required string RepositoryRoot { get; init; }
    public string? Description { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public AgentManagedWorktree Clone() =>
        new()
        {
            Id = Id,
            Name = Name,
            SourceWorkingDirectory = SourceWorkingDirectory,
            WorkingDirectory = WorkingDirectory,
            RootDirectory = RootDirectory,
            RepositoryRoot = RepositoryRoot,
            Description = Description,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
        };
}

public enum AgentManagedWorktreeExitStatus
{
    Exited,
    NotFound,
    HasChanges,
}

public sealed record AgentManagedWorktreeEnterResult(
    AgentManagedWorktree Worktree,
    int AutoCleanedCount);

public sealed record AgentManagedWorktreeExitResult(
    AgentManagedWorktreeExitStatus Status,
    AgentManagedWorktree? Worktree);

public sealed record AgentManagedWorktreeCleanupResult(
    IReadOnlyList<AgentManagedWorktree> RemovedWorktrees)
{
    public int RemovedCount => RemovedWorktrees.Count;
}

public interface IAgentManagedWorktreeRuntime
{
    Task<AgentManagedWorktreeEnterResult> EnterAsync(
        string workingDirectory,
        string? name = null,
        bool cleanupUnchanged = true,
        CancellationToken cancellationToken = default);

    AgentManagedWorktree? Get(string id);

    IReadOnlyList<AgentManagedWorktree> List();

    Task<AgentManagedWorktreeExitResult> ExitAsync(
        string id,
        bool force = false,
        CancellationToken cancellationToken = default);

    Task<AgentManagedWorktreeCleanupResult> CleanupUnchangedAsync(
        CancellationToken cancellationToken = default);
}

public sealed class InMemoryAgentManagedWorktreeRuntime : IAgentManagedWorktreeRuntime
{
    private readonly object _gate = new();
    private readonly IAgentWorkspaceManager _workspaceManager;
    private readonly Dictionary<string, WorktreeEntry> _worktrees =
        new(StringComparer.OrdinalIgnoreCase);
    private int _sequence;

    public InMemoryAgentManagedWorktreeRuntime(
        IAgentWorkspaceManager? workspaceManager = null,
        IEnumerable<AgentManagedWorktree>? restoredWorktrees = null)
    {
        _workspaceManager = workspaceManager ?? new GitWorktreeAgentWorkspaceManager();

        if (restoredWorktrees == null)
            return;

        foreach (var worktree in restoredWorktrees)
        {
            _worktrees[worktree.Id] = new WorktreeEntry(worktree.Clone(), Lease: null);
            _sequence = Math.Max(_sequence, ParseSequence(worktree.Id));
        }
    }

    public async Task<AgentManagedWorktreeEnterResult> EnterAsync(
        string workingDirectory,
        string? name = null,
        bool cleanupUnchanged = true,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        AgentManagedWorktreeCleanupResult cleanupResult = new([]);
        if (cleanupUnchanged)
            cleanupResult = await CleanupUnchangedAsync(cancellationToken);

        var normalizedWorkingDirectory = Path.GetFullPath(workingDirectory);
        var lease = await _workspaceManager.AcquireAsync(normalizedWorkingDirectory, cancellationToken);
        if (!lease.IsIsolated)
        {
            await lease.DisposeAsync();
            throw new InvalidOperationException(
                lease.Description ?? "Failed to create an isolated git worktree.");
        }

        var worktree = new AgentManagedWorktree
        {
            Id = $"worktree-{Interlocked.Increment(ref _sequence)}",
            Name = Normalize(name),
            SourceWorkingDirectory = normalizedWorkingDirectory,
            WorkingDirectory = lease.WorkingDirectory,
            RootDirectory = lease.RootDirectory,
            RepositoryRoot = lease.RepositoryRoot,
            Description = lease.Description,
        };

        lock (_gate)
            _worktrees[worktree.Id] = new WorktreeEntry(worktree, lease);

        return new AgentManagedWorktreeEnterResult(
            worktree.Clone(),
            cleanupResult.RemovedCount);
    }

    public AgentManagedWorktree? Get(string id)
    {
        lock (_gate)
            return _worktrees.TryGetValue(id, out var entry) ? entry.Worktree.Clone() : null;
    }

    public IReadOnlyList<AgentManagedWorktree> List()
    {
        lock (_gate)
        {
            return _worktrees.Values
                .Select(entry => entry.Worktree.Clone())
                .OrderByDescending(worktree => worktree.CreatedAt)
                .ThenBy(worktree => worktree.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public async Task<AgentManagedWorktreeExitResult> ExitAsync(
        string id,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(id))
            return new AgentManagedWorktreeExitResult(AgentManagedWorktreeExitStatus.NotFound, null);

        var normalizedId = id.Trim();
        WorktreeEntry? entry;
        lock (_gate)
            _worktrees.TryGetValue(normalizedId, out entry);

        if (entry == null)
            return new AgentManagedWorktreeExitResult(AgentManagedWorktreeExitStatus.NotFound, null);

        if (Directory.Exists(entry.Worktree.RootDirectory) &&
            !force &&
            await GitWorkspaceUtilities.HasUncommittedChangesAsync(
                entry.Worktree.RootDirectory,
                cancellationToken))
        {
            return new AgentManagedWorktreeExitResult(
                AgentManagedWorktreeExitStatus.HasChanges,
                entry.Worktree.Clone());
        }

        await ReleaseAsync(entry, force, cancellationToken);

        lock (_gate)
            _worktrees.Remove(normalizedId);

        return new AgentManagedWorktreeExitResult(
            AgentManagedWorktreeExitStatus.Exited,
            entry.Worktree.Clone());
    }

    public async Task<AgentManagedWorktreeCleanupResult> CleanupUnchangedAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var removed = new List<AgentManagedWorktree>();
        foreach (var worktree in List())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Directory.Exists(worktree.RootDirectory) &&
                await GitWorkspaceUtilities.HasUncommittedChangesAsync(
                    worktree.RootDirectory,
                    cancellationToken))
            {
                continue;
            }

            var result = await ExitAsync(worktree.Id, force: true, cancellationToken);
            if (result.Status == AgentManagedWorktreeExitStatus.Exited &&
                result.Worktree != null)
            {
                removed.Add(result.Worktree);
            }
        }

        return new AgentManagedWorktreeCleanupResult(removed);
    }

    private async Task ReleaseAsync(
        WorktreeEntry entry,
        bool force,
        CancellationToken cancellationToken)
    {
        if (entry.Lease != null)
        {
            await entry.Lease.DisposeAsync();
            return;
        }

        if (!Directory.Exists(entry.Worktree.RootDirectory))
            return;

        var arguments = force
            ? new[] { "worktree", "remove", "--force", entry.Worktree.RootDirectory }
            : new[] { "worktree", "remove", entry.Worktree.RootDirectory };
        var result = await GitWorkspaceUtilities.RunGitAsync(
            entry.Worktree.RepositoryRoot,
            arguments,
            cancellationToken);
        if (result.ExitCode != 0)
            throw new InvalidOperationException(result.Stderr.Trim());

        TryDeleteDirectory(entry.Worktree.RootDirectory);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int ParseSequence(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) ||
            !id.StartsWith("worktree-", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        return int.TryParse(id["worktree-".Length..], out var value)
            ? value
            : 0;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    private sealed record WorktreeEntry(
        AgentManagedWorktree Worktree,
        AgentWorkspaceLease? Lease);
}

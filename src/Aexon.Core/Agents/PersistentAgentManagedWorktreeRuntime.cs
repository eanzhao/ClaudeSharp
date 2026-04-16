using Aexon.Core.Storage;

namespace Aexon.Core.Agents;

public sealed class PersistentAgentManagedWorktreeRuntime : IAgentManagedWorktreeRuntime
{
    private readonly InMemoryAgentManagedWorktreeRuntime _inner;
    private readonly IConversationJournal _journal;

    private PersistentAgentManagedWorktreeRuntime(
        InMemoryAgentManagedWorktreeRuntime inner,
        IConversationJournal journal)
    {
        _inner = inner;
        _journal = journal;
    }

    public static async Task<PersistentAgentManagedWorktreeRuntime> CreateAsync(
        IConversationJournal journal,
        IReadOnlyList<TranscriptMetadataEntry>? metadataEntries = null,
        IAgentWorkspaceManager? workspaceManager = null,
        CancellationToken cancellationToken = default)
    {
        var restored = AgentManagedWorktreePersistence.Restore(metadataEntries ?? []);
        var runtime = new PersistentAgentManagedWorktreeRuntime(
            new InMemoryAgentManagedWorktreeRuntime(workspaceManager, restored),
            journal);
        await runtime.NormalizeRecoveredStateAsync(cancellationToken);
        return runtime;
    }

    public async Task<AgentManagedWorktreeEnterResult> EnterAsync(
        string workingDirectory,
        string? name = null,
        bool cleanupUnchanged = true,
        CancellationToken cancellationToken = default)
    {
        var cleanupResult = new AgentManagedWorktreeCleanupResult([]);
        if (cleanupUnchanged)
            cleanupResult = await CleanupUnchangedAsync(cancellationToken);

        var result = await _inner.EnterAsync(
            workingDirectory,
            name,
            cleanupUnchanged: false,
            cancellationToken);
        Persist(AgentManagedWorktreePersistence.CreateWorktreeEntry(result.Worktree));
        return result with
        {
            AutoCleanedCount = cleanupResult.RemovedCount,
        };
    }

    public AgentManagedWorktree? Get(string id) => _inner.Get(id);

    public IReadOnlyList<AgentManagedWorktree> List() => _inner.List();

    public async Task<AgentManagedWorktreeExitResult> ExitAsync(
        string id,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        var result = await _inner.ExitAsync(id, force, cancellationToken);
        if (result.Status == AgentManagedWorktreeExitStatus.Exited &&
            result.Worktree != null)
        {
            Persist(AgentManagedWorktreePersistence.CreateWorktreeDeletedEntry(result.Worktree.Id));
        }

        return result;
    }

    public async Task<AgentManagedWorktreeCleanupResult> CleanupUnchangedAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await _inner.CleanupUnchangedAsync(cancellationToken);
        foreach (var worktree in result.RemovedWorktrees)
            Persist(AgentManagedWorktreePersistence.CreateWorktreeDeletedEntry(worktree.Id));

        return result;
    }

    private async Task NormalizeRecoveredStateAsync(CancellationToken cancellationToken)
    {
        foreach (var worktree in _inner.List().ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Directory.Exists(worktree.RootDirectory))
                continue;

            var result = await _inner.ExitAsync(worktree.Id, force: true, cancellationToken);
            if (result.Status == AgentManagedWorktreeExitStatus.Exited &&
                result.Worktree != null)
            {
                await _journal.AppendMetadataEntryAsync(
                    AgentManagedWorktreePersistence.CreateWorktreeDeletedEntry(result.Worktree.Id),
                    cancellationToken);
            }
        }
    }

    private void Persist(TranscriptMetadataEntry entry)
    {
        try
        {
            _journal.AppendMetadataEntryAsync(entry).GetAwaiter().GetResult();
        }
        catch
        {
            // Worktree persistence is best effort.
        }
    }
}

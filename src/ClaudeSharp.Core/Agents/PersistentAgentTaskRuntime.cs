using ClaudeSharp.Core.Storage;

namespace ClaudeSharp.Core.Agents;

/// <summary>
/// Persists agent task runtime state into the conversation transcript.
/// </summary>
public sealed class PersistentAgentTaskRuntime : IAgentTaskRuntime
{
    private readonly InMemoryAgentTaskRuntime _inner;
    private readonly IConversationJournal _journal;

    private PersistentAgentTaskRuntime(
        InMemoryAgentTaskRuntime inner,
        IConversationJournal journal)
    {
        _inner = inner;
        _journal = journal;
    }

    public static async Task<PersistentAgentTaskRuntime> CreateAsync(
        IConversationJournal journal,
        IReadOnlyList<TranscriptMetadataEntry>? metadataEntries = null,
        CancellationToken cancellationToken = default)
    {
        var restored = AgentTaskPersistence.Restore(metadataEntries ?? []);
        var runtime = new PersistentAgentTaskRuntime(
            new InMemoryAgentTaskRuntime(restored.WorkItems, restored.BackgroundRuns),
            journal);

        await runtime.NormalizeRecoveredStateAsync(cancellationToken);
        return runtime;
    }

    public AgentWorkItem CreateWorkItem(
        string title,
        string? description = null,
        string? owner = null)
    {
        var item = _inner.CreateWorkItem(title, description, owner);
        Persist(AgentTaskPersistence.CreateWorkItemEntry(item));
        return item;
    }

    public AgentWorkItem? GetWorkItem(string id) => _inner.GetWorkItem(id);

    public IReadOnlyList<AgentWorkItem> ListWorkItems() => _inner.ListWorkItems();

    public bool UpdateWorkItem(string id, Action<AgentWorkItem> update)
    {
        var updated = _inner.UpdateWorkItem(id, update);
        if (updated && _inner.GetWorkItem(id) is { } item)
            Persist(AgentTaskPersistence.CreateWorkItemEntry(item));

        return updated;
    }

    public AgentBackgroundRun StartBackgroundRun(
        string name,
        string? owner = null,
        string? workItemId = null,
        AgentBackgroundRunStatus initialStatus = AgentBackgroundRunStatus.Running)
    {
        var run = _inner.StartBackgroundRun(name, owner, workItemId, initialStatus);
        Persist(AgentTaskPersistence.CreateBackgroundRunEntry(run));
        return run;
    }

    public AgentBackgroundRun? GetBackgroundRun(string id) => _inner.GetBackgroundRun(id);

    public IReadOnlyList<AgentBackgroundRun> ListBackgroundRuns() => _inner.ListBackgroundRuns();

    public bool UpdateBackgroundRun(string id, Action<AgentBackgroundRun> update)
    {
        var updated = _inner.UpdateBackgroundRun(id, update);
        if (updated && _inner.GetBackgroundRun(id) is { } run)
            Persist(AgentTaskPersistence.CreateBackgroundRunEntry(run));

        return updated;
    }

    public bool AppendBackgroundRunOutput(string id, string chunk)
    {
        var updated = _inner.AppendBackgroundRunOutput(id, chunk);
        if (updated && !string.IsNullOrWhiteSpace(chunk))
            Persist(AgentTaskPersistence.CreateBackgroundOutputEntry(id, chunk));

        return updated;
    }

    public bool RegisterBackgroundRunCancellation(string id, Action cancel) =>
        _inner.RegisterBackgroundRunCancellation(id, cancel);

    public AgentBackgroundRunCancellationResult RequestBackgroundRunCancellation(
        string id,
        string? reason = null)
    {
        var result = _inner.RequestBackgroundRunCancellation(id, reason);
        if (result == AgentBackgroundRunCancellationResult.Requested &&
            _inner.GetBackgroundRun(id) is { } run)
        {
            Persist(AgentTaskPersistence.CreateBackgroundRunEntry(run));
        }

        return result;
    }

    public bool StopBackgroundRun(string id, string? reason = null)
    {
        var updated = _inner.StopBackgroundRun(id, reason);
        if (updated && _inner.GetBackgroundRun(id) is { } run)
            Persist(AgentTaskPersistence.CreateBackgroundRunEntry(run));

        return updated;
    }

    public bool FailBackgroundRun(string id, string? reason = null)
    {
        var updated = _inner.FailBackgroundRun(id, reason);
        if (updated && _inner.GetBackgroundRun(id) is { } run)
            Persist(AgentTaskPersistence.CreateBackgroundRunEntry(run));

        return updated;
    }

    public bool CancelBackgroundRun(string id, string? reason = null)
    {
        var updated = _inner.CancelBackgroundRun(id, reason);
        if (updated && _inner.GetBackgroundRun(id) is { } run)
            Persist(AgentTaskPersistence.CreateBackgroundRunEntry(run));

        return updated;
    }

    private async Task NormalizeRecoveredStateAsync(CancellationToken cancellationToken)
    {
        foreach (var run in _inner.ListBackgroundRuns()
                     .Where(run => run.Status is AgentBackgroundRunStatus.Running or
                         AgentBackgroundRunStatus.CancellationRequested or
                         AgentBackgroundRunStatus.Queued)
                     .ToArray())
        {
            var originalStatus = run.Status;
            if (originalStatus == AgentBackgroundRunStatus.CancellationRequested)
            {
                _inner.CancelBackgroundRun(run.Id, run.StopReason ?? "Cancellation requested before the previous ClaudeSharp process ended.");
            }
            else
            {
                _inner.FailBackgroundRun(
                    run.Id,
                    originalStatus == AgentBackgroundRunStatus.Queued
                        ? "Background run was still queued when the previous ClaudeSharp process exited."
                        : "Background run ended when the previous ClaudeSharp process exited.");
            }

            await _journal.AppendMetadataEntryAsync(
                AgentTaskPersistence.CreateBackgroundRunEntry(_inner.GetBackgroundRun(run.Id)!),
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(run.WorkItemId) &&
                _inner.GetWorkItem(run.WorkItemId) is { } workItem &&
                workItem.Status == AgentWorkItemStatus.InProgress)
            {
                if (originalStatus == AgentBackgroundRunStatus.CancellationRequested)
                {
                    _inner.UpdateWorkItem(workItem.Id, item => item.Status = AgentWorkItemStatus.Cancelled);
                }
                else
                {
                    _inner.UpdateWorkItem(workItem.Id, item => item.Status = AgentWorkItemStatus.Blocked);
                }

                await _journal.AppendMetadataEntryAsync(
                    AgentTaskPersistence.CreateWorkItemEntry(_inner.GetWorkItem(workItem.Id)!),
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
            // Agent runtime state persistence is best-effort; in-memory state still updates.
        }
    }
}

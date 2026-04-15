using Aexon.Core.Storage;

namespace Aexon.Core.Agents;

/// <summary>
/// Persists agent task runtime state into the conversation transcript.
/// </summary>
public sealed class PersistentAgentTaskRuntime : IAgentTaskRuntime
{
    private readonly InMemoryAgentTaskRuntime _inner;
    private readonly IConversationJournal _journal;
    private readonly AgentRetentionPolicy? _autoPrunePolicy;

    private PersistentAgentTaskRuntime(
        InMemoryAgentTaskRuntime inner,
        IConversationJournal journal,
        AgentRetentionPolicy? autoPrunePolicy)
    {
        _inner = inner;
        _journal = journal;
        _autoPrunePolicy = autoPrunePolicy;
    }

    public static async Task<PersistentAgentTaskRuntime> CreateAsync(
        IConversationJournal journal,
        IReadOnlyList<TranscriptMetadataEntry>? metadataEntries = null,
        AgentRetentionPolicy? autoPrunePolicy = null,
        CancellationToken cancellationToken = default)
    {
        var restored = AgentTaskPersistence.Restore(metadataEntries ?? []);
        var runtime = new PersistentAgentTaskRuntime(
            new InMemoryAgentTaskRuntime(restored.WorkItems, restored.BackgroundRuns),
            journal,
            autoPrunePolicy);

        await runtime.NormalizeRecoveredStateAsync(cancellationToken);
        return runtime;
    }

    public string AllocateSubagentId() => _inner.AllocateSubagentId();

    public AgentWorkItem CreateWorkItem(
        string title,
        string? description = null,
        string? owner = null,
        string? subagentId = null)
    {
        var item = _inner.CreateWorkItem(title, description, owner, subagentId);
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
        AgentBackgroundRunStatus initialStatus = AgentBackgroundRunStatus.Running,
        string? subagentId = null)
    {
        var run = _inner.StartBackgroundRun(name, owner, workItemId, initialStatus, subagentId);
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

    public AgentBackgroundRunCancellationResult RequestBackgroundRunCancellation(
        string id,
        AgentTerminationInfo termination)
    {
        var result = _inner.RequestBackgroundRunCancellation(id, termination);
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
        if (updated)
            PersistTerminalTransition(id);

        return updated;
    }

    public bool StopBackgroundRun(string id, AgentTerminationInfo termination)
    {
        var updated = _inner.StopBackgroundRun(id, termination);
        if (updated)
            PersistTerminalTransition(id);

        return updated;
    }

    public bool FailBackgroundRun(string id, string? reason = null)
    {
        var updated = _inner.FailBackgroundRun(id, reason);
        if (updated)
            PersistTerminalTransition(id);

        return updated;
    }

    public bool FailBackgroundRun(string id, AgentTerminationInfo termination)
    {
        var updated = _inner.FailBackgroundRun(id, termination);
        if (updated)
            PersistTerminalTransition(id);

        return updated;
    }

    public bool CancelBackgroundRun(string id, string? reason = null)
    {
        var updated = _inner.CancelBackgroundRun(id, reason);
        if (updated)
            PersistTerminalTransition(id);

        return updated;
    }

    public bool CancelBackgroundRun(string id, AgentTerminationInfo termination)
    {
        var updated = _inner.CancelBackgroundRun(id, termination);
        if (updated)
            PersistTerminalTransition(id);

        return updated;
    }

    public AgentTerminationEvent? GetLastTerminationEvent(string backgroundRunId) =>
        _inner.GetLastTerminationEvent(backgroundRunId);

    public AgentPruneResult PruneHistory(AgentRetentionPolicy? policy = null)
    {
        var result = _inner.PruneHistory(policy);
        PersistPruneResult(result);
        return result;
    }

    private void PersistTerminalTransition(string id)
    {
        if (_inner.GetBackgroundRun(id) is { } run)
        {
            Persist(AgentTaskPersistence.CreateBackgroundRunEntry(run));
            if (_inner.GetLastTerminationEvent(id) is { } terminationEvent)
                Persist(AgentTaskPersistence.CreateTerminationEventEntry(terminationEvent));
            ApplyAutoPrune();
        }
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
                _inner.CancelBackgroundRun(run.Id, AgentTerminationInfo.Cancelled(
                    run.StopReason ?? "Cancellation requested before the previous Aexon process ended.",
                    AgentTerminationSource.System));
            }
            else
            {
                _inner.FailBackgroundRun(run.Id, AgentTerminationInfo.Failed(
                    originalStatus == AgentBackgroundRunStatus.Queued
                        ? "Background run was still queued when the previous Aexon process exited."
                        : "Background run ended when the previous Aexon process exited.",
                    AgentTerminationSource.System));
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

        await PersistPruneResultAsync(
            _inner.PruneHistory(_autoPrunePolicy),
            cancellationToken);
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

    private void ApplyAutoPrune()
    {
        if (_autoPrunePolicy == null)
            return;

        PersistPruneResult(_inner.PruneHistory(_autoPrunePolicy));
    }

    private void PersistPruneResult(AgentPruneResult result)
    {
        foreach (var workItemId in result.RemovedWorkItemIds)
            Persist(AgentTaskPersistence.CreateWorkItemDeletedEntry(workItemId));

        foreach (var backgroundRunId in result.RemovedBackgroundRunIds)
            Persist(AgentTaskPersistence.CreateBackgroundRunDeletedEntry(backgroundRunId));
    }

    private async Task PersistPruneResultAsync(
        AgentPruneResult result,
        CancellationToken cancellationToken)
    {
        foreach (var workItemId in result.RemovedWorkItemIds)
        {
            await _journal.AppendMetadataEntryAsync(
                AgentTaskPersistence.CreateWorkItemDeletedEntry(workItemId),
                cancellationToken);
        }

        foreach (var backgroundRunId in result.RemovedBackgroundRunIds)
        {
            await _journal.AppendMetadataEntryAsync(
                AgentTaskPersistence.CreateBackgroundRunDeletedEntry(backgroundRunId),
                cancellationToken);
        }
    }
}

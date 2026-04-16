using Aexon.Core.Storage;

namespace Aexon.Core.Agents;

public sealed class PersistentAgentRemoteTriggerRuntime : IAgentRemoteTriggerRuntime
{
    private readonly InMemoryAgentRemoteTriggerRuntime _inner;
    private readonly IConversationJournal _journal;

    private PersistentAgentRemoteTriggerRuntime(
        InMemoryAgentRemoteTriggerRuntime inner,
        IConversationJournal journal)
    {
        _inner = inner;
        _journal = journal;
    }

    public static Task<PersistentAgentRemoteTriggerRuntime> CreateAsync(
        IConversationJournal journal,
        IAgentTaskRuntime taskRuntime,
        IReadOnlyList<TranscriptMetadataEntry>? metadataEntries = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(
            new PersistentAgentRemoteTriggerRuntime(
                new InMemoryAgentRemoteTriggerRuntime(
                    taskRuntime,
                    AgentRemoteTriggerPersistence.Restore(metadataEntries ?? [])),
                journal));
    }

    public AgentRemoteTrigger CreateTrigger(
        string? id,
        string workItemId,
        AgentRemoteTriggerKind kind,
        string? description = null,
        string? schedule = null,
        string? secret = null)
    {
        var trigger = _inner.CreateTrigger(id, workItemId, kind, description, schedule, secret);
        Persist(AgentRemoteTriggerPersistence.CreateTriggerEntry(trigger));
        return trigger;
    }

    public AgentRemoteTrigger? GetTrigger(string id) => _inner.GetTrigger(id);

    public IReadOnlyList<AgentRemoteTrigger> ListTriggers(string? workItemId = null) =>
        _inner.ListTriggers(workItemId);

    public bool DeleteTrigger(string id)
    {
        var deleted = _inner.DeleteTrigger(id);
        if (deleted)
            Persist(AgentRemoteTriggerPersistence.CreateTriggerDeletedEntry(id.Trim()));

        return deleted;
    }

    public AgentRemoteTriggerFireResult FireTrigger(
        string id,
        AgentRemoteTriggerFireRequest request)
    {
        var result = _inner.FireTrigger(id, request);
        if (result.Status == AgentRemoteTriggerFireStatus.Fired &&
            result.Trigger != null)
        {
            Persist(AgentRemoteTriggerPersistence.CreateTriggerEntry(result.Trigger));
        }

        return result;
    }

    private void Persist(TranscriptMetadataEntry entry)
    {
        try
        {
            _journal.AppendMetadataEntryAsync(entry).GetAwaiter().GetResult();
        }
        catch
        {
            // Trigger persistence is best effort.
        }
    }
}

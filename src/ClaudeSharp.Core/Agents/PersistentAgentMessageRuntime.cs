using ClaudeSharp.Core.Storage;

namespace ClaudeSharp.Core.Agents;

/// <summary>
/// Persists mailbox runtime state into the conversation transcript.
/// </summary>
public sealed class PersistentAgentMessageRuntime : IAgentMessageRuntime
{
    private readonly InMemoryAgentMessageRuntime _inner;
    private readonly IConversationJournal _journal;

    private PersistentAgentMessageRuntime(
        InMemoryAgentMessageRuntime inner,
        IConversationJournal journal)
    {
        _inner = inner;
        _journal = journal;
    }

    public static Task<PersistentAgentMessageRuntime> CreateAsync(
        IConversationJournal journal,
        IReadOnlyList<TranscriptMetadataEntry>? metadataEntries = null,
        CancellationToken cancellationToken = default)
    {
        var restored = AgentMessagePersistence.Restore(metadataEntries ?? []);
        var runtime = new PersistentAgentMessageRuntime(
            new InMemoryAgentMessageRuntime(restored.Messages),
            journal);
        return Task.FromResult(runtime);
    }

    public AgentMessage SendMessage(
        string from,
        string to,
        string body,
        AgentMessageKind kind = AgentMessageKind.Note,
        string? subject = null,
        string? relatedMessageId = null,
        AgentMessageProtocol? protocol = null)
    {
        var message = _inner.SendMessage(from, to, body, kind, subject, relatedMessageId, protocol);
        Persist(AgentMessagePersistence.CreateMessageEntry(message));
        return message;
    }

    public AgentMessage SendMessage(
        string from,
        string to,
        AgentMessageKind kind,
        string body,
        string? subject = null,
        string? relatedMessageId = null,
        AgentMessageProtocol? protocol = null)
    {
        var message = _inner.SendMessage(from, to, kind, body, subject, relatedMessageId, protocol);
        Persist(AgentMessagePersistence.CreateMessageEntry(message));
        return message;
    }

    public AgentMessage? GetMessage(string id) => _inner.GetMessage(id);

    public IReadOnlyList<AgentMessage> ListMessages(AgentMessageListOptions? options = null) =>
        _inner.ListMessages(options);

    public IReadOnlyList<AgentMessage> ListThread(string threadId) =>
        _inner.ListThread(threadId);

    public bool MarkMessageRead(string id)
    {
        var updated = _inner.MarkMessageRead(id);
        if (updated && _inner.GetMessage(id) is { } message)
            Persist(AgentMessagePersistence.CreateMessageEntry(message));

        return updated;
    }

    public bool MarkRead(string id) => MarkMessageRead(id);

    public AgentMessageReadResult MarkRecipientMessagesRead(string recipient)
    {
        var result = _inner.MarkRecipientMessagesRead(recipient);
        foreach (var messageId in result.MessageIds)
        {
            if (_inner.GetMessage(messageId) is { } message)
                Persist(AgentMessagePersistence.CreateMessageEntry(message));
        }

        return result;
    }

    public IReadOnlyDictionary<string, int> GetUnreadCounts() => _inner.GetUnreadCounts();

    public AgentMessageSummary GetSummary(AgentMessageListOptions? options = null) =>
        _inner.GetSummary(options);

    private void Persist(TranscriptMetadataEntry entry)
    {
        try
        {
            _journal.AppendMetadataEntryAsync(entry).GetAwaiter().GetResult();
        }
        catch
        {
            // Mailbox persistence is best-effort; in-memory state still updates.
        }
    }
}

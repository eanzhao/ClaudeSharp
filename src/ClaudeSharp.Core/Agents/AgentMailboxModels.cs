namespace ClaudeSharp.Core.Agents;

/// <summary>
/// Defines mailbox message status values.
/// </summary>
public enum AgentMailboxMessageStatus
{
    Unread,
    Read,
    Archived,
}

/// <summary>
/// Represents a mailbox message exchanged between agents or teammates.
/// </summary>
public sealed class AgentMailboxMessage
{
    public required string Id { get; init; }
    public required string ThreadId { get; init; }
    public required string Sender { get; set; }
    public required string Recipient { get; set; }
    public string? Subject { get; set; }
    public required string Body { get; set; }
    public string? ReplyToMessageId { get; set; }
    public AgentMailboxMessageStatus Status { get; set; } = AgentMailboxMessageStatus.Unread;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReadAt { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }

    public void MarkRead()
    {
        if (Status == AgentMailboxMessageStatus.Archived)
            return;

        Status = AgentMailboxMessageStatus.Read;
        ReadAt = ReadAt ?? DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Archive()
    {
        Status = AgentMailboxMessageStatus.Archived;
        ArchivedAt = ArchivedAt ?? DateTimeOffset.UtcNow;
        ReadAt ??= DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public AgentMailboxMessage Clone() =>
        new()
        {
            Id = Id,
            ThreadId = ThreadId,
            Sender = Sender,
            Recipient = Recipient,
            Subject = Subject,
            Body = Body,
            ReplyToMessageId = ReplyToMessageId,
            Status = Status,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            ReadAt = ReadAt,
            ArchivedAt = ArchivedAt,
        };
}

/// <summary>
/// Represents a mailbox summary projected into the app state.
/// </summary>
public sealed record AgentMailboxSummary(
    string Participant,
    int InboxCount,
    int UnreadCount,
    int OutboxCount,
    int ThreadCount,
    string? LatestThreadId,
    string? LatestSubject,
    string? LatestCounterparty,
    DateTimeOffset? LatestMessageAt);

/// <summary>
/// Defines the contract for mailbox runtime.
/// </summary>
public interface IAgentMailboxRuntime
{
    AgentMailboxMessage SendMessage(
        string sender,
        string recipient,
        string body,
        string? subject = null,
        string? replyToMessageId = null);

    AgentMailboxMessage? GetMessage(string messageId);

    IReadOnlyList<AgentMailboxMessage> ListInbox(string participant);

    IReadOnlyList<AgentMailboxMessage> ListOutbox(string participant);

    IReadOnlyList<AgentMailboxMessage> ListThread(string threadId);

    IReadOnlyList<AgentMailboxSummary> ListMailboxes();

    bool MarkAsRead(string messageId);

    bool ArchiveMessage(string messageId);
}

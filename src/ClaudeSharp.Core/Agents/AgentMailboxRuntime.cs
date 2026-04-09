namespace ClaudeSharp.Core.Agents;

/// <summary>
/// Provides an in-memory mailbox runtime for agent-to-agent messaging.
/// </summary>
public sealed class InMemoryAgentMailboxRuntime : IAgentMailboxRuntime
{
    private readonly object _gate = new();
    private readonly Dictionary<string, AgentMailboxMessage> _messages =
        new(StringComparer.OrdinalIgnoreCase);
    private int _messageSequence;
    private int _threadSequence;

    public AgentMailboxMessage SendMessage(
        string sender,
        string recipient,
        string body,
        string? subject = null,
        string? replyToMessageId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sender);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipient);
        ArgumentException.ThrowIfNullOrWhiteSpace(body);

        AgentMailboxMessage? replyTo = null;
        var threadId = CreateThreadId();

        lock (_gate)
        {
            if (!string.IsNullOrWhiteSpace(replyToMessageId) &&
                !_messages.TryGetValue(replyToMessageId.Trim(), out replyTo))
            {
                throw new InvalidOperationException(
                    $"Mailbox message '{replyToMessageId.Trim()}' was not found.");
            }

            if (replyTo != null)
                threadId = replyTo.ThreadId;

            var message = new AgentMailboxMessage
            {
                Id = $"message-{Interlocked.Increment(ref _messageSequence)}",
                ThreadId = threadId,
                Sender = sender.Trim(),
                Recipient = recipient.Trim(),
                Subject = NormalizeOptional(subject),
                Body = body.Trim(),
                ReplyToMessageId = replyTo?.Id,
            };

            _messages[message.Id] = message;
            return message.Clone();
        }
    }

    public AgentMailboxMessage? GetMessage(string messageId)
    {
        if (string.IsNullOrWhiteSpace(messageId))
            return null;

        lock (_gate)
            return _messages.TryGetValue(messageId.Trim(), out var message)
                ? message.Clone()
                : null;
    }

    public IReadOnlyList<AgentMailboxMessage> ListInbox(string participant) =>
        ListMessages(participant, isInbox: true);

    public IReadOnlyList<AgentMailboxMessage> ListOutbox(string participant) =>
        ListMessages(participant, isInbox: false);

    public IReadOnlyList<AgentMailboxMessage> ListThread(string threadId)
    {
        if (string.IsNullOrWhiteSpace(threadId))
            return [];

        lock (_gate)
        {
            return _messages.Values
                .Where(message => string.Equals(message.ThreadId, threadId.Trim(), StringComparison.OrdinalIgnoreCase))
                .OrderBy(message => message.CreatedAt)
                .ThenBy(message => message.Id, StringComparer.OrdinalIgnoreCase)
                .Select(message => message.Clone())
                .ToArray();
        }
    }

    public IReadOnlyList<AgentMailboxSummary> ListMailboxes()
    {
        lock (_gate)
        {
            var participants = _messages.Values
                .Select(message => new[] { message.Sender, message.Recipient })
                .SelectMany(participants => participants)
                .Where(participant => !string.IsNullOrWhiteSpace(participant))
                .Select(participant => participant.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(participant => participant, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return participants
                .Select(BuildSummary)
                .ToArray();
        }
    }

    public bool MarkAsRead(string messageId)
    {
        lock (_gate)
        {
            if (!_messages.TryGetValue(NormalizeId(messageId), out var message))
                return false;

            if (message.Status == AgentMailboxMessageStatus.Archived)
                return false;

            var before = message.Status;
            message.MarkRead();
            return before != message.Status || message.ReadAt != null;
        }
    }

    public bool ArchiveMessage(string messageId)
    {
        lock (_gate)
        {
            if (!_messages.TryGetValue(NormalizeId(messageId), out var message))
                return false;

            message.Archive();
            return true;
        }
    }

    private IReadOnlyList<AgentMailboxMessage> ListMessages(
        string participant,
        bool isInbox)
    {
        if (string.IsNullOrWhiteSpace(participant))
            return [];

        lock (_gate)
        {
            return _messages.Values
                .Where(message =>
                    isInbox
                        ? string.Equals(message.Recipient, participant.Trim(), StringComparison.OrdinalIgnoreCase) &&
                          message.Status != AgentMailboxMessageStatus.Archived
                        : string.Equals(message.Sender, participant.Trim(), StringComparison.OrdinalIgnoreCase) &&
                          message.Status != AgentMailboxMessageStatus.Archived)
                .OrderBy(message => message.CreatedAt)
                .ThenBy(message => message.Id, StringComparer.OrdinalIgnoreCase)
                .Select(message => message.Clone())
                .ToArray();
        }
    }

    private AgentMailboxSummary BuildSummary(string participant)
    {
        var inbox = _messages.Values.Where(message =>
            string.Equals(message.Recipient, participant, StringComparison.OrdinalIgnoreCase) &&
            message.Status != AgentMailboxMessageStatus.Archived).ToArray();
        var outbox = _messages.Values.Where(message =>
            string.Equals(message.Sender, participant, StringComparison.OrdinalIgnoreCase) &&
            message.Status != AgentMailboxMessageStatus.Archived).ToArray();
        var threadIds = _messages.Values
            .Where(message =>
                string.Equals(message.Sender, participant, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(message.Recipient, participant, StringComparison.OrdinalIgnoreCase))
            .Select(message => message.ThreadId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var latest = _messages.Values
            .Where(message =>
                string.Equals(message.Sender, participant, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(message.Recipient, participant, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(message => message.CreatedAt)
            .ThenByDescending(message => message.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        string? latestCounterparty = null;
        if (latest != null)
        {
            latestCounterparty = string.Equals(latest.Sender, participant, StringComparison.OrdinalIgnoreCase)
                ? latest.Recipient
                : latest.Sender;
        }

        return new AgentMailboxSummary(
            participant,
            inbox.Length,
            inbox.Count(message => message.Status == AgentMailboxMessageStatus.Unread),
            outbox.Length,
            threadIds,
            latest?.ThreadId,
            latest?.Subject,
            latestCounterparty,
            latest?.CreatedAt);
    }

    private string CreateThreadId() =>
        $"thread-{Interlocked.Increment(ref _threadSequence)}";

    private static string NormalizeId(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

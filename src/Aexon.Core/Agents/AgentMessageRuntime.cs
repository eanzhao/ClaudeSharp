namespace Aexon.Core.Agents;

/// <summary>
/// Defines the contract for agent mailbox runtime.
/// </summary>
public interface IAgentMessageRuntime
{
    AgentMessage SendMessage(
        string from,
        string to,
        string body,
        AgentMessageKind kind = AgentMessageKind.Note,
        string? subject = null,
        string? relatedMessageId = null,
        AgentMessageProtocol? protocol = null);

    AgentMessage SendMessage(
        string from,
        string to,
        AgentMessageKind kind,
        string body,
        string? subject = null,
        string? relatedMessageId = null,
        AgentMessageProtocol? protocol = null);

    AgentMessage? GetMessage(string id);

    IReadOnlyList<AgentMessage> ListMessages(AgentMessageListOptions? options = null);

    IReadOnlyList<AgentMessage> ListThread(string threadId);

    bool MarkMessageRead(string id);

    bool MarkRead(string id);

    AgentMessageReadResult MarkRecipientMessagesRead(string recipient);

    IReadOnlyDictionary<string, int> GetUnreadCounts();

    AgentMessageSummary GetSummary(AgentMessageListOptions? options = null);
}

/// <summary>
/// Provides an in-memory mailbox runtime.
/// </summary>
public sealed class InMemoryAgentMessageRuntime : IAgentMessageRuntime
{
    private readonly object _gate = new();
    private readonly Dictionary<string, AgentMessage> _messages =
        new(StringComparer.OrdinalIgnoreCase);
    private int _messageSequence;
    private int _threadSequence;

    public InMemoryAgentMessageRuntime(IEnumerable<AgentMessage>? messages = null)
    {
        if (messages == null)
            return;

        foreach (var message in messages)
        {
            var clone = message.Clone();
            _messages[clone.Id] = clone;
            _messageSequence = Math.Max(_messageSequence, ParseSequence(clone.Id, "agent-message-"));
            _threadSequence = Math.Max(_threadSequence, ParseSequence(clone.ThreadId, "thread-"));
        }
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
        return SendMessage(from, to, kind, body, subject, relatedMessageId, protocol);
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
        ArgumentException.ThrowIfNullOrWhiteSpace(from);
        ArgumentException.ThrowIfNullOrWhiteSpace(to);
        ArgumentException.ThrowIfNullOrWhiteSpace(body);

        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            AgentMessage? related = null;
            if (!string.IsNullOrWhiteSpace(relatedMessageId))
            {
                if (!_messages.TryGetValue(relatedMessageId.Trim(), out related))
                    throw new InvalidOperationException($"Mailbox message '{relatedMessageId.Trim()}' was not found.");
            }

            var message = new AgentMessage
            {
                Id = $"agent-message-{Interlocked.Increment(ref _messageSequence)}",
                ThreadId = related?.ThreadId ?? $"thread-{Interlocked.Increment(ref _threadSequence)}",
                From = from.Trim(),
                To = to.Trim(),
                Kind = kind,
                Body = body.Trim(),
                Subject = string.IsNullOrWhiteSpace(subject) ? null : subject.Trim(),
                RelatedMessageId = string.IsNullOrWhiteSpace(relatedMessageId) ? null : relatedMessageId.Trim(),
                Protocol = protocol is null ? null : protocol with { },
                Status = AgentMessageStatus.Delivered,
                CreatedAt = now,
                UpdatedAt = now,
            };

            _messages[message.Id] = message;
            return message.Clone();
        }
    }

    public AgentMessage? GetMessage(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        lock (_gate)
        {
            return _messages.TryGetValue(id.Trim(), out var message)
                ? message.Clone()
                : null;
        }
    }

    public IReadOnlyList<AgentMessage> ListMessages(AgentMessageListOptions? options = null)
    {
        var resolved = options ?? new AgentMessageListOptions();
        if (resolved.Offset < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Offset must be 0 or greater.");
        if (resolved.Limit is <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Limit must be greater than 0.");

        lock (_gate)
        {
            IEnumerable<AgentMessage> query = _messages.Values;

            if (!string.IsNullOrWhiteSpace(resolved.Sender))
            {
                var sender = resolved.Sender.Trim();
                query = query.Where(message =>
                    string.Equals(message.From, sender, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(resolved.Recipient))
            {
                var recipient = resolved.Recipient.Trim();
                query = query.Where(message =>
                    string.Equals(message.To, recipient, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(resolved.ThreadId))
            {
                var threadId = resolved.ThreadId.Trim();
                query = query.Where(message =>
                    string.Equals(message.ThreadId, threadId, StringComparison.OrdinalIgnoreCase));
            }

            if (resolved.Status is { } status)
                query = query.Where(message => message.Status == status);

            query = query
                .OrderByDescending(message => message.CreatedAt)
                .ThenByDescending(message => message.Id, StringComparer.OrdinalIgnoreCase)
                .Skip(resolved.Offset);

            if (resolved.Limit is { } limit)
                query = query.Take(limit);

            return query.Select(message => message.Clone()).ToArray();
        }
    }

    public IReadOnlyList<AgentMessage> ListThread(string threadId)
    {
        if (string.IsNullOrWhiteSpace(threadId))
            return [];

        lock (_gate)
        {
            return _messages.Values
                .Where(message =>
                    string.Equals(message.ThreadId, threadId.Trim(), StringComparison.OrdinalIgnoreCase))
                .OrderBy(message => message.CreatedAt)
                .ThenBy(message => message.Id, StringComparer.OrdinalIgnoreCase)
                .Select(message => message.Clone())
                .ToArray();
        }
    }

    public bool MarkMessageRead(string id)
    {
        return MarkRead(id);
    }

    public bool MarkRead(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return false;

        lock (_gate)
        {
            if (!_messages.TryGetValue(id.Trim(), out var message) ||
                message.Status == AgentMessageStatus.Read)
            {
                return false;
            }

            message.Status = AgentMessageStatus.Read;
            message.UpdatedAt = DateTimeOffset.UtcNow;
            return true;
        }
    }

    public AgentMessageReadResult MarkRecipientMessagesRead(string recipient)
    {
        if (string.IsNullOrWhiteSpace(recipient))
            return new AgentMessageReadResult([]);

        var normalized = recipient.Trim();
        var updated = new List<string>();

        lock (_gate)
        {
            foreach (var message in _messages.Values
                         .Where(message =>
                             message.Status == AgentMessageStatus.Delivered &&
                             string.Equals(message.To, normalized, StringComparison.OrdinalIgnoreCase)))
            {
                message.Status = AgentMessageStatus.Read;
                message.UpdatedAt = DateTimeOffset.UtcNow;
                updated.Add(message.Id);
            }
        }

        return new AgentMessageReadResult(updated);
    }

    public IReadOnlyDictionary<string, int> GetUnreadCounts()
    {
        lock (_gate)
        {
            return _messages.Values
                .Where(message => message.Status == AgentMessageStatus.Delivered)
                .GroupBy(message => message.To, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.Count(),
                    StringComparer.OrdinalIgnoreCase);
        }
    }

    public AgentMessageSummary GetSummary(AgentMessageListOptions? options = null)
    {
        var messages = ListMessages(options);
        var unreadCounts = GetUnreadCounts();

        return new AgentMessageSummary
        {
            TotalCount = messages.Count,
            ReadCount = messages.Count(message => message.Status == AgentMessageStatus.Read),
            UnreadCount = messages.Count(message => message.Status == AgentMessageStatus.Delivered),
            UnreadCounts = unreadCounts,
            RecentMessages = messages.Take(5).Select(message => message.Clone()).ToArray(),
        };
    }

    private static int ParseSequence(string value, string prefix)
    {
        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return 0;

        return int.TryParse(value[prefix.Length..], out var parsed)
            ? parsed
            : 0;
    }
}

namespace ClaudeSharp.Core.Agents;

/// <summary>
/// Defines supported agent message kinds.
/// </summary>
public enum AgentMessageKind
{
    Note,
    PlanApprovalRequest,
    PlanApprovalResponse,
    ShutdownRequest,
    ShutdownResponse,
}

/// <summary>
/// Defines agent message delivery states.
/// </summary>
public enum AgentMessageStatus
{
    Delivered,
    Read,
}

/// <summary>
/// Represents a mailbox message exchanged between agents.
/// </summary>
public sealed class AgentMessage
{
    public required string Id { get; init; }
    public required string From { get; set; }
    public required string To { get; set; }
    public AgentMessageKind Kind { get; set; } = AgentMessageKind.Note;
    public required string Body { get; set; }
    public string? Subject { get; set; }
    public string? RelatedMessageId { get; set; }
    public AgentMessageStatus Status { get; set; } = AgentMessageStatus.Delivered;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public AgentMessage Clone() =>
        new()
        {
            Id = Id,
            From = From,
            To = To,
            Kind = Kind,
            Body = Body,
            Subject = Subject,
            RelatedMessageId = RelatedMessageId,
            Status = Status,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
        };
}

/// <summary>
/// Represents mailbox list options.
/// </summary>
public sealed record AgentMessageListOptions
{
    public string? Sender { get; init; }
    public string? Recipient { get; init; }
    public AgentMessageStatus? Status { get; init; }
    public int Offset { get; init; }
    public int? Limit { get; init; }
}

/// <summary>
/// Represents a batch message read result.
/// </summary>
public sealed record AgentMessageReadResult(
    IReadOnlyList<string> MessageIds)
{
    public int UpdatedCount => MessageIds.Count;
    public bool HasChanges => UpdatedCount > 0;
}

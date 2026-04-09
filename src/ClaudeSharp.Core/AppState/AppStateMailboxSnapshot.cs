namespace ClaudeSharp.Core.AppState;

/// <summary>
/// Represents a mailbox snapshot projected into app state.
/// </summary>
public sealed record AppStateMailboxSnapshot
{
    public required string Participant { get; init; }
    public int InboxCount { get; init; }
    public int UnreadCount { get; init; }
    public int OutboxCount { get; init; }
    public int ThreadCount { get; init; }
    public int PendingActionCount { get; init; }
    public int PendingPlanApprovalCount { get; init; }
    public string? LatestThreadId { get; init; }
    public string? LatestSubject { get; init; }
    public string? LatestCounterparty { get; init; }
    public DateTimeOffset? LatestMessageAt { get; init; }
}

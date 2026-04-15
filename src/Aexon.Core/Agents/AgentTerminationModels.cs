namespace Aexon.Core.Agents;

/// <summary>
/// Classifies how a subagent run ended.
/// </summary>
public enum AgentTerminationKind
{
    Completed = 0,
    Cancelled = 1,
    Failed = 2,
    TimedOut = 3,
}

/// <summary>
/// Identifies who or what initiated the termination.
/// </summary>
public enum AgentTerminationSource
{
    Agent = 0,
    User = 1,
    System = 2,
    Scheduler = 3,
}

/// <summary>
/// Captures the full context of a subagent termination event.
/// </summary>
public sealed record AgentTerminationInfo
{
    public required AgentTerminationKind Kind { get; init; }
    public required AgentTerminationSource Source { get; init; }
    public string? Reason { get; init; }
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;

    public static AgentTerminationInfo Completed(
        string? reason = null,
        AgentTerminationSource source = AgentTerminationSource.Agent) =>
        new()
        {
            Kind = AgentTerminationKind.Completed,
            Source = source,
            Reason = reason,
        };

    public static AgentTerminationInfo Cancelled(
        string? reason = null,
        AgentTerminationSource source = AgentTerminationSource.User) =>
        new()
        {
            Kind = AgentTerminationKind.Cancelled,
            Source = source,
            Reason = reason,
        };

    public static AgentTerminationInfo Failed(
        string? reason = null,
        AgentTerminationSource source = AgentTerminationSource.Agent) =>
        new()
        {
            Kind = AgentTerminationKind.Failed,
            Source = source,
            Reason = reason,
        };

    public static AgentTerminationInfo TimedOut(
        string? reason = null,
        AgentTerminationSource source = AgentTerminationSource.System) =>
        new()
        {
            Kind = AgentTerminationKind.TimedOut,
            Source = source,
            Reason = reason,
        };
}

/// <summary>
/// A persistable termination event that links subagent, background run, and work item.
/// </summary>
public sealed record AgentTerminationEvent
{
    public required string SubagentId { get; init; }
    public required string BackgroundRunId { get; init; }
    public string? WorkItemId { get; init; }
    public required AgentTerminationKind Kind { get; init; }
    public required AgentTerminationSource Source { get; init; }
    public string? Reason { get; init; }
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;

    public static AgentTerminationEvent FromRun(
        AgentBackgroundRun run,
        AgentTerminationInfo termination) =>
        new()
        {
            SubagentId = run.SubagentId ?? run.Id,
            BackgroundRunId = run.Id,
            WorkItemId = run.WorkItemId,
            Kind = termination.Kind,
            Source = termination.Source,
            Reason = termination.Reason,
            OccurredAt = termination.OccurredAt,
        };
}

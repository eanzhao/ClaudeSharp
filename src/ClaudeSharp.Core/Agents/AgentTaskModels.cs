namespace ClaudeSharp.Core.Agents;

/// <summary>
/// Defines agent work item status values.
/// </summary>
public enum AgentWorkItemStatus
{
    Pending,
    InProgress,
    Blocked,
    Completed,
    Cancelled,
}

/// <summary>
/// Defines agent background run status values.
/// </summary>
public enum AgentBackgroundRunStatus
{
    Running = 0,
    CancellationRequested = 1,
    Stopped = 2,
    Failed = 3,
    Cancelled = 4,
    Queued = 5,
}

/// <summary>
/// Represents agent work item.
/// </summary>
public sealed class AgentWorkItem
{
    private readonly List<string> _blocks = [];
    private readonly List<string> _blockedBy = [];

    public required string Id { get; init; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public string? Owner { get; set; }
    public string? SourceKind { get; set; }
    public string? SourceId { get; set; }
    public string? SourceThreadId { get; set; }
    public AgentWorkItemStatus Status { get; set; } = AgentWorkItemStatus.Pending;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public IReadOnlyList<string> Blocks => _blocks;
    public IReadOnlyList<string> BlockedBy => _blockedBy;

    public void AddBlock(string taskId)
    {
        if (!_blocks.Contains(taskId, StringComparer.OrdinalIgnoreCase))
            _blocks.Add(taskId);
    }

    public void AddBlockedBy(string taskId)
    {
        if (!_blockedBy.Contains(taskId, StringComparer.OrdinalIgnoreCase))
            _blockedBy.Add(taskId);
    }

    public AgentWorkItem Clone()
    {
        var clone = new AgentWorkItem
        {
            Id = Id,
            Title = Title,
            Description = Description,
            Owner = Owner,
            SourceKind = SourceKind,
            SourceId = SourceId,
            SourceThreadId = SourceThreadId,
            Status = Status,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
        };

        foreach (var taskId in _blocks)
            clone.AddBlock(taskId);
        foreach (var taskId in _blockedBy)
            clone.AddBlockedBy(taskId);

        return clone;
    }
}

/// <summary>
/// Represents agent background run.
/// </summary>
public sealed class AgentBackgroundRun
{
    private readonly List<string> _output = [];

    public required string Id { get; init; }
    public required string Name { get; set; }
    public string? Owner { get; set; }
    public string? WorkItemId { get; set; }
    public AgentBackgroundRunStatus Status { get; set; } = AgentBackgroundRunStatus.Running;
    public string? StopReason { get; set; }
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StoppedAt { get; set; }

    public IReadOnlyList<string> Output => _output;

    public void AppendOutput(string chunk)
    {
        if (!string.IsNullOrWhiteSpace(chunk))
        {
            _output.Add(chunk);
            UpdatedAt = DateTimeOffset.UtcNow;
        }
    }

    public void Stop(string? reason = null)
    {
        Status = AgentBackgroundRunStatus.Stopped;
        StopReason = string.IsNullOrWhiteSpace(reason) ? StopReason : reason;
        UpdatedAt = DateTimeOffset.UtcNow;
        StoppedAt = DateTimeOffset.UtcNow;
    }

    public void Fail(string? reason = null)
    {
        Status = AgentBackgroundRunStatus.Failed;
        StopReason = string.IsNullOrWhiteSpace(reason) ? StopReason : reason;
        UpdatedAt = DateTimeOffset.UtcNow;
        StoppedAt = DateTimeOffset.UtcNow;
    }

    public void RequestCancellation(string? reason = null)
    {
        if (Status is AgentBackgroundRunStatus.Stopped or
            AgentBackgroundRunStatus.Failed or
            AgentBackgroundRunStatus.Cancelled)
            return;

        Status = AgentBackgroundRunStatus.CancellationRequested;
        StopReason = string.IsNullOrWhiteSpace(reason) ? StopReason : reason;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Cancel(string? reason = null)
    {
        Status = AgentBackgroundRunStatus.Cancelled;
        StopReason = string.IsNullOrWhiteSpace(reason) ? StopReason : reason;
        UpdatedAt = DateTimeOffset.UtcNow;
        StoppedAt = DateTimeOffset.UtcNow;
    }

    public AgentBackgroundRun Clone()
    {
        var clone = new AgentBackgroundRun
        {
            Id = Id,
            Name = Name,
            Owner = Owner,
            WorkItemId = WorkItemId,
            Status = Status,
            StopReason = StopReason,
            StartedAt = StartedAt,
            UpdatedAt = UpdatedAt,
            StoppedAt = StoppedAt,
        };

        foreach (var chunk in _output)
            clone.AppendOutput(chunk);

        clone.UpdatedAt = UpdatedAt;
        clone.StoppedAt = StoppedAt;
        return clone;
    }
}

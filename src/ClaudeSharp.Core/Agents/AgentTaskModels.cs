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
    Running,
    Stopped,
    Failed,
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
        StopReason = reason;
        UpdatedAt = DateTimeOffset.UtcNow;
        StoppedAt = DateTimeOffset.UtcNow;
    }

    public void Fail(string? reason = null)
    {
        Status = AgentBackgroundRunStatus.Failed;
        StopReason = reason;
        UpdatedAt = DateTimeOffset.UtcNow;
        StoppedAt = DateTimeOffset.UtcNow;
    }
}

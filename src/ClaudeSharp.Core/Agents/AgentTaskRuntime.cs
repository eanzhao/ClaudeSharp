namespace ClaudeSharp.Core.Agents;

/// <summary>
/// Defines the contract for agent task runtime.
/// </summary>
public interface IAgentTaskRuntime
{
    AgentWorkItem CreateWorkItem(
        string title,
        string? description = null,
        string? owner = null);

    AgentWorkItem? GetWorkItem(string id);

    IReadOnlyList<AgentWorkItem> ListWorkItems();

    bool UpdateWorkItem(string id, Action<AgentWorkItem> update);

    AgentBackgroundRun StartBackgroundRun(
        string name,
        string? owner = null);

    AgentBackgroundRun? GetBackgroundRun(string id);

    IReadOnlyList<AgentBackgroundRun> ListBackgroundRuns();

    bool UpdateBackgroundRun(string id, Action<AgentBackgroundRun> update);

    bool AppendBackgroundRunOutput(string id, string chunk);

    bool StopBackgroundRun(string id, string? reason = null);

    bool FailBackgroundRun(string id, string? reason = null);
}

/// <summary>
/// Provides in memory agent task runtime.
/// </summary>
public sealed class InMemoryAgentTaskRuntime : IAgentTaskRuntime
{
    private readonly object _gate = new();
    private readonly Dictionary<string, AgentWorkItem> _workItems =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AgentBackgroundRun> _backgroundRuns =
        new(StringComparer.OrdinalIgnoreCase);
    private int _workItemSequence;
    private int _backgroundRunSequence;

    public AgentWorkItem CreateWorkItem(
        string title,
        string? description = null,
        string? owner = null)
    {
        var item = new AgentWorkItem
        {
            Id = $"work-item-{Interlocked.Increment(ref _workItemSequence)}",
            Title = title.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            Owner = string.IsNullOrWhiteSpace(owner) ? null : owner.Trim(),
        };

        lock (_gate)
            _workItems[item.Id] = item;
        return item;
    }

    public AgentWorkItem? GetWorkItem(string id)
    {
        lock (_gate)
            return _workItems.TryGetValue(id, out var item) ? item : null;
    }

    public IReadOnlyList<AgentWorkItem> ListWorkItems() =>
        SnapshotWorkItems()
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public bool UpdateWorkItem(string id, Action<AgentWorkItem> update)
    {
        lock (_gate)
        {
            if (!_workItems.TryGetValue(id, out var item))
                return false;

            update(item);
            item.UpdatedAt = DateTimeOffset.UtcNow;
            return true;
        }
    }

    public AgentBackgroundRun StartBackgroundRun(
        string name,
        string? owner = null)
    {
        var run = new AgentBackgroundRun
        {
            Id = $"background-run-{Interlocked.Increment(ref _backgroundRunSequence)}",
            Name = name.Trim(),
            Owner = string.IsNullOrWhiteSpace(owner) ? null : owner.Trim(),
        };

        lock (_gate)
            _backgroundRuns[run.Id] = run;
        return run;
    }

    public AgentBackgroundRun? GetBackgroundRun(string id)
    {
        lock (_gate)
            return _backgroundRuns.TryGetValue(id, out var run) ? run : null;
    }

    public IReadOnlyList<AgentBackgroundRun> ListBackgroundRuns() =>
        SnapshotBackgroundRuns()
            .OrderBy(run => run.StartedAt)
            .ThenBy(run => run.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public bool UpdateBackgroundRun(string id, Action<AgentBackgroundRun> update)
    {
        lock (_gate)
        {
            if (!_backgroundRuns.TryGetValue(id, out var run))
                return false;

            update(run);
            run.UpdatedAt = DateTimeOffset.UtcNow;
            return true;
        }
    }

    public bool AppendBackgroundRunOutput(string id, string chunk)
    {
        lock (_gate)
        {
            if (!_backgroundRuns.TryGetValue(id, out var run))
                return false;

            run.AppendOutput(chunk);
            return true;
        }
    }

    public bool StopBackgroundRun(string id, string? reason = null)
    {
        lock (_gate)
        {
            if (!_backgroundRuns.TryGetValue(id, out var run))
                return false;

            run.Stop(reason);
            return true;
        }
    }

    public bool FailBackgroundRun(string id, string? reason = null)
    {
        lock (_gate)
        {
            if (!_backgroundRuns.TryGetValue(id, out var run))
                return false;

            run.Fail(reason);
            return true;
        }
    }

    private IReadOnlyList<AgentWorkItem> SnapshotWorkItems()
    {
        lock (_gate)
            return _workItems.Values.ToArray();
    }

    private IReadOnlyList<AgentBackgroundRun> SnapshotBackgroundRuns()
    {
        lock (_gate)
            return _backgroundRuns.Values.ToArray();
    }
}

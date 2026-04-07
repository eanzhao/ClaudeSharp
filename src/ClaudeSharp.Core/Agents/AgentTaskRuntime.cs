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

    bool AppendBackgroundRunOutput(string id, string chunk);

    bool StopBackgroundRun(string id, string? reason = null);
}

/// <summary>
/// Provides in memory agent task runtime.
/// </summary>
public sealed class InMemoryAgentTaskRuntime : IAgentTaskRuntime
{
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

        _workItems[item.Id] = item;
        return item;
    }

    public AgentWorkItem? GetWorkItem(string id) =>
        _workItems.TryGetValue(id, out var item) ? item : null;

    public IReadOnlyList<AgentWorkItem> ListWorkItems() =>
        _workItems.Values
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public bool UpdateWorkItem(string id, Action<AgentWorkItem> update)
    {
        if (!_workItems.TryGetValue(id, out var item))
            return false;

        update(item);
        item.UpdatedAt = DateTimeOffset.UtcNow;
        return true;
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

        _backgroundRuns[run.Id] = run;
        return run;
    }

    public AgentBackgroundRun? GetBackgroundRun(string id) =>
        _backgroundRuns.TryGetValue(id, out var run) ? run : null;

    public IReadOnlyList<AgentBackgroundRun> ListBackgroundRuns() =>
        _backgroundRuns.Values
            .OrderBy(run => run.StartedAt)
            .ThenBy(run => run.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public bool AppendBackgroundRunOutput(string id, string chunk)
    {
        if (!_backgroundRuns.TryGetValue(id, out var run))
            return false;

        run.AppendOutput(chunk);
        return true;
    }

    public bool StopBackgroundRun(string id, string? reason = null)
    {
        if (!_backgroundRuns.TryGetValue(id, out var run))
            return false;

        run.Stop(reason);
        return true;
    }
}

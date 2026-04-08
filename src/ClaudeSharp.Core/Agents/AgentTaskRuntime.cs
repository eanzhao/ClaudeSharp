namespace ClaudeSharp.Core.Agents;

/// <summary>
/// Defines background run cancellation outcomes.
/// </summary>
public enum AgentBackgroundRunCancellationResult
{
    Requested,
    AlreadyRequested,
    AlreadyCompleted,
    NotFound,
    Unsupported,
}

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
        string? owner = null,
        string? workItemId = null,
        AgentBackgroundRunStatus initialStatus = AgentBackgroundRunStatus.Running);

    AgentBackgroundRun? GetBackgroundRun(string id);

    IReadOnlyList<AgentBackgroundRun> ListBackgroundRuns();

    bool UpdateBackgroundRun(string id, Action<AgentBackgroundRun> update);

    bool AppendBackgroundRunOutput(string id, string chunk);

    bool RegisterBackgroundRunCancellation(string id, Action cancel);

    AgentBackgroundRunCancellationResult RequestBackgroundRunCancellation(
        string id,
        string? reason = null);

    bool StopBackgroundRun(string id, string? reason = null);

    bool FailBackgroundRun(string id, string? reason = null);

    bool CancelBackgroundRun(string id, string? reason = null);
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
    private readonly Dictionary<string, Action> _backgroundRunCancellers =
        new(StringComparer.OrdinalIgnoreCase);
    private int _workItemSequence;
    private int _backgroundRunSequence;

    public InMemoryAgentTaskRuntime(
        IEnumerable<AgentWorkItem>? workItems = null,
        IEnumerable<AgentBackgroundRun>? backgroundRuns = null)
    {
        if (workItems != null)
        {
            foreach (var item in workItems)
            {
                _workItems[item.Id] = item.Clone();
                _workItemSequence = Math.Max(_workItemSequence, ParseSequence(item.Id, "work-item-"));
            }
        }

        if (backgroundRuns != null)
        {
            foreach (var run in backgroundRuns)
            {
                _backgroundRuns[run.Id] = run.Clone();
                _backgroundRunSequence = Math.Max(_backgroundRunSequence, ParseSequence(run.Id, "background-run-"));
            }
        }
    }

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
        string? owner = null,
        string? workItemId = null,
        AgentBackgroundRunStatus initialStatus = AgentBackgroundRunStatus.Running)
    {
        var run = new AgentBackgroundRun
        {
            Id = $"background-run-{Interlocked.Increment(ref _backgroundRunSequence)}",
            Name = name.Trim(),
            Owner = string.IsNullOrWhiteSpace(owner) ? null : owner.Trim(),
            WorkItemId = string.IsNullOrWhiteSpace(workItemId) ? null : workItemId.Trim(),
            Status = initialStatus,
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

    public bool RegisterBackgroundRunCancellation(string id, Action cancel)
    {
        lock (_gate)
        {
            if (!_backgroundRuns.ContainsKey(id))
                return false;

            _backgroundRunCancellers[id] = cancel;
            return true;
        }
    }

    public AgentBackgroundRunCancellationResult RequestBackgroundRunCancellation(
        string id,
        string? reason = null)
    {
        Action? cancel = null;

        lock (_gate)
        {
            if (!_backgroundRuns.TryGetValue(id, out var run))
                return AgentBackgroundRunCancellationResult.NotFound;

            if (run.Status is AgentBackgroundRunStatus.Stopped or
                AgentBackgroundRunStatus.Failed or
                AgentBackgroundRunStatus.Cancelled)
            {
                return AgentBackgroundRunCancellationResult.AlreadyCompleted;
            }

            if (run.Status == AgentBackgroundRunStatus.CancellationRequested)
                return AgentBackgroundRunCancellationResult.AlreadyRequested;

            if (!_backgroundRunCancellers.TryGetValue(id, out cancel))
                return AgentBackgroundRunCancellationResult.Unsupported;

            run.RequestCancellation(reason);
        }

        try
        {
            cancel();
        }
        catch (ObjectDisposedException)
        {
            // The background run finished while the cancellation request was in flight.
        }

        return AgentBackgroundRunCancellationResult.Requested;
    }

    public bool StopBackgroundRun(string id, string? reason = null)
    {
        lock (_gate)
        {
            if (!_backgroundRuns.TryGetValue(id, out var run))
                return false;

            run.Stop(reason);
            _backgroundRunCancellers.Remove(id);
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
            _backgroundRunCancellers.Remove(id);
            return true;
        }
    }

    public bool CancelBackgroundRun(string id, string? reason = null)
    {
        lock (_gate)
        {
            if (!_backgroundRuns.TryGetValue(id, out var run))
                return false;

            run.Cancel(reason);
            _backgroundRunCancellers.Remove(id);
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

    private static int ParseSequence(string id, string prefix)
    {
        if (!id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return 0;

        return int.TryParse(id[prefix.Length..], out var parsed)
            ? parsed
            : 0;
    }
}

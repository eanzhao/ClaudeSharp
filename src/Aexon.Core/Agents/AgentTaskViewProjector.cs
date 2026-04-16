namespace Aexon.Core.Agents;

public enum AgentTaskViewStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled,
}

public sealed record AgentTaskView(
    AgentWorkItem WorkItem,
    AgentTaskViewStatus Status,
    AgentBackgroundRun? ActiveRun,
    AgentBackgroundRun? LatestRun,
    IReadOnlyList<AgentBackgroundRun> Runs);

public static class AgentTaskViewProjector
{
    public static AgentTaskView Project(
        IAgentTaskRuntime runtime,
        AgentWorkItem workItem)
    {
        var runs = runtime.ListBackgroundRuns()
            .Where(run => string.Equals(run.WorkItemId, workItem.Id, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(run => run.StartedAt)
            .ThenBy(run => run.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var activeRun = runs.FirstOrDefault(run => !AgentBackgroundRunWaiter.IsTerminal(run.Status));
        var latestRun = runs.FirstOrDefault();

        return new AgentTaskView(
            workItem.Clone(),
            ComputeStatus(workItem, activeRun, latestRun),
            activeRun?.Clone(),
            latestRun?.Clone(),
            runs.Select(run => run.Clone()).ToArray());
    }

    public static AgentTaskViewStatus ComputeStatus(
        AgentWorkItem workItem,
        AgentBackgroundRun? activeRun,
        AgentBackgroundRun? latestRun)
    {
        if (activeRun != null)
            return AgentTaskViewStatus.Running;

        if (latestRun?.Status == AgentBackgroundRunStatus.Cancelled ||
            workItem.Status == AgentWorkItemStatus.Cancelled)
        {
            return AgentTaskViewStatus.Cancelled;
        }

        if (latestRun?.Status == AgentBackgroundRunStatus.Failed ||
            workItem.Status == AgentWorkItemStatus.Blocked)
        {
            return AgentTaskViewStatus.Failed;
        }

        if (latestRun?.Status == AgentBackgroundRunStatus.Stopped ||
            workItem.Status == AgentWorkItemStatus.Completed)
        {
            return AgentTaskViewStatus.Completed;
        }

        return workItem.Status switch
        {
            AgentWorkItemStatus.Pending => AgentTaskViewStatus.Pending,
            AgentWorkItemStatus.Cancelled => AgentTaskViewStatus.Cancelled,
            AgentWorkItemStatus.Completed => AgentTaskViewStatus.Completed,
            AgentWorkItemStatus.Blocked => AgentTaskViewStatus.Failed,
            _ => AgentTaskViewStatus.Running,
        };
    }

    public static AgentWorkItemStatus ToWorkItemStatus(AgentTaskViewStatus status) =>
        status switch
        {
            AgentTaskViewStatus.Pending => AgentWorkItemStatus.Pending,
            AgentTaskViewStatus.Running => AgentWorkItemStatus.InProgress,
            AgentTaskViewStatus.Completed => AgentWorkItemStatus.Completed,
            AgentTaskViewStatus.Failed => AgentWorkItemStatus.Blocked,
            AgentTaskViewStatus.Cancelled => AgentWorkItemStatus.Cancelled,
            _ => AgentWorkItemStatus.Pending,
        };

    public static bool TryParseStatus(
        string? value,
        out AgentTaskViewStatus status)
    {
        status = AgentTaskViewStatus.Pending;
        if (string.IsNullOrWhiteSpace(value))
            return true;

        return Normalize(value) switch
        {
            "pending" => true,
            "running" or "inprogress" => (status = AgentTaskViewStatus.Running) == AgentTaskViewStatus.Running,
            "completed" or "complete" => (status = AgentTaskViewStatus.Completed) == AgentTaskViewStatus.Completed,
            "failed" or "blocked" => (status = AgentTaskViewStatus.Failed) == AgentTaskViewStatus.Failed,
            "cancelled" or "canceled" => (status = AgentTaskViewStatus.Cancelled) == AgentTaskViewStatus.Cancelled,
            _ => false,
        };
    }

    public static string FormatStatus(AgentTaskViewStatus status) =>
        status.ToString().ToLowerInvariant();

    private static string Normalize(string value) =>
        new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
}

namespace Aexon.Core.Agents;

public sealed record AgentBackgroundRunSelection(
    AgentWorkItem? WorkItem,
    AgentBackgroundRun Run,
    IReadOnlyList<AgentBackgroundRun> RelatedRuns,
    string SelectionReason);

public static class AgentBackgroundRunSelector
{
    public static bool TrySelect(
        IAgentTaskRuntime runtime,
        string? taskId,
        string? backgroundRunId,
        out AgentBackgroundRunSelection? selection,
        out string? error)
    {
        selection = null;
        error = null;

        if (!string.IsNullOrWhiteSpace(backgroundRunId))
        {
            var run = runtime.GetBackgroundRun(backgroundRunId.Trim());
            if (run == null)
            {
                error = $"No background run matched id '{backgroundRunId.Trim()}'.";
                return false;
            }

            AgentWorkItem? workItem = null;
            if (!string.IsNullOrWhiteSpace(taskId))
            {
                workItem = runtime.GetWorkItem(taskId.Trim());
                if (workItem == null)
                {
                    error = $"No task matched id '{taskId.Trim()}'.";
                    return false;
                }

                if (!string.Equals(run.WorkItemId, workItem.Id, StringComparison.OrdinalIgnoreCase))
                {
                    error = $"Background run '{run.Id}' does not belong to task '{workItem.Id}'.";
                    return false;
                }
            }
            else if (!string.IsNullOrWhiteSpace(run.WorkItemId))
            {
                workItem = runtime.GetWorkItem(run.WorkItemId);
            }

            selection = new AgentBackgroundRunSelection(
                workItem?.Clone(),
                run.Clone(),
                [run.Clone()],
                "explicit run id");
            return true;
        }

        if (string.IsNullOrWhiteSpace(taskId))
        {
            error = "task_id or process_id is required.";
            return false;
        }

        var normalizedTaskId = taskId.Trim();
        var task = runtime.GetWorkItem(normalizedTaskId);
        if (task == null)
        {
            error = $"No task matched id '{normalizedTaskId}'.";
            return false;
        }

        var runs = runtime.ListBackgroundRuns()
            .Where(run => string.Equals(run.WorkItemId, task.Id, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(run => !AgentBackgroundRunWaiter.IsTerminal(run.Status))
            .ThenByDescending(run => run.StartedAt)
            .ThenBy(run => run.Id, StringComparer.OrdinalIgnoreCase)
            .Select(run => run.Clone())
            .ToArray();
        if (runs.Length == 0)
        {
            error = $"Task '{task.Id}' does not have any associated background runs.";
            return false;
        }

        var selectedRun = runs[0];
        var selectionReason = AgentBackgroundRunWaiter.IsTerminal(selectedRun.Status)
            ? "latest run"
            : "latest active run";
        selection = new AgentBackgroundRunSelection(
            task.Clone(),
            selectedRun,
            runs,
            selectionReason);
        return true;
    }
}

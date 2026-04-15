namespace Aexon.Core.Agents;

/// <summary>
/// Automatically resumes approved work items when their owner becomes idle.
/// </summary>
public static class AgentAutoResumePolicy
{
    public static async Task<IReadOnlyList<AgentWorkItemResumeResult>> TryResumeEligibleAsync(
        IAgentTaskRuntime taskRuntime,
        IAgentMessageRuntime messageRuntime,
        IAgentMessageActivationRuntime activationRuntime,
        AgentAutoResumeMode mode = AgentAutoResumeMode.Queue,
        string? owner = null,
        int? limit = 1,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(taskRuntime);
        ArgumentNullException.ThrowIfNull(messageRuntime);
        ArgumentNullException.ThrowIfNull(activationRuntime);

        if (mode == AgentAutoResumeMode.Disabled)
            return [];

        var normalizedOwner = string.IsNullOrWhiteSpace(owner) ? null : owner.Trim();
        if (normalizedOwner != null && HasActiveRun(taskRuntime, normalizedOwner))
            return [];

        IEnumerable<AgentWorkItem> query = taskRuntime.ListWorkItems()
            .Where(item => item.Status == AgentWorkItemStatus.AwaitingResume)
            .Where(item =>
                normalizedOwner == null ||
                string.Equals(item.Owner, normalizedOwner, StringComparison.OrdinalIgnoreCase));

        query = mode switch
        {
            AgentAutoResumeMode.Latest => query
                .OrderByDescending(item => item.UpdatedAt)
                .ThenByDescending(item => item.Id, StringComparer.OrdinalIgnoreCase),
            _ => query
                .OrderBy(item => item.UpdatedAt)
                .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase),
        };

        var candidates = query.ToArray();

        var results = new List<AgentWorkItemResumeResult>();
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate.Owner) &&
                HasActiveRun(taskRuntime, candidate.Owner))
            {
                continue;
            }

            var result = await AgentWorkItemResumer.TryResumeAsync(
                taskRuntime,
                messageRuntime,
                activationRuntime,
                candidate.Id,
                cancellationToken);
            results.Add(result);

            if (limit is > 0 && results.Count >= limit.Value)
                break;

            if (!string.IsNullOrWhiteSpace(candidate.Owner) &&
                HasActiveRun(taskRuntime, candidate.Owner))
            {
                break;
            }

            if (mode == AgentAutoResumeMode.Latest)
                break;
        }

        return results;
    }

    private static bool HasActiveRun(IAgentTaskRuntime taskRuntime, string owner)
    {
        return taskRuntime.ListBackgroundRuns().Any(run =>
            string.Equals(run.Owner, owner, StringComparison.OrdinalIgnoreCase) &&
            (run.Status is AgentBackgroundRunStatus.Queued or
                AgentBackgroundRunStatus.Running or
                AgentBackgroundRunStatus.CancellationRequested));
    }
}

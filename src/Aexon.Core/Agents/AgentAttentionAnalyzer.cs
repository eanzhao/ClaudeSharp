namespace Aexon.Core.Agents;

/// <summary>
/// Represents an attention-worthy agent work item.
/// </summary>
public sealed record AgentAttentionItem
{
    public required AgentWorkItem WorkItem { get; init; }
    public required string Summary { get; init; }
    public required string NextAction { get; init; }
    public string? ActiveBackgroundRunId { get; init; }
}

/// <summary>
/// Provides structured analysis for work items that need human attention or resume actions.
/// </summary>
public static class AgentAttentionAnalyzer
{
    public static IReadOnlyList<AgentAttentionItem> ListAttentionItems(
        IAgentTaskRuntime runtime,
        string? owner = null,
        int? limit = null)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        var normalizedOwner = string.IsNullOrWhiteSpace(owner) ? null : owner.Trim();
        var activeRunsByOwner = runtime.ListBackgroundRuns()
            .Where(run => run.Status is AgentBackgroundRunStatus.Queued or
                AgentBackgroundRunStatus.Running or
                AgentBackgroundRunStatus.CancellationRequested)
            .Where(run => !string.IsNullOrWhiteSpace(run.Owner))
            .GroupBy(run => run.Owner!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(run => run.UpdatedAt)
                    .ThenBy(run => run.Id, StringComparer.OrdinalIgnoreCase)
                    .First(),
                StringComparer.OrdinalIgnoreCase);

        var items = runtime.ListWorkItems()
            .Where(item => item.Status is AgentWorkItemStatus.AwaitingApproval or AgentWorkItemStatus.AwaitingResume)
            .Where(item =>
                normalizedOwner == null ||
                string.Equals(item.Owner, normalizedOwner, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.UpdatedAt)
            .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .Select(item => CreateAttentionItem(item, activeRunsByOwner))
            .ToArray();

        return limit is > 0
            ? items.Take(limit.Value).ToArray()
            : items;
    }

    public static string? DescribeNextAction(
        AgentWorkItem item,
        AgentBackgroundRun? activeOwnerRun = null)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item.Status switch
        {
            AgentWorkItemStatus.AwaitingApproval =>
                string.IsNullOrWhiteSpace(item.ApprovalRequestId)
                    ? "Resolve the outstanding approval request."
                    : $"Run /mailbox respond {item.ApprovalRequestId} approve|reject to resolve the approval.",
            AgentWorkItemStatus.AwaitingResume when activeOwnerRun != null =>
                $"Wait for {activeOwnerRun.Owner} to finish {activeOwnerRun.Id}, or inspect that run before resuming this work item.",
            AgentWorkItemStatus.AwaitingResume =>
                $"Run /agents resume {item.Id} to continue the approved work item.",
            _ => null,
        };
    }

    public static string? DescribeSummary(
        AgentWorkItem item,
        AgentBackgroundRun? activeOwnerRun = null)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item.Status switch
        {
            AgentWorkItemStatus.AwaitingApproval => "Waiting for approval response.",
            AgentWorkItemStatus.AwaitingResume when activeOwnerRun != null =>
                $"Approved, but {activeOwnerRun.Owner} is already busy with {activeOwnerRun.Id}.",
            AgentWorkItemStatus.AwaitingResume => "Approved and ready to resume.",
            _ => null,
        };
    }

    private static AgentAttentionItem CreateAttentionItem(
        AgentWorkItem item,
        IReadOnlyDictionary<string, AgentBackgroundRun> activeRunsByOwner)
    {
        activeRunsByOwner.TryGetValue(item.Owner ?? string.Empty, out var activeOwnerRun);
        return new AgentAttentionItem
        {
            WorkItem = item,
            Summary = DescribeSummary(item, activeOwnerRun) ?? item.Status.ToString(),
            NextAction = DescribeNextAction(item, activeOwnerRun) ?? "Inspect the work item.",
            ActiveBackgroundRunId = activeOwnerRun?.Id,
        };
    }
}

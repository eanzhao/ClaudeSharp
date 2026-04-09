using System.Text;

namespace ClaudeSharp.Core.Agents;

/// <summary>
/// Defines known source kinds for work items projected from other runtimes.
/// </summary>
public static class AgentWorkItemSourceKinds
{
    public const string MailboxPlanApproval = "mailbox-plan-approval";
}

/// <summary>
/// Represents the result of synchronizing mailbox workflows into agent work items.
/// </summary>
public sealed record AgentMailboxTaskProjectionResult(
    IReadOnlyList<string> CreatedWorkItemIds,
    IReadOnlyList<string> UpdatedWorkItemIds)
{
    public int CreatedCount => CreatedWorkItemIds.Count;
    public int UpdatedCount => UpdatedWorkItemIds.Count;
    public bool HasChanges => CreatedCount > 0 || UpdatedCount > 0;
}

/// <summary>
/// Projects mailbox action workflows into task-runtime todos.
/// </summary>
public static class AgentMailboxTaskProjector
{
    public static AgentMailboxTaskProjectionResult Synchronize(
        IAgentMessageRuntime messageRuntime,
        IAgentTaskRuntime taskRuntime)
    {
        ArgumentNullException.ThrowIfNull(messageRuntime);
        ArgumentNullException.ThrowIfNull(taskRuntime);

        var created = new List<string>();
        var updated = new List<string>();
        var existing = taskRuntime.ListWorkItems()
            .Where(item =>
                string.Equals(
                    item.SourceKind,
                    AgentWorkItemSourceKinds.MailboxPlanApproval,
                    StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(item.SourceId))
            .GroupBy(item => item.SourceId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var requests = messageRuntime.ListMessages()
            .Where(message => message.Kind == AgentMessageKind.PlanApprovalRequest)
            .OrderBy(message => message.CreatedAt)
            .ThenBy(message => message.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var request in requests)
        {
            var item = AgentMessageWorkflow.DescribeAction(messageRuntime, request);
            if (item?.ActionType != AgentMessageActionType.PlanApproval)
                continue;

            var title = BuildTitle(item.TriggerMessage);
            var description = BuildDescription(item);
            var owner = item.TriggerMessage.To;
            var status = ResolveStatus(item);

            if (!existing.TryGetValue(item.TriggerMessage.Id, out var workItem))
            {
                if (item.IsResolved)
                    continue;

                workItem = taskRuntime.CreateWorkItem(title, description, owner);
                created.Add(workItem.Id);
                existing[item.TriggerMessage.Id] = workItem;
            }

            if (!NeedsUpdate(workItem, title, description, owner, status, item.TriggerMessage))
                continue;

            if (taskRuntime.UpdateWorkItem(workItem.Id, stored =>
                ApplyProjectedState(stored, title, description, owner, status, item.TriggerMessage)))
            {
                updated.Add(workItem.Id);
            }
        }

        return new AgentMailboxTaskProjectionResult(created, updated);
    }

    private static bool NeedsUpdate(
        AgentWorkItem workItem,
        string title,
        string description,
        string owner,
        AgentWorkItemStatus status,
        AgentMessage request)
    {
        return !string.Equals(workItem.Title, title, StringComparison.Ordinal) ||
               !string.Equals(workItem.Description, description, StringComparison.Ordinal) ||
               !string.Equals(workItem.Owner, owner, StringComparison.Ordinal) ||
               !string.Equals(workItem.SourceKind, AgentWorkItemSourceKinds.MailboxPlanApproval, StringComparison.Ordinal) ||
               !string.Equals(workItem.SourceId, request.Id, StringComparison.Ordinal) ||
               !string.Equals(workItem.SourceThreadId, request.ThreadId, StringComparison.Ordinal) ||
               workItem.Status != status;
    }

    private static void ApplyProjectedState(
        AgentWorkItem workItem,
        string title,
        string description,
        string owner,
        AgentWorkItemStatus status,
        AgentMessage request)
    {
        workItem.Title = title;
        workItem.Description = description;
        workItem.Owner = owner;
        workItem.SourceKind = AgentWorkItemSourceKinds.MailboxPlanApproval;
        workItem.SourceId = request.Id;
        workItem.SourceThreadId = request.ThreadId;
        workItem.Status = status;
    }

    private static AgentWorkItemStatus ResolveStatus(AgentMessageActionItem item)
    {
        if (item.ResolutionMessage == null)
            return AgentWorkItemStatus.Pending;

        return item.ResolutionMessage.Protocol?.ActionName switch
        {
            "plan-approval-rejected" => AgentWorkItemStatus.Cancelled,
            _ => AgentWorkItemStatus.Completed,
        };
    }

    private static string BuildTitle(AgentMessage request)
    {
        var summary = string.IsNullOrWhiteSpace(request.Subject)
            ? request.Body
            : request.Subject!;
        return $"Approval: {Truncate(summary, 72)}";
    }

    private static string BuildDescription(AgentMessageActionItem item)
    {
        var builder = new StringBuilder();
        builder.Append($"Requested by {item.TriggerMessage.From} for {item.TriggerMessage.To}.");
        builder.Append($" Thread: {item.TriggerMessage.ThreadId}.");
        builder.Append($" Message: {item.TriggerMessage.Id}.");

        if (item.ResolutionMessage != null)
        {
            builder.Append($" Decision: {DescribeResolution(item.ResolutionMessage)}.");
            builder.Append($" Resolved by {item.ResolutionMessage.From}.");
        }
        else
        {
            builder.Append(" Waiting for approval response.");
        }

        return builder.ToString();
    }

    private static string DescribeResolution(AgentMessage resolution)
    {
        return resolution.Protocol?.ActionName switch
        {
            "plan-approval-approved" => "approved",
            "plan-approval-rejected" => "rejected",
            _ => "responded",
        };
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Untitled approval";

        var normalized = new string(value
            .Trim()
            .ReplaceLineEndings(" ")
            .Where(character => !char.IsControl(character))
            .ToArray());

        if (normalized.Length <= maxLength)
            return normalized;

        return $"{normalized[..Math.Max(0, maxLength - 3)].TrimEnd()}...";
    }
}

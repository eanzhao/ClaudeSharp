namespace Aexon.Core.Agents;

/// <summary>
/// Coordinates approval-related state transitions for agent work items.
/// </summary>
public static class AgentWorkItemApprovalCoordinator
{
    public static AgentWorkItem? FindTrackedWorkItem(
        IAgentTaskRuntime runtime,
        AgentMessage requestMessage)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(requestMessage);

        return runtime.ListWorkItems()
            .FirstOrDefault(item =>
                string.Equals(item.ApprovalRequestId, requestMessage.Id, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(item.ApprovalThreadId) &&
                 string.Equals(item.ApprovalThreadId, requestMessage.ThreadId, StringComparison.OrdinalIgnoreCase) &&
                 item.Status is AgentWorkItemStatus.AwaitingApproval or AgentWorkItemStatus.AwaitingResume));
    }

    public static bool TryMarkAwaitingApproval(
        IAgentTaskRuntime runtime,
        string? workItemId,
        AgentMessage requestMessage)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(requestMessage);

        if (string.IsNullOrWhiteSpace(workItemId) ||
            requestMessage.Kind != AgentMessageKind.PlanApprovalRequest)
        {
            return false;
        }

        return runtime.UpdateWorkItem(workItemId, item =>
        {
            item.Status = AgentWorkItemStatus.AwaitingApproval;
            item.ApprovalRequestId = requestMessage.Id;
            item.ApprovalThreadId = requestMessage.ThreadId;
        });
    }

    public static bool TryApplyApprovalResponse(
        IAgentTaskRuntime runtime,
        AgentMessage requestMessage,
        AgentMessage responseMessage)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(requestMessage);
        ArgumentNullException.ThrowIfNull(responseMessage);

        if (requestMessage.Kind != AgentMessageKind.PlanApprovalRequest ||
            responseMessage.Kind != AgentMessageKind.PlanApprovalResponse)
        {
            return false;
        }

        var workItem = FindTrackedWorkItem(runtime, requestMessage);
        if (workItem == null)
            return false;

        return runtime.UpdateWorkItem(workItem.Id, item =>
        {
            item.ApprovalRequestId ??= requestMessage.Id;
            item.ApprovalThreadId ??= requestMessage.ThreadId;

            if (string.Equals(
                    responseMessage.Protocol?.ActionName,
                    "plan-approval-rejected",
                    StringComparison.OrdinalIgnoreCase))
            {
                item.Status = AgentWorkItemStatus.Blocked;
                ClearApprovalState(item);
                return;
            }

            item.Status = AgentWorkItemStatus.AwaitingResume;
        });
    }

    public static bool TryResumeApprovedWorkItem(
        IAgentTaskRuntime runtime,
        string? workItemId)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        if (string.IsNullOrWhiteSpace(workItemId))
            return false;

        return runtime.UpdateWorkItem(workItemId, item =>
        {
            item.Status = AgentWorkItemStatus.InProgress;
            ClearApprovalState(item);
        });
    }

    public static bool TryCompleteSuccessfulRun(
        IAgentTaskRuntime runtime,
        string? workItemId)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        if (string.IsNullOrWhiteSpace(workItemId))
            return false;

        return runtime.UpdateWorkItem(workItemId, item =>
        {
            if (item.Status is AgentWorkItemStatus.AwaitingApproval or AgentWorkItemStatus.AwaitingResume)
                return;

            if (item.Status is AgentWorkItemStatus.Pending or AgentWorkItemStatus.InProgress)
            {
                item.Status = AgentWorkItemStatus.Completed;
                ClearApprovalState(item);
            }
        });
    }

    public static void ClearApprovalState(AgentWorkItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        item.ApprovalRequestId = null;
        item.ApprovalThreadId = null;
    }

    public static string? DescribeApprovalState(AgentWorkItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item.Status switch
        {
            AgentWorkItemStatus.AwaitingApproval => "Waiting for approval",
            AgentWorkItemStatus.AwaitingResume => "Approved, waiting to resume",
            _ => null,
        };
    }
}

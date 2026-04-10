namespace ClaudeSharp.Core.Agents;

/// <summary>
/// Defines outcomes for manually resuming an approved work item.
/// </summary>
public enum AgentWorkItemResumeStatus
{
    Resumed,
    AlreadyActive,
    NotFound,
    InvalidState,
    MissingApprovalRequest,
    ApprovalNotReady,
    ActivationNotRegistered,
    Failed,
}

/// <summary>
/// Represents the outcome of attempting to resume an approved work item.
/// </summary>
public sealed record AgentWorkItemResumeResult
{
    public required string WorkItemId { get; init; }
    public AgentWorkItemResumeStatus Status { get; init; }
    public AgentWorkItemStatus? CurrentStatus { get; init; }
    public string? Owner { get; init; }
    public string? ApprovalRequestId { get; init; }
    public string? ApprovalResponseId { get; init; }
    public string? ThreadId { get; init; }
    public AgentMessageActivationResult? Activation { get; init; }
    public string? Message { get; init; }
}

/// <summary>
/// Replays an approved mailbox response so the original agent work item can continue.
/// </summary>
public static class AgentWorkItemResumer
{
    public static async Task<AgentWorkItemResumeResult> TryResumeAsync(
        IAgentTaskRuntime taskRuntime,
        IAgentMessageRuntime messageRuntime,
        IAgentMessageActivationRuntime activationRuntime,
        string workItemId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(taskRuntime);
        ArgumentNullException.ThrowIfNull(messageRuntime);
        ArgumentNullException.ThrowIfNull(activationRuntime);

        var normalizedId = string.IsNullOrWhiteSpace(workItemId) ? string.Empty : workItemId.Trim();
        var workItem = string.IsNullOrWhiteSpace(normalizedId)
            ? null
            : taskRuntime.GetWorkItem(normalizedId);
        if (workItem == null)
        {
            return new AgentWorkItemResumeResult
            {
                WorkItemId = normalizedId,
                Status = AgentWorkItemResumeStatus.NotFound,
                Message = $"No work item matched id '{normalizedId}'.",
            };
        }

        if (workItem.Status != AgentWorkItemStatus.AwaitingResume)
        {
            return new AgentWorkItemResumeResult
            {
                WorkItemId = workItem.Id,
                Status = AgentWorkItemResumeStatus.InvalidState,
                CurrentStatus = workItem.Status,
                Owner = workItem.Owner,
                ApprovalRequestId = workItem.ApprovalRequestId,
                ThreadId = workItem.ApprovalThreadId,
                Message = $"{workItem.Id} is not waiting to resume. Current status: {workItem.Status}.",
            };
        }

        var requestMessage = ResolveApprovalRequest(messageRuntime, workItem);
        if (requestMessage == null)
        {
            return new AgentWorkItemResumeResult
            {
                WorkItemId = workItem.Id,
                Status = AgentWorkItemResumeStatus.MissingApprovalRequest,
                CurrentStatus = workItem.Status,
                Owner = workItem.Owner,
                ApprovalRequestId = workItem.ApprovalRequestId,
                ThreadId = workItem.ApprovalThreadId,
                Message = $"{workItem.Id} no longer has a linked approval request.",
            };
        }

        var responseMessage = ResolveApprovalResponse(messageRuntime, requestMessage);
        if (responseMessage == null)
        {
            return new AgentWorkItemResumeResult
            {
                WorkItemId = workItem.Id,
                Status = AgentWorkItemResumeStatus.ApprovalNotReady,
                CurrentStatus = workItem.Status,
                Owner = workItem.Owner,
                ApprovalRequestId = requestMessage.Id,
                ThreadId = requestMessage.ThreadId,
                Message = $"{workItem.Id} does not have an approved mailbox response yet.",
            };
        }

        var activation = await activationRuntime.TryActivateAsync(responseMessage, cancellationToken);
        return activation.Status switch
        {
            AgentMessageActivationStatus.Reactivated => new AgentWorkItemResumeResult
            {
                WorkItemId = workItem.Id,
                Status = AgentWorkItemResumeStatus.Resumed,
                CurrentStatus = taskRuntime.GetWorkItem(workItem.Id)?.Status ?? workItem.Status,
                Owner = workItem.Owner,
                ApprovalRequestId = requestMessage.Id,
                ApprovalResponseId = responseMessage.Id,
                ThreadId = requestMessage.ThreadId,
                Activation = activation,
                Message = $"Resumed {workItem.Id}.",
            },
            AgentMessageActivationStatus.AlreadyActive => new AgentWorkItemResumeResult
            {
                WorkItemId = workItem.Id,
                Status = AgentWorkItemResumeStatus.AlreadyActive,
                CurrentStatus = taskRuntime.GetWorkItem(workItem.Id)?.Status ?? workItem.Status,
                Owner = workItem.Owner,
                ApprovalRequestId = requestMessage.Id,
                ApprovalResponseId = responseMessage.Id,
                ThreadId = requestMessage.ThreadId,
                Activation = activation,
                Message = $"{workItem.Id} is still waiting because {activation.Owner} already has an active background run.",
            },
            AgentMessageActivationStatus.NotRegistered => new AgentWorkItemResumeResult
            {
                WorkItemId = workItem.Id,
                Status = AgentWorkItemResumeStatus.ActivationNotRegistered,
                CurrentStatus = taskRuntime.GetWorkItem(workItem.Id)?.Status ?? workItem.Status,
                Owner = workItem.Owner,
                ApprovalRequestId = requestMessage.Id,
                ApprovalResponseId = responseMessage.Id,
                ThreadId = requestMessage.ThreadId,
                Activation = activation,
                Message = $"No activation handler is registered for {activation.Owner}.",
            },
            _ => new AgentWorkItemResumeResult
            {
                WorkItemId = workItem.Id,
                Status = AgentWorkItemResumeStatus.Failed,
                CurrentStatus = taskRuntime.GetWorkItem(workItem.Id)?.Status ?? workItem.Status,
                Owner = workItem.Owner,
                ApprovalRequestId = requestMessage.Id,
                ApprovalResponseId = responseMessage.Id,
                ThreadId = requestMessage.ThreadId,
                Activation = activation,
                Message = $"Failed to resume {workItem.Id}: {activation.Message ?? "Unknown error"}.",
            },
        };
    }

    private static AgentMessage? ResolveApprovalRequest(
        IAgentMessageRuntime messageRuntime,
        AgentWorkItem workItem)
    {
        if (!string.IsNullOrWhiteSpace(workItem.ApprovalRequestId))
        {
            var byId = messageRuntime.GetMessage(workItem.ApprovalRequestId);
            if (byId?.Kind == AgentMessageKind.PlanApprovalRequest)
                return byId;
        }

        if (string.IsNullOrWhiteSpace(workItem.ApprovalThreadId))
            return null;

        return messageRuntime.ListThread(workItem.ApprovalThreadId)
            .Where(message => message.Kind == AgentMessageKind.PlanApprovalRequest)
            .OrderByDescending(message => message.CreatedAt)
            .ThenByDescending(message => message.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static AgentMessage? ResolveApprovalResponse(
        IAgentMessageRuntime messageRuntime,
        AgentMessage requestMessage)
    {
        return messageRuntime.ListThread(requestMessage.ThreadId)
            .Where(message => message.Kind == AgentMessageKind.PlanApprovalResponse)
            .Where(message =>
                string.Equals(message.RelatedMessageId, requestMessage.Id, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    message.Protocol?.ActionName,
                    "plan-approval-approved",
                    StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(message => message.CreatedAt)
            .ThenByDescending(message => message.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }
}

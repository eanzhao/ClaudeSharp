using Aexon.Core.Agents;

namespace Aexon.Core.Tests.Agents;

/// <summary>
/// Contains tests for resuming approved work items.
/// </summary>
public sealed class AgentWorkItemResumerTests
{
    [Fact]
    public async Task TryResumeAsync_ReactivatesApprovedWorkItem()
    {
        var messages = new InMemoryAgentMessageRuntime();
        var request = messages.SendMessage(
            "subagent",
            "main",
            AgentMessageKind.PlanApprovalRequest,
            "Approve this runtime plan",
            subject: "Runtime plan");
        var approval = messages.SendMessage(
            "main",
            "subagent",
            AgentMessageKind.PlanApprovalResponse,
            "Looks good",
            relatedMessageId: request.Id,
            protocol: new AgentMessageProtocol
            {
                ActionName = "plan-approval-approved",
            });

        var tasks = new InMemoryAgentTaskRuntime();
        var workItem = tasks.CreateWorkItem("Inspect runtime", owner: "subagent");
        tasks.UpdateWorkItem(workItem.Id, item =>
        {
            item.Status = AgentWorkItemStatus.AwaitingResume;
            item.ApprovalRequestId = request.Id;
            item.ApprovalThreadId = request.ThreadId;
        });

        var activations = new InMemoryAgentMessageActivationRuntime();
        activations.RegisterOwner(
            "subagent",
            (trigger, _) =>
            {
                Assert.Equal(approval.Id, trigger.Message.Id);
                Assert.Equal(request.ThreadId, trigger.Message.ThreadId);
                AgentWorkItemApprovalCoordinator.TryResumeApprovedWorkItem(tasks, workItem.Id);
                return Task.FromResult(AgentMessageActivationResult.Reactivated(
                    "subagent",
                    "background-run-7",
                    workItem.Id,
                    "Triggered by approval."));
            });

        var result = await AgentWorkItemResumer.TryResumeAsync(
            tasks,
            messages,
            activations,
            workItem.Id);

        Assert.Equal(AgentWorkItemResumeStatus.Resumed, result.Status);
        Assert.Equal(request.Id, result.ApprovalRequestId);
        Assert.Equal(approval.Id, result.ApprovalResponseId);
        Assert.Equal(AgentWorkItemStatus.InProgress, tasks.GetWorkItem(workItem.Id)!.Status);
    }

    [Fact]
    public async Task TryResumeAsync_ReturnsApprovalNotReadyWhenApprovedResponseIsMissing()
    {
        var messages = new InMemoryAgentMessageRuntime();
        var request = messages.SendMessage(
            "subagent",
            "main",
            AgentMessageKind.PlanApprovalRequest,
            "Approve this runtime plan",
            subject: "Runtime plan");

        var tasks = new InMemoryAgentTaskRuntime();
        var workItem = tasks.CreateWorkItem("Inspect runtime", owner: "subagent");
        tasks.UpdateWorkItem(workItem.Id, item =>
        {
            item.Status = AgentWorkItemStatus.AwaitingResume;
            item.ApprovalRequestId = request.Id;
            item.ApprovalThreadId = request.ThreadId;
        });

        var result = await AgentWorkItemResumer.TryResumeAsync(
            tasks,
            messages,
            new InMemoryAgentMessageActivationRuntime(),
            workItem.Id);

        Assert.Equal(AgentWorkItemResumeStatus.ApprovalNotReady, result.Status);
        Assert.Contains("does not have an approved mailbox response yet", result.Message, StringComparison.Ordinal);
        Assert.Equal(AgentWorkItemStatus.AwaitingResume, tasks.GetWorkItem(workItem.Id)!.Status);
    }
}

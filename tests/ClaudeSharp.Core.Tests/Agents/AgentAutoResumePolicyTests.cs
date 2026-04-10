using ClaudeSharp.Core.Agents;

namespace ClaudeSharp.Core.Tests.Agents;

/// <summary>
/// Contains tests for automatic approved-work-item resume behavior.
/// </summary>
public sealed class AgentAutoResumePolicyTests
{
    [Fact]
    public async Task TryResumeEligibleAsync_ResumesOldestIdleWorkItem()
    {
        var messages = new InMemoryAgentMessageRuntime();
        var firstRequest = messages.SendMessage(
            "subagent",
            "main",
            AgentMessageKind.PlanApprovalRequest,
            "Approve first");
        var firstApproval = messages.SendMessage(
            "main",
            "subagent",
            AgentMessageKind.PlanApprovalResponse,
            "Approved first",
            relatedMessageId: firstRequest.Id,
            protocol: new AgentMessageProtocol
            {
                ActionName = "plan-approval-approved",
            });
        var secondRequest = messages.SendMessage(
            "subagent",
            "main",
            AgentMessageKind.PlanApprovalRequest,
            "Approve second");
        messages.SendMessage(
            "main",
            "subagent",
            AgentMessageKind.PlanApprovalResponse,
            "Approved second",
            relatedMessageId: secondRequest.Id,
            protocol: new AgentMessageProtocol
            {
                ActionName = "plan-approval-approved",
            });

        var tasks = new InMemoryAgentTaskRuntime();
        var firstItem = tasks.CreateWorkItem("First", owner: "subagent");
        var secondItem = tasks.CreateWorkItem("Second", owner: "subagent");
        tasks.UpdateWorkItem(firstItem.Id, item =>
        {
            item.Status = AgentWorkItemStatus.AwaitingResume;
            item.ApprovalRequestId = firstRequest.Id;
            item.ApprovalThreadId = firstRequest.ThreadId;
        });
        tasks.UpdateWorkItem(secondItem.Id, item =>
        {
            item.Status = AgentWorkItemStatus.AwaitingResume;
            item.ApprovalRequestId = secondRequest.Id;
            item.ApprovalThreadId = secondRequest.ThreadId;
        });

        var activations = new InMemoryAgentMessageActivationRuntime();
        activations.RegisterOwner(
            "subagent",
            (trigger, _) =>
            {
                Assert.Equal(firstApproval.Id, trigger.Message.Id);
                AgentWorkItemApprovalCoordinator.TryResumeApprovedWorkItem(tasks, firstItem.Id);
                tasks.StartBackgroundRun(
                    "Resumed first",
                    owner: "subagent",
                    workItemId: firstItem.Id,
                    initialStatus: AgentBackgroundRunStatus.Running);
                return Task.FromResult(AgentMessageActivationResult.Reactivated(
                    "subagent",
                    "background-run-1",
                    firstItem.Id));
            });

        var results = await AgentAutoResumePolicy.TryResumeEligibleAsync(
            tasks,
            messages,
            activations,
            owner: "subagent");

        var result = Assert.Single(results);
        Assert.Equal(AgentWorkItemResumeStatus.Resumed, result.Status);
        Assert.Equal(firstItem.Id, result.WorkItemId);
        Assert.Equal(AgentWorkItemStatus.InProgress, tasks.GetWorkItem(firstItem.Id)!.Status);
        Assert.Equal(AgentWorkItemStatus.AwaitingResume, tasks.GetWorkItem(secondItem.Id)!.Status);
    }

    [Fact]
    public async Task TryResumeEligibleAsync_SkipsOwnersThatAreStillBusy()
    {
        var messages = new InMemoryAgentMessageRuntime();
        var request = messages.SendMessage(
            "subagent",
            "main",
            AgentMessageKind.PlanApprovalRequest,
            "Approve first");
        messages.SendMessage(
            "main",
            "subagent",
            AgentMessageKind.PlanApprovalResponse,
            "Approved first",
            relatedMessageId: request.Id,
            protocol: new AgentMessageProtocol
            {
                ActionName = "plan-approval-approved",
            });

        var tasks = new InMemoryAgentTaskRuntime();
        var item = tasks.CreateWorkItem("First", owner: "subagent");
        tasks.UpdateWorkItem(item.Id, workItem =>
        {
            workItem.Status = AgentWorkItemStatus.AwaitingResume;
            workItem.ApprovalRequestId = request.Id;
            workItem.ApprovalThreadId = request.ThreadId;
        });
        tasks.StartBackgroundRun(
            "Busy run",
            owner: "subagent",
            workItemId: "other-work-item",
            initialStatus: AgentBackgroundRunStatus.Running);

        var activations = new InMemoryAgentMessageActivationRuntime();
        activations.RegisterOwner(
            "subagent",
            (_, _) => throw new InvalidOperationException("Should not activate while owner is busy."));

        var results = await AgentAutoResumePolicy.TryResumeEligibleAsync(
            tasks,
            messages,
            activations,
            owner: "subagent");

        Assert.Empty(results);
        Assert.Equal(AgentWorkItemStatus.AwaitingResume, tasks.GetWorkItem(item.Id)!.Status);
    }

    [Fact]
    public async Task TryResumeEligibleAsync_LatestModePicksMostRecentCandidate()
    {
        var messages = new InMemoryAgentMessageRuntime();
        var firstRequest = messages.SendMessage("subagent", "main", AgentMessageKind.PlanApprovalRequest, "Approve first");
        messages.SendMessage(
            "main",
            "subagent",
            AgentMessageKind.PlanApprovalResponse,
            "Approved first",
            relatedMessageId: firstRequest.Id,
            protocol: new AgentMessageProtocol { ActionName = "plan-approval-approved" });
        var secondRequest = messages.SendMessage("subagent", "main", AgentMessageKind.PlanApprovalRequest, "Approve second");
        var secondApproval = messages.SendMessage(
            "main",
            "subagent",
            AgentMessageKind.PlanApprovalResponse,
            "Approved second",
            relatedMessageId: secondRequest.Id,
            protocol: new AgentMessageProtocol { ActionName = "plan-approval-approved" });

        var tasks = new InMemoryAgentTaskRuntime();
        var firstItem = tasks.CreateWorkItem("First", owner: "subagent");
        var secondItem = tasks.CreateWorkItem("Second", owner: "subagent");
        tasks.UpdateWorkItem(firstItem.Id, item =>
        {
            item.Status = AgentWorkItemStatus.AwaitingResume;
            item.ApprovalRequestId = firstRequest.Id;
            item.ApprovalThreadId = firstRequest.ThreadId;
        });
        await Task.Delay(5);
        tasks.UpdateWorkItem(secondItem.Id, item =>
        {
            item.Status = AgentWorkItemStatus.AwaitingResume;
            item.ApprovalRequestId = secondRequest.Id;
            item.ApprovalThreadId = secondRequest.ThreadId;
        });

        var activations = new InMemoryAgentMessageActivationRuntime();
        activations.RegisterOwner(
            "subagent",
            (trigger, _) =>
            {
                Assert.Equal(secondApproval.Id, trigger.Message.Id);
                AgentWorkItemApprovalCoordinator.TryResumeApprovedWorkItem(tasks, secondItem.Id);
                return Task.FromResult(AgentMessageActivationResult.Reactivated(
                    "subagent",
                    "background-run-2",
                    secondItem.Id));
            });

        var results = await AgentAutoResumePolicy.TryResumeEligibleAsync(
            tasks,
            messages,
            activations,
            AgentAutoResumeMode.Latest,
            owner: "subagent");

        var result = Assert.Single(results);
        Assert.Equal(secondItem.Id, result.WorkItemId);
        Assert.Equal(AgentWorkItemStatus.AwaitingResume, tasks.GetWorkItem(firstItem.Id)!.Status);
        Assert.Equal(AgentWorkItemStatus.InProgress, tasks.GetWorkItem(secondItem.Id)!.Status);
    }

    [Fact]
    public async Task TryResumeEligibleAsync_DisabledModeDoesNothing()
    {
        var messages = new InMemoryAgentMessageRuntime();
        var request = messages.SendMessage("subagent", "main", AgentMessageKind.PlanApprovalRequest, "Approve");
        messages.SendMessage(
            "main",
            "subagent",
            AgentMessageKind.PlanApprovalResponse,
            "Approved",
            relatedMessageId: request.Id,
            protocol: new AgentMessageProtocol { ActionName = "plan-approval-approved" });

        var tasks = new InMemoryAgentTaskRuntime();
        var item = tasks.CreateWorkItem("Queued", owner: "subagent");
        tasks.UpdateWorkItem(item.Id, workItem =>
        {
            workItem.Status = AgentWorkItemStatus.AwaitingResume;
            workItem.ApprovalRequestId = request.Id;
            workItem.ApprovalThreadId = request.ThreadId;
        });

        var activations = new InMemoryAgentMessageActivationRuntime();
        activations.RegisterOwner(
            "subagent",
            (_, _) => throw new InvalidOperationException("Disabled mode should not trigger activation."));

        var results = await AgentAutoResumePolicy.TryResumeEligibleAsync(
            tasks,
            messages,
            activations,
            AgentAutoResumeMode.Disabled,
            owner: "subagent");

        Assert.Empty(results);
        Assert.Equal(AgentWorkItemStatus.AwaitingResume, tasks.GetWorkItem(item.Id)!.Status);
    }
}

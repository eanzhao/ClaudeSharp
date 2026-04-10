using ClaudeSharp.Core.Agents;

namespace ClaudeSharp.Core.Tests.Agents;

/// <summary>
/// Contains tests for approval-driven work-item coordination.
/// </summary>
public sealed class AgentWorkItemApprovalCoordinatorTests
{
    [Fact]
    public void TryMarkAwaitingApproval_TracksRequestOnOriginalWorkItem()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var workItem = runtime.CreateWorkItem("Inspect runtime", owner: "subagent");
        runtime.UpdateWorkItem(workItem.Id, item => item.Status = AgentWorkItemStatus.InProgress);
        var request = new InMemoryAgentMessageRuntime().SendMessage(
            "subagent",
            "main",
            AgentMessageKind.PlanApprovalRequest,
            "Approve this runtime plan",
            subject: "Runtime plan");

        var changed = AgentWorkItemApprovalCoordinator.TryMarkAwaitingApproval(runtime, workItem.Id, request);

        Assert.True(changed);
        var stored = runtime.GetWorkItem(workItem.Id);
        Assert.NotNull(stored);
        Assert.Equal(AgentWorkItemStatus.AwaitingApproval, stored!.Status);
        Assert.Equal(request.Id, stored.ApprovalRequestId);
        Assert.Equal(request.ThreadId, stored.ApprovalThreadId);
    }

    [Fact]
    public void TryApplyApprovalResponse_ApproveMarksWorkItemAwaitingResume()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var messages = new InMemoryAgentMessageRuntime();
        var request = messages.SendMessage(
            "subagent",
            "main",
            AgentMessageKind.PlanApprovalRequest,
            "Approve this runtime plan");
        var response = messages.SendMessage(
            "main",
            "subagent",
            AgentMessageKind.PlanApprovalResponse,
            "Approved",
            relatedMessageId: request.Id,
            protocol: new AgentMessageProtocol
            {
                ActionName = "plan-approval-approved",
            });

        var workItem = runtime.CreateWorkItem("Inspect runtime", owner: "subagent");
        runtime.UpdateWorkItem(workItem.Id, item =>
        {
            item.Status = AgentWorkItemStatus.AwaitingApproval;
            item.ApprovalRequestId = request.Id;
            item.ApprovalThreadId = request.ThreadId;
        });

        var changed = AgentWorkItemApprovalCoordinator.TryApplyApprovalResponse(runtime, request, response);

        Assert.True(changed);
        var stored = runtime.GetWorkItem(workItem.Id);
        Assert.NotNull(stored);
        Assert.Equal(AgentWorkItemStatus.AwaitingResume, stored!.Status);
        Assert.Equal(request.Id, stored.ApprovalRequestId);
        Assert.Equal(request.ThreadId, stored.ApprovalThreadId);
    }

    [Fact]
    public void TryApplyApprovalResponse_RejectBlocksAndClearsApprovalTracking()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var messages = new InMemoryAgentMessageRuntime();
        var request = messages.SendMessage(
            "subagent",
            "main",
            AgentMessageKind.PlanApprovalRequest,
            "Approve this runtime plan");
        var response = messages.SendMessage(
            "main",
            "subagent",
            AgentMessageKind.PlanApprovalResponse,
            "Rejected",
            relatedMessageId: request.Id,
            protocol: new AgentMessageProtocol
            {
                ActionName = "plan-approval-rejected",
            });

        var workItem = runtime.CreateWorkItem("Inspect runtime", owner: "subagent");
        runtime.UpdateWorkItem(workItem.Id, item =>
        {
            item.Status = AgentWorkItemStatus.AwaitingApproval;
            item.ApprovalRequestId = request.Id;
            item.ApprovalThreadId = request.ThreadId;
        });

        var changed = AgentWorkItemApprovalCoordinator.TryApplyApprovalResponse(runtime, request, response);

        Assert.True(changed);
        var stored = runtime.GetWorkItem(workItem.Id);
        Assert.NotNull(stored);
        Assert.Equal(AgentWorkItemStatus.Blocked, stored!.Status);
        Assert.Null(stored.ApprovalRequestId);
        Assert.Null(stored.ApprovalThreadId);
    }

    [Fact]
    public void SuccessfulRunCompletion_PreservesAwaitingStatesAndResumeClearsThem()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var workItem = runtime.CreateWorkItem("Inspect runtime", owner: "subagent");
        runtime.UpdateWorkItem(workItem.Id, item =>
        {
            item.Status = AgentWorkItemStatus.AwaitingResume;
            item.ApprovalRequestId = "agent-message-1";
            item.ApprovalThreadId = "thread-1";
        });

        Assert.True(AgentWorkItemApprovalCoordinator.TryCompleteSuccessfulRun(runtime, workItem.Id));
        Assert.Equal(AgentWorkItemStatus.AwaitingResume, runtime.GetWorkItem(workItem.Id)?.Status);

        Assert.True(AgentWorkItemApprovalCoordinator.TryResumeApprovedWorkItem(runtime, workItem.Id));
        var resumed = runtime.GetWorkItem(workItem.Id);
        Assert.NotNull(resumed);
        Assert.Equal(AgentWorkItemStatus.InProgress, resumed!.Status);
        Assert.Null(resumed.ApprovalRequestId);
        Assert.Null(resumed.ApprovalThreadId);

        Assert.True(AgentWorkItemApprovalCoordinator.TryCompleteSuccessfulRun(runtime, workItem.Id));
        var completed = runtime.GetWorkItem(workItem.Id);
        Assert.NotNull(completed);
        Assert.Equal(AgentWorkItemStatus.Completed, completed!.Status);
    }

    [Fact]
    public void DescribeApprovalState_ReturnsFriendlyLabelsForWaitingStates()
    {
        Assert.Equal(
            "Waiting for approval",
            AgentWorkItemApprovalCoordinator.DescribeApprovalState(new AgentWorkItem
            {
                Id = "work-item-1",
                Title = "Inspect runtime",
                Status = AgentWorkItemStatus.AwaitingApproval,
            }));
        Assert.Equal(
            "Approved, waiting to resume",
            AgentWorkItemApprovalCoordinator.DescribeApprovalState(new AgentWorkItem
            {
                Id = "work-item-2",
                Title = "Inspect runtime",
                Status = AgentWorkItemStatus.AwaitingResume,
            }));
    }
}

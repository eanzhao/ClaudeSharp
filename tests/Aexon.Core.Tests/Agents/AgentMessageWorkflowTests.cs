using Aexon.Core.Agents;

namespace Aexon.Core.Tests.Agents;

/// <summary>
/// Contains tests for mailbox action workflows.
/// </summary>
public sealed class AgentMessageWorkflowTests
{
    [Fact]
    public void ListPendingActions_FindsOutstandingApprovalsShutdownsAndFollowUps()
    {
        var runtime = new InMemoryAgentMessageRuntime();
        var plan = runtime.SendMessage("lead", "Platform/Ada", AgentMessageKind.PlanApprovalRequest, "Approve this plan", subject: "Plan");
        runtime.SendMessage("Platform/Ada", "lead", AgentMessageKind.PlanApprovalResponse, "Approved", relatedMessageId: plan.Id);
        var shutdown = runtime.SendMessage("lead", "Platform/Ada", AgentMessageKind.ShutdownRequest, "Please stop");
        runtime.SendMessage(
            "lead",
            "Platform/Ada",
            AgentMessageKind.Note,
            "Need a follow-up",
            protocol: new AgentMessageProtocol
            {
                ActionName = "follow-up",
                RequiresResponse = true,
            });

        var pending = AgentMessageWorkflow.ListPendingActions(runtime, "Platform/Ada");

        Assert.Equal(2, pending.Count);
        Assert.Contains(pending, item => item.ActionType == AgentMessageActionType.Shutdown);
        Assert.Contains(pending, item => item.ActionType == AgentMessageActionType.FollowUp);
        Assert.DoesNotContain(pending, item => item.ActionType == AgentMessageActionType.PlanApproval);
    }

    [Theory]
    [InlineData(AgentMessageKind.PlanApprovalRequest, "approve", AgentMessageKind.PlanApprovalResponse, "plan-approval-approved")]
    [InlineData(AgentMessageKind.PlanApprovalRequest, "reject", AgentMessageKind.PlanApprovalResponse, "plan-approval-rejected")]
    [InlineData(AgentMessageKind.ShutdownRequest, "ack", AgentMessageKind.ShutdownResponse, "shutdown-acknowledged")]
    [InlineData(AgentMessageKind.ShutdownRequest, "decline", AgentMessageKind.ShutdownResponse, "shutdown-declined")]
    public void TryBuildResponse_BuildsStructuredControlResponses(
        AgentMessageKind kind,
        string decision,
        AgentMessageKind expectedKind,
        string expectedAction)
    {
        var trigger = new AgentMessage
        {
            Id = "agent-message-1",
            ThreadId = "thread-1",
            From = "lead",
            To = "Platform/Ada",
            Kind = kind,
            Body = "Please handle this",
            Subject = "Work item",
        };

        var success = AgentMessageWorkflow.TryBuildResponse(
            trigger,
            "Platform/Ada",
            decision,
            note: null,
            out var response,
            out var error);

        Assert.True(success, error);
        Assert.NotNull(response);
        Assert.Equal(expectedKind, response!.Kind);
        Assert.Equal(expectedAction, response.Protocol?.ActionName);
        Assert.Equal("Re: Work item", response.Subject);
        Assert.Equal("lead", response.To);
        Assert.Equal("Platform/Ada", response.From);
    }

    [Fact]
    public void TryBuildResponse_BuildsFollowUpReplies()
    {
        var trigger = new AgentMessage
        {
            Id = "agent-message-1",
            ThreadId = "thread-1",
            From = "lead",
            To = "Platform/Ada",
            Kind = AgentMessageKind.Note,
            Body = "Need a reply",
            Protocol = new AgentMessageProtocol
            {
                RequiresResponse = true,
            },
        };

        var success = AgentMessageWorkflow.TryBuildResponse(
            trigger,
            "Platform/Ada",
            "done",
            "Finished the task",
            out var response,
            out var error);

        Assert.True(success, error);
        Assert.NotNull(response);
        Assert.Equal(AgentMessageKind.Note, response!.Kind);
        Assert.Equal("follow-up-completed", response.Protocol?.ActionName);
        Assert.Equal("Finished the task", response.Body);
    }
}

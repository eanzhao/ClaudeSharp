using Aexon.Core.Agents;

namespace Aexon.Core.Tests.Agents;

/// <summary>
/// Contains tests for mailbox-to-task projection.
/// </summary>
public sealed class AgentMailboxTaskProjectorTests
{
    [Fact]
    public void Synchronize_CreatesPendingApprovalTodo()
    {
        var messageRuntime = new InMemoryAgentMessageRuntime();
        var taskRuntime = new InMemoryAgentTaskRuntime();
        var trigger = messageRuntime.SendMessage(
            "lead",
            "Platform/Ada",
            AgentMessageKind.PlanApprovalRequest,
            "Approve this launch plan",
            subject: "Launch");

        var result = AgentMailboxTaskProjector.Synchronize(messageRuntime, taskRuntime);

        Assert.True(result.HasChanges);
        Assert.Single(result.CreatedWorkItemIds);
        var workItem = Assert.Single(taskRuntime.ListWorkItems());
        Assert.Equal("Approval: Launch", workItem.Title);
        Assert.Equal("Platform/Ada", workItem.Owner);
        Assert.Equal(AgentWorkItemStatus.Pending, workItem.Status);
        Assert.Equal(AgentWorkItemSourceKinds.MailboxPlanApproval, workItem.SourceKind);
        Assert.Equal(trigger.Id, workItem.SourceId);
        Assert.Equal(trigger.ThreadId, workItem.SourceThreadId);
        Assert.Contains("Waiting for approval response.", workItem.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void Synchronize_UpdatesExistingApprovalTodoWhenApproved()
    {
        var messageRuntime = new InMemoryAgentMessageRuntime();
        var taskRuntime = new InMemoryAgentTaskRuntime();
        var trigger = messageRuntime.SendMessage(
            "lead",
            "Platform/Ada",
            AgentMessageKind.PlanApprovalRequest,
            "Approve this launch plan",
            subject: "Launch");
        AgentMailboxTaskProjector.Synchronize(messageRuntime, taskRuntime);

        messageRuntime.SendMessage(
            "Platform/Ada",
            "lead",
            AgentMessageKind.PlanApprovalResponse,
            "Looks good",
            subject: "Re: Launch",
            relatedMessageId: trigger.Id,
            protocol: new AgentMessageProtocol
            {
                ActionName = "plan-approval-approved",
            });

        var result = AgentMailboxTaskProjector.Synchronize(messageRuntime, taskRuntime);

        Assert.True(result.HasChanges);
        var workItem = Assert.Single(taskRuntime.ListWorkItems());
        Assert.Equal(AgentWorkItemStatus.Completed, workItem.Status);
        Assert.Contains("Decision: approved.", workItem.Description, StringComparison.Ordinal);
        Assert.Contains("Resolved by Platform/Ada.", workItem.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void Synchronize_UpdatesExistingApprovalTodoWhenRejected()
    {
        var messageRuntime = new InMemoryAgentMessageRuntime();
        var taskRuntime = new InMemoryAgentTaskRuntime();
        var trigger = messageRuntime.SendMessage(
            "lead",
            "Platform/Ada",
            AgentMessageKind.PlanApprovalRequest,
            "Approve this launch plan",
            subject: "Launch");
        AgentMailboxTaskProjector.Synchronize(messageRuntime, taskRuntime);

        messageRuntime.SendMessage(
            "Platform/Ada",
            "lead",
            AgentMessageKind.PlanApprovalResponse,
            "Not yet",
            subject: "Re: Launch",
            relatedMessageId: trigger.Id,
            protocol: new AgentMessageProtocol
            {
                ActionName = "plan-approval-rejected",
            });

        AgentMailboxTaskProjector.Synchronize(messageRuntime, taskRuntime);

        var workItem = Assert.Single(taskRuntime.ListWorkItems());
        Assert.Equal(AgentWorkItemStatus.Cancelled, workItem.Status);
        Assert.Contains("Decision: rejected.", workItem.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void Synchronize_RepeatedRunsDoNotDuplicateApprovalTodos()
    {
        var messageRuntime = new InMemoryAgentMessageRuntime();
        var taskRuntime = new InMemoryAgentTaskRuntime();
        messageRuntime.SendMessage(
            "lead",
            "Platform/Ada",
            AgentMessageKind.PlanApprovalRequest,
            "Approve this launch plan",
            subject: "Launch");

        AgentMailboxTaskProjector.Synchronize(messageRuntime, taskRuntime);
        var second = AgentMailboxTaskProjector.Synchronize(messageRuntime, taskRuntime);

        Assert.False(second.HasChanges);
        Assert.Single(taskRuntime.ListWorkItems());
    }

    [Fact]
    public void Synchronize_DoesNotRecreateResolvedApprovalTodoFromHistoryAlone()
    {
        var messageRuntime = new InMemoryAgentMessageRuntime();
        var taskRuntime = new InMemoryAgentTaskRuntime();
        var trigger = messageRuntime.SendMessage(
            "lead",
            "Platform/Ada",
            AgentMessageKind.PlanApprovalRequest,
            "Approve this launch plan",
            subject: "Launch");
        messageRuntime.SendMessage(
            "Platform/Ada",
            "lead",
            AgentMessageKind.PlanApprovalResponse,
            "Looks good",
            subject: "Re: Launch",
            relatedMessageId: trigger.Id,
            protocol: new AgentMessageProtocol
            {
                ActionName = "plan-approval-approved",
            });

        var result = AgentMailboxTaskProjector.Synchronize(messageRuntime, taskRuntime);

        Assert.False(result.HasChanges);
        Assert.Empty(taskRuntime.ListWorkItems());
    }
}

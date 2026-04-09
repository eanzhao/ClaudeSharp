using ClaudeSharp.Core.Agents;
using ClaudeSharp.Core.AppState;
using ClaudeSharp.Core.Permissions;

namespace ClaudeSharp.Core.Tests.AppState;

/// <summary>
/// Contains tests for mailbox projection into app state.
/// </summary>
public sealed class AppStateMailboxProjectionTests
{
    [Fact]
    public void CreateSnapshot_ProjectsPendingMailboxActionsAndPlanApprovals()
    {
        var runtime = new InMemoryAgentMessageRuntime();
        runtime.SendMessage("lead", "Ada", AgentMessageKind.PlanApprovalRequest, "Approve launch", subject: "Launch");
        runtime.SendMessage("lead", "Ada", AgentMessageKind.ShutdownRequest, "Pause rollout", subject: "Launch");
        runtime.SendMessage(
            "lead",
            "Ada",
            "Please confirm the rollout",
            AgentMessageKind.Note,
            subject: "Launch",
            protocol: new AgentMessageProtocol
            {
                ActionName = "follow-up-request",
                RequiresResponse = true,
            });

        var projector = new AppStateProjector();
        var snapshot = projector.CreateSnapshot(
            "/workspace",
            PermissionMode.Plan,
            agentMessageRuntime: runtime);

        var ada = Assert.Single(snapshot.Mailboxes, mailbox =>
            string.Equals(mailbox.Participant, "Ada", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(3, ada.InboxCount);
        Assert.Equal(3, ada.UnreadCount);
        Assert.Equal(0, ada.OutboxCount);
        Assert.Equal(3, ada.ThreadCount);
        Assert.Equal(3, ada.PendingActionCount);
        Assert.Equal(1, ada.PendingPlanApprovalCount);
        Assert.Equal("lead", ada.LatestCounterparty);
    }

    [Fact]
    public void CreateSnapshot_ExcludesResolvedPlanApprovalsFromPendingCounts()
    {
        var runtime = new InMemoryAgentMessageRuntime();
        var request = runtime.SendMessage(
            "lead",
            "Ada",
            AgentMessageKind.PlanApprovalRequest,
            "Approve launch",
            subject: "Launch");
        runtime.SendMessage(
            "Ada",
            "lead",
            AgentMessageKind.PlanApprovalResponse,
            "Approved",
            subject: "Re: Launch",
            relatedMessageId: request.Id);

        var projector = new AppStateProjector();
        var snapshot = projector.CreateSnapshot(
            "/workspace",
            PermissionMode.Plan,
            agentMessageRuntime: runtime);

        var ada = Assert.Single(snapshot.Mailboxes, mailbox =>
            string.Equals(mailbox.Participant, "Ada", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(1, ada.InboxCount);
        Assert.Equal(1, ada.UnreadCount);
        Assert.Equal(1, ada.OutboxCount);
        Assert.Equal(1, ada.ThreadCount);
        Assert.Equal(0, ada.PendingActionCount);
        Assert.Equal(0, ada.PendingPlanApprovalCount);
        Assert.Equal("lead", ada.LatestCounterparty);
    }

    [Fact]
    public void CreateSnapshot_WithoutMailboxRuntimeLeavesMailboxListEmpty()
    {
        var projector = new AppStateProjector();

        var snapshot = projector.CreateSnapshot("/workspace", PermissionMode.Default);

        Assert.Empty(snapshot.Mailboxes);
    }
}

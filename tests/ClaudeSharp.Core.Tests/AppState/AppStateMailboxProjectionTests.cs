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
    public void CreateSnapshot_ProjectsMailboxSummaries()
    {
        var runtime = new InMemoryAgentMessageRuntime();
        var launch = runtime.SendMessage("Ada", "Bob", "Inspect launch", AgentMessageKind.Note, subject: "Launch");
        runtime.SendMessage("Ada", "Bob", "Follow up", AgentMessageKind.PlanApprovalRequest, subject: "Launch");
        runtime.SendMessage("Bob", "Ada", "Looks good", AgentMessageKind.PlanApprovalResponse, subject: "Re: Launch", relatedMessageId: launch.Id);
        runtime.MarkRead(launch.Id);

        var projector = new AppStateProjector();
        var snapshot = projector.CreateSnapshot(
            "/workspace",
            PermissionMode.Plan,
            agentMessageRuntime: runtime);

        var ada = Assert.Single(snapshot.Mailboxes, mailbox =>
            string.Equals(mailbox.Participant, "Ada", StringComparison.OrdinalIgnoreCase));
        var bob = Assert.Single(snapshot.Mailboxes, mailbox =>
            string.Equals(mailbox.Participant, "Bob", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(1, ada.InboxCount);
        Assert.Equal(1, ada.UnreadCount);
        Assert.Equal(2, ada.OutboxCount);
        Assert.Equal(2, ada.ThreadCount);
        Assert.Equal("Bob", ada.LatestCounterparty);

        Assert.Equal(2, bob.InboxCount);
        Assert.Equal(1, bob.UnreadCount);
        Assert.Equal(1, bob.OutboxCount);
        Assert.Equal(2, bob.ThreadCount);
        Assert.Equal("Ada", bob.LatestCounterparty);
    }

    [Fact]
    public void CreateSnapshot_WithoutMailboxRuntimeLeavesMailboxListEmpty()
    {
        var projector = new AppStateProjector();

        var snapshot = projector.CreateSnapshot("/workspace", PermissionMode.Default);

        Assert.Empty(snapshot.Mailboxes);
    }
}

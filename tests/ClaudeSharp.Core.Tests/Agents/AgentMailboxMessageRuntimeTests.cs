using ClaudeSharp.Core.Agents;

namespace ClaudeSharp.Core.Tests.Agents;

/// <summary>
/// Contains deep tests for the mailbox runtime.
/// </summary>
public sealed class AgentMailboxMessageRuntimeTests
{
    [Fact]
    public void SendMessage_CreatesThreadsAndKeepsInboxOutboxSeparated()
    {
        var runtime = new InMemoryAgentMailboxRuntime();

        var first = runtime.SendMessage("Ada", "Bob", "Inspect launch", subject: "Launch");
        var second = runtime.SendMessage("Ada", "Bob", "Follow up", subject: "Launch");
        var reply = runtime.SendMessage("Bob", "Ada", "Looks good", subject: "Re: Launch", replyToMessageId: first.Id);

        Assert.Equal(first.ThreadId, reply.ThreadId);
        Assert.NotEqual(first.ThreadId, second.ThreadId);
        Assert.Collection(
            runtime.ListThread(first.ThreadId),
            message => Assert.Equal(first.Id, message.Id),
            message => Assert.Equal(reply.Id, message.Id));
        Assert.Collection(
            runtime.ListInbox("Bob"),
            message => Assert.Equal(first.Id, message.Id),
            message => Assert.Equal(second.Id, message.Id));
        var adaInbox = Assert.Single(runtime.ListInbox("Ada"));
        Assert.Equal(reply.Id, adaInbox.Id);
        Assert.Collection(
            runtime.ListOutbox("Ada"),
            message => Assert.Equal(first.Id, message.Id),
            message => Assert.Equal(second.Id, message.Id));

        var adaMailbox = runtime.ListMailboxes().Single(mailbox =>
            string.Equals(mailbox.Participant, "Ada", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, adaMailbox.OutboxCount);
        Assert.Equal(1, adaMailbox.InboxCount);
        Assert.Equal(1, adaMailbox.UnreadCount);
        Assert.Equal(2, adaMailbox.ThreadCount);
        Assert.Equal(reply.ThreadId, adaMailbox.LatestThreadId);
        Assert.Equal("Re: Launch", adaMailbox.LatestSubject);
        Assert.Equal("Bob", adaMailbox.LatestCounterparty);
    }

    [Fact]
    public void MarkReadAndArchive_UpdateStateAndSummary()
    {
        var runtime = new InMemoryAgentMailboxRuntime();
        var message = runtime.SendMessage("Ada", "Bob", "Please review", subject: "Review");

        Assert.True(runtime.MarkAsRead(message.Id));
        Assert.True(runtime.ArchiveMessage(message.Id));
        Assert.False(runtime.MarkAsRead(message.Id));
        Assert.False(runtime.ArchiveMessage("missing-message"));

        var stored = Assert.Single(runtime.ListThread(message.ThreadId));
        Assert.Equal(AgentMailboxMessageStatus.Archived, stored.Status);
        Assert.NotNull(stored.ReadAt);
        Assert.NotNull(stored.ArchivedAt);

        var bobMailbox = runtime.ListMailboxes().Single(mailbox =>
            string.Equals(mailbox.Participant, "Bob", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, bobMailbox.InboxCount);
        Assert.Equal(0, bobMailbox.UnreadCount);
    }

    [Fact]
    public async Task ListMethodsReturnClonesAndStableOrdering()
    {
        var runtime = new InMemoryAgentMailboxRuntime();
        var older = runtime.SendMessage("Ada", "Bob", "Older body", subject: "Older");
        await Task.Delay(5);
        var newer = runtime.SendMessage("Ada", "Bob", "Newer body", subject: "Newer");

        var inbox = runtime.ListInbox("Bob");
        inbox[0].Subject = "mutated";

        Assert.Equal([older.Id, newer.Id], inbox.Select(message => message.Id));
        Assert.Equal("Older", runtime.GetMessage(older.Id)?.Subject);
        Assert.Equal("Newer", runtime.GetMessage(newer.Id)?.Subject);
    }

    [Fact]
    public void SendMessage_RejectsUnknownReplyTargets()
    {
        var runtime = new InMemoryAgentMailboxRuntime();

        var error = Assert.Throws<InvalidOperationException>(() =>
            runtime.SendMessage("Ada", "Bob", "Reply", replyToMessageId: "missing-message"));

        Assert.Contains("missing-message", error.Message, StringComparison.Ordinal);
    }
}

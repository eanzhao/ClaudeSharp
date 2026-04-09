using ClaudeSharp.Core.Agents;
using ClaudeSharp.Core.Tests.Runtime;
using ClaudeSharp.Core.Tests.Storage;
using ClaudeSharp.Core.Storage;

namespace ClaudeSharp.Core.Tests.Agents;

/// <summary>
/// Contains tests for the agent message runtime.
/// </summary>
public sealed class AgentMessageRuntimeTests
{
    [Fact]
    public void InMemoryRuntime_SendsListsMarksAndSummarizesMessages()
    {
        var runtime = new InMemoryAgentMessageRuntime();

        var first = runtime.SendMessage("Ada", "Bob", "Inspect launch", AgentMessageKind.Note, subject: "Launch");
        var second = runtime.SendMessage("Ada", "Bob", "Follow up", AgentMessageKind.PlanApprovalRequest, subject: "Launch");
        var reply = runtime.SendMessage("Bob", "Ada", AgentMessageKind.PlanApprovalResponse, "Looks good", subject: "Re: Launch", relatedMessageId: first.Id);

        Assert.Equal("agent-message-1", first.Id);
        Assert.Equal(AgentMessageStatus.Delivered, first.Status);
        Assert.Equal(AgentMessageKind.Note, first.Kind);
        Assert.Equal("agent-message-3", reply.Id);
        Assert.Equal(first.ThreadId, reply.ThreadId);
        Assert.NotEqual(first.ThreadId, second.ThreadId);

        Assert.True(runtime.MarkRead(first.Id));
        Assert.True(runtime.MarkMessageRead(second.Id));
        Assert.False(runtime.MarkRead("missing"));

        var messages = runtime.ListMessages();
        Assert.Equal([reply.Id, second.Id, first.Id], messages.Select(message => message.Id));
        Assert.Equal(AgentMessageStatus.Read, Assert.Single(messages, message => message.Id == first.Id).Status);

        var bobInbox = runtime.ListMessages(new AgentMessageListOptions
        {
            Recipient = "Bob",
        });
        Assert.Equal([second.Id, first.Id], bobInbox.Select(message => message.Id));

        var summary = runtime.GetSummary();
        Assert.Equal(3, summary.TotalCount);
        Assert.Equal(2, summary.ReadCount);
        Assert.Equal(1, summary.UnreadCount);
        Assert.Equal(1, summary.UnreadCounts["Ada"]);
        Assert.Equal(3, summary.RecentMessages.Count);

        var overview = AgentMessageFormatter.FormatOverview(messages, runtime.GetUnreadCounts());
        var list = AgentMessageFormatter.FormatList(messages);
        var details = AgentMessageFormatter.FormatDetails(first);
        var summaryText = AgentMessageFormatter.FormatSummary(summary);

        Assert.Contains("Mailbox:", overview, StringComparison.Ordinal);
        Assert.Contains("Unread by recipient:", overview, StringComparison.Ordinal);
        Assert.Contains("Mailbox:", list, StringComparison.Ordinal);
        Assert.Contains("Message: agent-message-1", details, StringComparison.Ordinal);
        Assert.Contains("Mailbox summary:", summaryText, StringComparison.Ordinal);
    }

    [Fact]
    public void InMemoryRuntime_RejectsEmptyInputs()
    {
        var runtime = new InMemoryAgentMessageRuntime();

        Assert.Throws<ArgumentException>(() => runtime.SendMessage("", "Bob", "hello", AgentMessageKind.Note));
        Assert.Throws<ArgumentException>(() => runtime.SendMessage("Alice", " ", "hello", AgentMessageKind.Note));
        Assert.Throws<ArgumentException>(() => runtime.SendMessage("Alice", "Bob", "", AgentMessageKind.Note));
    }

    [Fact]
    public async Task PersistentRuntime_PersistsAndRestoresMessages()
    {
        var journal = new RecordingJournal();
        var runtime = await PersistentAgentMessageRuntime.CreateAsync(journal);

        var first = runtime.SendMessage("Alice", "Bob", "Hello Bob", AgentMessageKind.Note, subject: "Launch");
        var second = runtime.SendMessage("Bob", "Alice", AgentMessageKind.ShutdownRequest, "Hi Alice", subject: "Re: Launch");
        Assert.True(runtime.MarkMessageRead(first.Id));

        Assert.Contains(journal.MetadataEntries, entry => entry.EventType == AgentMessagePersistence.MessageEventType);

        var restored = await PersistentAgentMessageRuntime.CreateAsync(
            new RecordingJournal(),
            journal.MetadataEntries);

        var restoredMessages = restored.ListMessages();
        Assert.Equal(2, restoredMessages.Count);
        Assert.Equal(AgentMessageStatus.Read, Assert.Single(restoredMessages, message => message.Id == first.Id).Status);
        Assert.Equal(AgentMessageStatus.Delivered, Assert.Single(restoredMessages, message => message.Id == second.Id).Status);

        var summary = restored.GetSummary();
        Assert.Equal(2, summary.TotalCount);
        Assert.Equal(1, summary.ReadCount);
        Assert.Equal(1, summary.UnreadCount);
    }

    [Fact]
    public async Task PersistentRuntime_RestoresFromTranscriptStoreProjection()
    {
        using var temp = new TempDirectoryScope(nameof(PersistentRuntime_RestoresFromTranscriptStoreProjection));
        var store = new JsonlTranscriptStore(temp.RootPath);
        var session = await store.CreateSessionAsync("/work/project", "claude-sonnet-4-6");
        var journal = new ConversationJournal(store, session);

        var runtime = await PersistentAgentMessageRuntime.CreateAsync(journal);
        var first = runtime.SendMessage("Alice", "Bob", "Hello Bob", AgentMessageKind.Note, subject: "Launch");
        runtime.MarkMessageRead(first.Id);

        var reloadedSession = await store.FindSessionAsync(session.SessionId);
        Assert.NotNull(reloadedSession);

        var projection = await store.LoadProjectionAsync(
            reloadedSession!,
            new TranscriptLoadOptions());

        var restored = await PersistentAgentMessageRuntime.CreateAsync(
            new RecordingJournal(),
            projection.MetadataEntries);

        var restoredMessage = Assert.Single(restored.ListMessages());
        Assert.Equal(AgentMessageStatus.Read, restoredMessage.Status);
        Assert.Equal("Hello Bob", restoredMessage.Body);
    }
}

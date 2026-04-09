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

        var first = runtime.SendMessage("Alice", "Bob", "direct", "Hello Bob");
        var second = runtime.SendMessage("Bob", "Alice", "reply", "Hi Alice");

        Assert.Equal("message-1", first.Id);
        Assert.Equal(AgentMessageStatus.Delivered, first.Status);
        Assert.Equal("message-2", second.Id);
        Assert.Equal(AgentMessageStatus.Delivered, second.Status);

        Assert.True(runtime.MarkRead(first.Id));
        Assert.False(runtime.MarkRead("missing"));

        var messages = runtime.ListMessages();
        Assert.Equal([first.Id, second.Id], messages.Select(message => message.Id));
        Assert.Equal(AgentMessageStatus.Read, Assert.Single(messages, message => message.Id == first.Id).Status);

        var summary = runtime.GetSummary();
        Assert.Equal(2, summary.TotalCount);
        Assert.Equal(1, summary.DeliveredCount);
        Assert.Equal(1, summary.ReadCount);
        Assert.Collection(summary.RecentMessages, _ => { }, _ => { });

        var participantSummary = runtime.GetSummary(new AgentMessageSummaryOptions
        {
            Participant = "Alice",
            RecentLimit = 1,
        });
        Assert.Equal(2, participantSummary.TotalCount);
        Assert.Single(participantSummary.RecentMessages);

        var overview = AgentMessageFormatter.FormatOverview(messages);
        var details = AgentMessageFormatter.FormatDetails(first);
        var summaryText = AgentMessageFormatter.FormatSummary(summary);

        Assert.Contains("Messages:", overview, StringComparison.Ordinal);
        Assert.Contains("message-1", overview, StringComparison.Ordinal);
        Assert.Contains("Read", overview, StringComparison.Ordinal);
        Assert.Contains("Message: message-1", details, StringComparison.Ordinal);
        Assert.Contains("Body:", details, StringComparison.Ordinal);
        Assert.Contains("Mailbox summary:", summaryText, StringComparison.Ordinal);
        Assert.Contains("Delivered: 1", summaryText, StringComparison.Ordinal);
    }

    [Fact]
    public void InMemoryRuntime_RejectsEmptyInputs()
    {
        var runtime = new InMemoryAgentMessageRuntime();

        Assert.Throws<ArgumentException>(() => runtime.SendMessage("", "Bob", "direct", "hello"));
        Assert.Throws<ArgumentException>(() => runtime.SendMessage("Alice", " ", "direct", "hello"));
        Assert.Throws<ArgumentException>(() => runtime.SendMessage("Alice", "Bob", "", "hello"));
        Assert.Throws<ArgumentException>(() => runtime.SendMessage("Alice", "Bob", "direct", " "));
    }

    [Fact]
    public async Task PersistentRuntime_PersistsAndRestoresMessages()
    {
        var journal = new RecordingJournal();
        var runtime = await PersistentAgentMessageRuntime.CreateAsync(journal);

        var first = runtime.SendMessage("Alice", "Bob", "direct", "Hello Bob");
        var second = runtime.SendMessage("Bob", "Alice", "reply", "Hi Alice");
        Assert.True(runtime.MarkRead(first.Id));

        Assert.Contains(journal.MetadataEntries, entry => entry.EventType == AgentMessagePersistence.MessageEventType);
        Assert.Contains(journal.MetadataEntries, entry => entry.EventType == AgentMessagePersistence.MessageStatusEventType);

        var restored = await PersistentAgentMessageRuntime.CreateAsync(
            new RecordingJournal(),
            journal.MetadataEntries);

        var restoredMessages = restored.ListMessages();
        Assert.Equal(2, restoredMessages.Count);
        Assert.Equal(AgentMessageStatus.Read, Assert.Single(restoredMessages, message => message.Id == first.Id).Status);
        Assert.Equal(AgentMessageStatus.Delivered, Assert.Single(restoredMessages, message => message.Id == second.Id).Status);

        var summary = restored.GetSummary(new AgentMessageSummaryOptions
        {
            Participant = "Alice",
        });
        Assert.Equal(2, summary.TotalCount);
        Assert.Equal(1, summary.ReadCount);
        Assert.Equal(1, summary.DeliveredCount);
    }

    [Fact]
    public async Task PersistentRuntime_RestoresFromTranscriptStoreProjection()
    {
        using var temp = new TempDirectoryScope(nameof(PersistentRuntime_RestoresFromTranscriptStoreProjection));
        var store = new JsonlTranscriptStore(temp.RootPath);
        var session = await store.CreateSessionAsync("/work/project", "claude-sonnet-4-6");
        var journal = new ConversationJournal(store, session);

        var runtime = await PersistentAgentMessageRuntime.CreateAsync(journal);
        var first = runtime.SendMessage("Alice", "Bob", "direct", "Hello Bob");
        runtime.MarkRead(first.Id);

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

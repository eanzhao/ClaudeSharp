using Aexon.Core.Compaction;
using Aexon.Core.Messages;
using Aexon.Core.Storage;

namespace Aexon.Core.Tests.Storage;

public sealed class TombstoneMessageTests
{
    [Fact]
    public void TombstoneMessage_HasCorrectType()
    {
        var tombstone = StorageTestData.Tombstone("t-1", "msg-1", "user requested deletion");

        Assert.Equal("tombstone", tombstone.Type);
        Assert.Equal("msg-1", tombstone.DeletedMessageId);
        Assert.Equal("user requested deletion", tombstone.Reason);
    }

    [Fact]
    public void Recovery_ReplaceDeletedMessageWithTombstone()
    {
        var user1 = StorageTestData.UserText("user-1", "hello");
        var assistant1 = StorageTestData.Assistant("assistant-1", new TextBlock("world"));
        var user2 = StorageTestData.UserText("user-2", "bye");
        var tombstone = StorageTestData.Tombstone("tombstone-1", "assistant-1", "redacted");

        var projection = BuildProjection(
            [user1, assistant1, user2, tombstone],
            [null, user1.Id, assistant1.Id, user2.Id],
            currentLeafMessageId: tombstone.Id);

        var recovered = new ConversationRecovery().Recover(projection);

        Assert.Equal(3, recovered.Messages.Count);
        Assert.Equal("user-1", recovered.Messages[0].Id);
        Assert.IsType<TombstoneMessage>(recovered.Messages[1]);
        Assert.Equal("assistant-1", ((TombstoneMessage)recovered.Messages[1]).DeletedMessageId);
        Assert.Equal("user-2", recovered.Messages[2].Id);
    }

    [Fact]
    public void Recovery_TombstoneWithNoMatchingMessage_IsFilteredOut()
    {
        var user1 = StorageTestData.UserText("user-1", "hello");
        var tombstone = StorageTestData.Tombstone("tombstone-1", "nonexistent-id");

        var projection = BuildProjection(
            [user1, tombstone],
            [null, user1.Id],
            currentLeafMessageId: tombstone.Id);

        var recovered = new ConversationRecovery().Recover(projection);

        Assert.Single(recovered.Messages);
        Assert.Equal("user-1", recovered.Messages[0].Id);
    }

    [Fact]
    public void Recovery_MultipleTombstones_ReplaceCorrectMessages()
    {
        var user1 = StorageTestData.UserText("user-1", "first");
        var assistant1 = StorageTestData.Assistant("assistant-1", new TextBlock("reply1"));
        var user2 = StorageTestData.UserText("user-2", "second");
        var assistant2 = StorageTestData.Assistant("assistant-2", new TextBlock("reply2"));
        var tombstone1 = StorageTestData.Tombstone("t-1", "user-1");
        var tombstone2 = StorageTestData.Tombstone("t-2", "assistant-2");

        var projection = BuildProjection(
            [user1, assistant1, user2, assistant2, tombstone1, tombstone2],
            [null, user1.Id, assistant1.Id, user2.Id, assistant2.Id, tombstone1.Id],
            currentLeafMessageId: tombstone2.Id);

        var recovered = new ConversationRecovery().Recover(projection);

        Assert.Equal(4, recovered.Messages.Count);
        Assert.IsType<TombstoneMessage>(recovered.Messages[0]);
        Assert.Equal("user-1", ((TombstoneMessage)recovered.Messages[0]).DeletedMessageId);
        Assert.Equal("assistant-1", recovered.Messages[1].Id);
        Assert.Equal("user-2", recovered.Messages[2].Id);
        Assert.IsType<TombstoneMessage>(recovered.Messages[3]);
        Assert.Equal("assistant-2", ((TombstoneMessage)recovered.Messages[3]).DeletedMessageId);
    }

    [Fact]
    public void Recovery_TombstoneIsPreservedInRecoveredMessages()
    {
        var user1 = StorageTestData.UserText("user-1", "hello");
        var assistant1 = StorageTestData.Assistant("assistant-1", new TextBlock("response"));
        var tombstone = StorageTestData.Tombstone("t-1", "user-1");

        var projection = BuildProjection(
            [user1, assistant1, tombstone],
            [null, user1.Id, assistant1.Id],
            currentLeafMessageId: tombstone.Id);

        var recovered = new ConversationRecovery().Recover(projection);

        Assert.Equal(2, recovered.Messages.Count);
        var tombstoneMsg = Assert.IsType<TombstoneMessage>(recovered.Messages[0]);
        Assert.Equal("user-1", tombstoneMsg.DeletedMessageId);
        Assert.Equal("assistant-1", recovered.Messages[1].Id);
    }

    [Fact]
    public void Compactor_HandlesTombstoneInSummary()
    {
        var messages = new List<ConversationMessage>();
        for (var i = 0; i < 10; i++)
        {
            messages.Add(StorageTestData.UserText($"user-{i}", $"message {i}"));
            messages.Add(StorageTestData.Assistant($"assistant-{i}", new TextBlock($"reply {i}")));
        }

        messages.Insert(5, StorageTestData.Tombstone("t-1", "user-2"));

        var compactor = new HeuristicConversationCompactor();
        var result = compactor.Compact(messages, preserveTailCount: 4);

        Assert.NotNull(result);
        Assert.True(result!.RemovedMessageCount > 0);
        Assert.Equal(result.ActiveMessages.Count, result.RemovedMessageCount == 0 ? messages.Count : messages.Count - result.RemovedMessageCount + 1);
    }

    [Fact]
    public async Task TranscriptStore_RoundTripsTombstoneMessage()
    {
        using var tempDir = new TempDirectoryScope("tombstone");
        var store = new JsonlTranscriptStore(tempDir.RootPath);

        var session = await store.CreateSessionAsync("/work", "test-model");
        var tombstone = StorageTestData.Tombstone("t-1", "msg-to-delete", "test reason");

        await store.AppendMessageAsync(session, tombstone, null);

        var projection = await store.LoadProjectionAsync(session, new TranscriptLoadOptions());

        Assert.Single(projection.MessagesById);
        var recovered = projection.MessagesById["t-1"].Message;
        var tombstoneRecovered = Assert.IsType<TombstoneMessage>(recovered);
        Assert.Equal("msg-to-delete", tombstoneRecovered.DeletedMessageId);
        Assert.Equal("test reason", tombstoneRecovered.Reason);
    }

    [Fact]
    public async Task ConversationJournal_DeleteMessageAsync_AppendsTombstone()
    {
        using var tempDir = new TempDirectoryScope("tombstone-journal");
        var store = new JsonlTranscriptStore(tempDir.RootPath);
        var session = await store.CreateSessionAsync("/work", "test-model");

        var user1 = StorageTestData.UserText("user-1", "hello");
        await store.AppendMessageAsync(session, user1, null);

        var journal = new ConversationJournal(store, session);
        await journal.DeleteMessageAsync("user-1", "/work", "test-model", "test deletion");

        var projection = await store.LoadProjectionAsync(session, new TranscriptLoadOptions());

        Assert.Equal(2, projection.MessagesById.Count);
        var tombstone = projection.MessagesById.Values
            .Select(s => s.Message)
            .OfType<TombstoneMessage>()
            .Single();
        Assert.Equal("user-1", tombstone.DeletedMessageId);
        Assert.Equal("test deletion", tombstone.Reason);
    }

    [Fact]
    public void MicroCompactor_PassesThroughTombstoneUnchanged()
    {
        var messages = new ConversationMessage[]
        {
            StorageTestData.UserText("user-1", "hello"),
            StorageTestData.Tombstone("t-1", "deleted-msg"),
            StorageTestData.Assistant("assistant-1", new TextBlock("response")),
        };

        var compactor = new TimeBasedMicroCompactor();
        var result = compactor.Run(messages, new MicrocompactRunOptions { Force = true });

        Assert.IsType<TombstoneMessage>(result.UpdatedMessages[1]);
        Assert.Equal("t-1", result.UpdatedMessages[1].Id);
    }

    private static TranscriptProjection BuildProjection(
        IReadOnlyList<ConversationMessage> messages,
        IReadOnlyList<string?> parentMessageIds,
        string? currentLeafMessageId,
        IReadOnlyList<TranscriptMetadataEntry>? metadataEntries = null)
    {
        var session = new TranscriptSession
        {
            SessionId = "session-1",
            SessionDirectory = "/tmp/session-1",
            TranscriptPath = "/tmp/session-1/transcript.jsonl",
            ManifestPath = "/tmp/session-1/manifest.json",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            WorkingDirectory = "/work/project",
            Model = "sonnet",
            CurrentLeafMessageId = currentLeafMessageId,
            Metadata = new ConversationSessionMetadata(),
        };

        var projectedMessages = messages
            .Select((message, index) => new StoredTranscriptMessage(
                message,
                parentMessageIds[index],
                index + 1))
            .ToDictionary(message => message.Message.Id, message => message, StringComparer.Ordinal);

        return new TranscriptProjection
        {
            Session = session,
            MessagesById = projectedMessages,
            MetadataEntries = metadataEntries ?? [],
        };
    }
}

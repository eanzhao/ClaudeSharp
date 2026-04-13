using Aexon.Core.Compaction;
using Aexon.Core.Messages;
using Aexon.Core.Permissions;
using Aexon.Core.Storage;

namespace Aexon.Core.Tests.Storage;

/// <summary>
/// Contains tests for transcript Store.
/// </summary>
public sealed class TranscriptStoreTests
{
    [Fact]
    public async Task JournalCheckpointAndMicrocompact_RoundTripThroughLoadAndRecover()
    {
        using var temp = new TempDirectoryScope(nameof(JournalCheckpointAndMicrocompact_RoundTripThroughLoadAndRecover));
        var store = new JsonlTranscriptStore(temp.RootPath);
        var session = await store.CreateSessionAsync("/work/project", "sonnet");
        var journal = new ConversationJournal(store, session);

        var firstUser = StorageTestData.ToolResult(
            "user-1",
            "tool-1",
            "raw tool output");
        var firstAssistant = StorageTestData.ThinkingAssistant(
            "assistant-1",
            "original thinking",
            "sig-1");
        firstAssistant = firstAssistant with
        {
            Usage = new TokenUsage
            {
                InputTokens = 7,
                OutputTokens = 11,
            },
        };
        var whitespaceAssistant = StorageTestData.Assistant(
            "assistant-blank",
            new TextBlock("   "));
        var tailUser = StorageTestData.UserText("user-2", "tail context");

        await journal.AppendMessageAsync(firstUser, "/work/project", "sonnet");
        await journal.AppendMessageAsync(firstAssistant, "/work/project", "sonnet");
        await journal.RecordMicrocompactAsync(
            [
                new MicrocompactEdit
                {
                    MessageId = firstUser.Id,
                    ClearToolResult = true,
                },
                new MicrocompactEdit
                {
                    MessageId = firstAssistant.Id,
                    ClearThinking = true,
                },
            ],
            "/work/project",
            "sonnet");

        var summary = StorageTestData.UserText("summary-1", "summary checkpoint");
        await journal.RecordConversationCheckpointAsync(
            summary,
            [firstUser, firstAssistant],
            "/work/project",
            "sonnet");
        await journal.AppendMessageAsync(whitespaceAssistant, "/work/project", "sonnet");
        await journal.AppendMessageAsync(tailUser, "/work/project", "sonnet");

        var reloaded = await store.FindSessionAsync(session.SessionDirectory);
        Assert.NotNull(reloaded);

        var projection = await store.LoadProjectionAsync(reloaded!, new TranscriptLoadOptions());
        var recovery = new ConversationRecovery();
        var resumed = recovery.Recover(projection);

        Assert.Equal(["user-1", "assistant-1", "user-2"], resumed.Messages.Select(message => message.Id));

        var resumedUser = Assert.IsType<UserMessage>(resumed.Messages[0]);
        var resumedUserResult = Assert.Single(resumedUser.Content.OfType<ToolResultBlock>());
        Assert.Equal(MicrocompactPlaceholders.OldToolResult, resumedUserResult.Content);

        var resumedAssistant = Assert.IsType<AssistantMessage>(resumed.Messages[1]);
        var resumedThinking = Assert.Single(resumedAssistant.Content.OfType<ThinkingBlock>());
        Assert.Equal(MicrocompactPlaceholders.OldThinking, resumedThinking.Text);

        var resumedTail = Assert.IsType<UserMessage>(resumed.Messages[2]);
        Assert.Equal("tail context", Assert.Single(resumedTail.Content.OfType<TextBlock>()).Text);

        Assert.Equal(firstAssistant.Usage, resumed.TotalUsage);
        Assert.Equal("summary-1", projection.MessagesById["summary-1"].Message.Id);
        Assert.Contains(projection.MetadataEntries, entry => entry.EventType == ConversationCheckpointRecordName);
        Assert.Contains(projection.MetadataEntries, entry => entry.EventType == MicrocompactRecord.EventType);
    }

    [Fact]
    public async Task MetadataUpdatesRoundTripThroughManifestAndProjection()
    {
        using var temp = new TempDirectoryScope(nameof(MetadataUpdatesRoundTripThroughManifestAndProjection));
        var store = new JsonlTranscriptStore(temp.RootPath);
        var session = await store.CreateSessionAsync("/work/project", "sonnet");
        var journal = new ConversationJournal(store, session);

        await journal.UpdateMetadataAsync(metadata =>
        {
            metadata.Title = "Session Alpha";
            metadata.Mode = PermissionMode.Auto;
            metadata.Tags.Add("one");
            metadata.Tags.Add("two");
        });

        await journal.UpdateMetadataAsync(metadata =>
        {
            metadata.Title = null;
            metadata.Tags.Remove("two");
        });

        var reloaded = await store.FindSessionAsync(session.SessionDirectory);
        Assert.NotNull(reloaded);
        Assert.Null(reloaded!.Metadata.Title);
        Assert.Equal(PermissionMode.Auto, reloaded.Metadata.Mode);
        Assert.Equal(["one"], reloaded.Metadata.Tags.OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase));

        var projection = await store.LoadProjectionAsync(reloaded, new TranscriptLoadOptions());
        Assert.Equal(reloaded.Metadata.Title, projection.Session.Metadata.Title);
        Assert.Equal(reloaded.Metadata.Mode, projection.Session.Metadata.Mode);
        Assert.Equal(
            reloaded.Metadata.Tags.OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase),
            projection.Session.Metadata.Tags.OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase));

        Assert.Equal(
            [
                "custom-title",
                "mode",
                "tag-add",
                "tag-add",
                "custom-title",
                "tag-remove",
            ],
            projection.MetadataEntries.Select(entry => entry.EventType));
    }

    [Fact]
    public async Task ResetHeadClearsTheActiveChainOnResume()
    {
        using var temp = new TempDirectoryScope(nameof(ResetHeadClearsTheActiveChainOnResume));
        var store = new JsonlTranscriptStore(temp.RootPath);
        var session = await store.CreateSessionAsync("/work/project", "sonnet");
        var journal = new ConversationJournal(store, session);

        await journal.AppendMessageAsync(StorageTestData.UserText("user-1", "before reset"), "/work/project", "sonnet");
        await journal.AppendMessageAsync(StorageTestData.Assistant("assistant-1", new TextBlock("reply")), "/work/project", "sonnet");
        await journal.ResetHeadAsync();
        await journal.AppendMessageAsync(StorageTestData.UserText("user-2", "after reset"), "/work/project", "sonnet");

        var reloaded = await store.FindSessionAsync(session.SessionDirectory);
        Assert.NotNull(reloaded);
        Assert.Equal("user-2", reloaded!.CurrentLeafMessageId);

        var projection = await store.LoadProjectionAsync(reloaded, new TranscriptLoadOptions());
        var resumed = new ConversationRecovery().Recover(projection);

        Assert.Equal(["user-2"], resumed.Messages.Select(message => message.Id));
        Assert.Contains(projection.MetadataEntries, entry => entry.EventType == "reset-head");
    }

    [Fact]
    public async Task FindSessionAndLatestSessionResolveTheExpectedManifest()
    {
        using var temp = new TempDirectoryScope(nameof(FindSessionAndLatestSessionResolveTheExpectedManifest));
        var store = new JsonlTranscriptStore(temp.RootPath);

        var older = await store.CreateSessionAsync("/work/older", "sonnet");
        await Task.Delay(25);
        var newer = await store.CreateSessionAsync("/work/newer", "opus");

        var byDirectory = await store.FindSessionAsync(newer.SessionDirectory);
        var byManifest = await store.FindSessionAsync(newer.ManifestPath);
        var byTranscript = await store.FindSessionAsync(newer.TranscriptPath);
        var latest = await store.GetLatestSessionAsync();

        Assert.NotNull(byDirectory);
        Assert.NotNull(byManifest);
        Assert.NotNull(byTranscript);
        Assert.NotNull(latest);

        Assert.Equal(newer.SessionId, byDirectory!.SessionId);
        Assert.Equal(newer.SessionId, byManifest!.SessionId);
        Assert.Equal(newer.SessionId, byTranscript!.SessionId);
        Assert.Equal(newer.SessionId, latest!.SessionId);
        Assert.NotEqual(older.SessionId, latest.SessionId);
    }

    [Fact]
    public async Task LoadProjectionIgnoresMalformedTranscriptLines()
    {
        using var temp = new TempDirectoryScope(nameof(LoadProjectionIgnoresMalformedTranscriptLines));
        var store = new JsonlTranscriptStore(temp.RootPath);
        var session = await store.CreateSessionAsync("/work/project", "sonnet");

        var first = StorageTestData.UserText("user-1", "hello");
        var second = StorageTestData.UserText("user-2", "world");
        await store.AppendMessageAsync(session, first, null);
        await File.AppendAllTextAsync(session.TranscriptPath, "not-json-at-all" + Environment.NewLine);
        await store.AppendMessageAsync(session, second, first.Id);

        var reloaded = await store.FindSessionAsync(session.SessionDirectory);
        Assert.NotNull(reloaded);

        var projection = await store.LoadProjectionAsync(reloaded!, new TranscriptLoadOptions());

        Assert.Equal(["user-1", "user-2"], projection.MessagesById.Values.OrderBy(message => message.Sequence).Select(message => message.Message.Id));
    }

    private const string ConversationCheckpointRecordName = "conversation-checkpoint";
}

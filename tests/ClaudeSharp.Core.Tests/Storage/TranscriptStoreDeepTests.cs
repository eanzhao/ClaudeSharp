using System.Text.Json;
using ClaudeSharp.Core.Compaction;
using ClaudeSharp.Core.Messages;
using ClaudeSharp.Core.Permissions;
using ClaudeSharp.Core.Storage;

namespace ClaudeSharp.Core.Tests.Storage;

/// <summary>
/// Contains tests for transcript Store Deep.
/// </summary>
public sealed class TranscriptStoreDeepTests
{
    [Fact]
    public async Task CreateSessionAndLookupReturnExpectedResults()
    {
        using var temp = new TempDirectoryScope(nameof(CreateSessionAndLookupReturnExpectedResults));
        var store = new JsonlTranscriptStore(temp.RootPath);

        Assert.Null(await store.GetLatestSessionAsync());
        Assert.Null(await store.FindSessionAsync(string.Empty));
        Assert.Null(await store.FindSessionAsync("missing-session"));

        var session = await store.CreateSessionAsync("/work/project", "sonnet");

        Assert.True(File.Exists(session.TranscriptPath));
        Assert.True(File.Exists(session.ManifestPath));

        var latest = await store.GetLatestSessionAsync();
        Assert.NotNull(latest);
        Assert.Equal(session.SessionId, latest!.SessionId);
        Assert.Equal(session.SessionDirectory, latest.SessionDirectory);

        var byTranscript = await store.FindSessionAsync(session.TranscriptPath);
        Assert.NotNull(byTranscript);
        Assert.Equal(session.SessionId, byTranscript!.SessionId);
    }

    [Fact]
    public async Task LoadProjection_UsesLatestDuplicateMessageAndSkipsMalformedRecords()
    {
        using var temp = new TempDirectoryScope(nameof(LoadProjection_UsesLatestDuplicateMessageAndSkipsMalformedRecords));
        var store = new JsonlTranscriptStore(temp.RootPath);
        var session = await store.CreateSessionAsync("/work/project", "sonnet");

        var first = StorageTestData.UserText("dup", "first");
        var second = StorageTestData.UserText("dup", "second");
        var system = StorageTestData.System("sys-1", "system prompt");
        var assistant = StorageTestData.Assistant(
            "assistant-1",
            new ToolUseBlock
            {
                ToolUseId = "tool-1",
                Name = "search",
                Input = JsonSerializer.SerializeToElement(new { query = "x" }),
            },
            new TextBlock("done"));

        await store.AppendMessageAsync(session, first, null);
        await File.AppendAllTextAsync(session.TranscriptPath, """{"RecordType":"message"}""" + Environment.NewLine);
        await File.AppendAllTextAsync(session.TranscriptPath, """{"RecordType":"unknown"}""" + Environment.NewLine);
        await store.AppendMessageAsync(session, second, first.Id);
        await store.AppendMessageAsync(session, system, second.Id);
        await store.AppendMessageAsync(session, assistant, system.Id);
        await File.AppendAllTextAsync(session.TranscriptPath, """{"RecordType":"metadata"}""" + Environment.NewLine);

        var reloaded = await store.FindSessionAsync(session.SessionDirectory);
        Assert.NotNull(reloaded);

        var projection = await store.LoadProjectionAsync(reloaded!, new TranscriptLoadOptions());

        Assert.Equal(["dup", "sys-1", "assistant-1"], projection.MessagesById.Values
            .OrderBy(message => message.Sequence)
            .Select(message => message.Message.Id));

        var duplicate = Assert.IsType<UserMessage>(projection.MessagesById["dup"].Message);
        Assert.Equal("second", Assert.Single(duplicate.Content.OfType<TextBlock>()).Text);

        var systemMessage = Assert.IsType<SystemMessage>(projection.MessagesById["sys-1"].Message);
        Assert.Equal("system prompt", systemMessage.Content);

        var assistantMessage = Assert.IsType<AssistantMessage>(projection.MessagesById["assistant-1"].Message);
        var toolUse = Assert.Single(assistantMessage.Content.OfType<ToolUseBlock>());
        Assert.Equal("tool-1", toolUse.ToolUseId);
        Assert.Equal("search", toolUse.Name);
        Assert.Contains(projection.MessagesById.Values, message => message.Message.Id == "assistant-1");
    }

    [Fact]
    public async Task AppendMetadataAppliesDeltaAndIgnoresInvalidValues()
    {
        using var temp = new TempDirectoryScope(nameof(AppendMetadataAppliesDeltaAndIgnoresInvalidValues));
        var store = new JsonlTranscriptStore(temp.RootPath);
        var session = await store.CreateSessionAsync("/work/project", "sonnet");

        await store.AppendMetadataAsync(
            session,
            new TranscriptMetadataEntry("custom-title", JsonSerializer.SerializeToElement(new { title = "Alpha" })));
        await store.AppendMetadataAsync(
            session,
            new TranscriptMetadataEntry("mode", JsonSerializer.SerializeToElement(new { mode = PermissionMode.Auto.ToString() })));
        await store.AppendMetadataAsync(
            session,
            new TranscriptMetadataEntry("tag-add", JsonSerializer.SerializeToElement(new { tag = "one" })));
        await store.AppendMetadataAsync(
            session,
            new TranscriptMetadataEntry("custom-title", JsonSerializer.SerializeToElement(new { title = (string?)null })));
        await store.AppendMetadataAsync(
            session,
            new TranscriptMetadataEntry("mode", JsonSerializer.SerializeToElement(new { mode = "not-a-mode" })));
        await store.AppendMetadataAsync(
            session,
            new TranscriptMetadataEntry("tag-add", JsonSerializer.SerializeToElement(new { tag = "   " })));
        await store.AppendMetadataAsync(
            session,
            new TranscriptMetadataEntry("tag-remove", JsonSerializer.SerializeToElement(new { tag = "   " })));

        var reloaded = await store.FindSessionAsync(session.SessionDirectory);
        Assert.NotNull(reloaded);
        Assert.Null(reloaded!.Metadata.Title);
        Assert.Null(reloaded.Metadata.Mode);
        Assert.Equal(["one"], reloaded.Metadata.Tags);

        var projection = await store.LoadProjectionAsync(reloaded, new TranscriptLoadOptions());
        Assert.Null(projection.Session.Metadata.Title);
        Assert.Null(projection.Session.Metadata.Mode);
        Assert.Equal(["one"], projection.Session.Metadata.Tags);
        Assert.Contains(projection.MetadataEntries, entry => entry.EventType == "custom-title");
        Assert.Contains(projection.MetadataEntries, entry => entry.EventType == "mode");
        Assert.Contains(projection.MetadataEntries, entry => entry.EventType == "tag-add");
    }

    [Fact]
    public async Task SeedAsyncWritesMessagesAndSessionMetadata()
    {
        using var temp = new TempDirectoryScope(nameof(SeedAsyncWritesMessagesAndSessionMetadata));
        var store = new JsonlTranscriptStore(temp.RootPath);
        var session = await store.CreateSessionAsync("/work/old", "sonnet");
        var journal = new ConversationJournal(store, session);

        var metadata = new ConversationSessionMetadata
        {
            Title = "Seeded session",
            Mode = PermissionMode.Plan,
        };
        metadata.Tags.Add("alpha");

        var user = StorageTestData.UserText("user-1", "hello");
        var assistant = StorageTestData.Assistant("assistant-1", new TextBlock("world"));

        await journal.SeedAsync(
            [user, assistant],
            metadata,
            "/work/new",
            "opus");

        var reloaded = await store.FindSessionAsync(session.SessionDirectory);
        Assert.NotNull(reloaded);
        Assert.Equal("/work/new", reloaded!.WorkingDirectory);
        Assert.Equal("opus", reloaded.Model);
        Assert.Equal("assistant-1", reloaded.CurrentLeafMessageId);
        Assert.Equal("Seeded session", reloaded.Metadata.Title);
        Assert.Equal(PermissionMode.Plan, reloaded.Metadata.Mode);
        Assert.Equal(["alpha"], reloaded.Metadata.Tags);

        var projection = await store.LoadProjectionAsync(reloaded, new TranscriptLoadOptions());
        Assert.Equal(["user-1", "assistant-1"], projection.MessagesById.Values
            .OrderBy(message => message.Sequence)
            .Select(message => message.Message.Id));
    }

    [Fact]
    public async Task UpdateSessionInfoAndEmptyMicrocompactUpdateTheManifestOnly()
    {
        using var temp = new TempDirectoryScope(nameof(UpdateSessionInfoAndEmptyMicrocompactUpdateTheManifestOnly));
        var store = new JsonlTranscriptStore(temp.RootPath);
        var session = await store.CreateSessionAsync("/work/project", "sonnet");
        var journal = new ConversationJournal(store, session);

        await journal.UpdateSessionInfoAsync("/work/updated", "opus");
        await journal.RecordMicrocompactAsync([], "/work/updated", "opus");

        var reloaded = await store.FindSessionAsync(session.SessionDirectory);
        Assert.NotNull(reloaded);
        Assert.Equal("/work/updated", reloaded!.WorkingDirectory);
        Assert.Equal("opus", reloaded.Model);

        var projection = await store.LoadProjectionAsync(reloaded, new TranscriptLoadOptions());
        Assert.Empty(projection.MetadataEntries);
        Assert.Empty(projection.MessagesById);
    }

    [Fact]
    public async Task RecordConversationCheckpointRejectsEmptyActiveMessageList()
    {
        using var temp = new TempDirectoryScope(nameof(RecordConversationCheckpointRejectsEmptyActiveMessageList));
        var store = new JsonlTranscriptStore(temp.RootPath);
        var session = await store.CreateSessionAsync("/work/project", "sonnet");
        var journal = new ConversationJournal(store, session);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            journal.RecordConversationCheckpointAsync(
                StorageTestData.UserText("summary-1", "summary"),
                [],
                "/work/project",
                "sonnet"));
    }

    [Fact]
    public async Task FindSessionParsesManifestMetadataAndRejectsMalformedManifest()
    {
        using var temp = new TempDirectoryScope(nameof(FindSessionParsesManifestMetadataAndRejectsMalformedManifest));
        var store = new JsonlTranscriptStore(temp.RootPath);
        var session = await store.CreateSessionAsync("/work/project", "sonnet");

        var malformedManifest = JsonSerializer.Serialize(new
        {
            SessionId = session.SessionId,
            CreatedAt = session.CreatedAt,
            UpdatedAt = session.UpdatedAt,
            WorkingDirectory = "/work/project",
            Model = "sonnet",
            Title = "Imported",
            Tags = (string[]?)null,
            Mode = "not-a-mode",
            CurrentLeafMessageId = (string?)null,
        });

        await File.WriteAllTextAsync(session.ManifestPath, malformedManifest);

        var reloaded = await store.FindSessionAsync(session.SessionDirectory);
        Assert.NotNull(reloaded);
        Assert.Equal(session.SessionId, reloaded!.SessionId);
        Assert.Equal("Imported", reloaded.Metadata.Title);
        Assert.Null(reloaded.Metadata.Mode);
        Assert.Empty(reloaded.Metadata.Tags);

        await File.WriteAllTextAsync(session.ManifestPath, "{broken");
        Assert.Null(await store.FindSessionAsync(session.SessionDirectory));
    }
}

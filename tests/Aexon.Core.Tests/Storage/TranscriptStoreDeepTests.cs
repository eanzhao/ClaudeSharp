using System.Text.Json;
using Aexon.Core.Compaction;
using Aexon.Core.Messages;
using Aexon.Core.Permissions;
using Aexon.Core.Storage;

namespace Aexon.Core.Tests.Storage;

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
    public async Task LoadProjection_RoundTripsStructuredSystemMessages()
    {
        using var temp = new TempDirectoryScope(nameof(LoadProjection_RoundTripsStructuredSystemMessages));
        var store = new JsonlTranscriptStore(temp.RootPath);
        var session = await store.CreateSessionAsync("/work/project", "sonnet");

        var messages = new ConversationMessage[]
        {
            new SystemCompactBoundaryMessage
            {
                Id = "compact-1",
                Content = "conversation compacted",
                BoundaryId = "boundary-1",
                Mode = "compact",
                Reason = "manual",
                Automatic = true,
                FoldedMessageCount = 5,
                PreservedMessageCount = 8,
                SummaryMessageId = "summary-1",
            },
            new SystemMicrocompactBoundaryMessage
            {
                Id = "microcompact-1",
                Content = "microcompact applied",
                BoundaryId = "boundary-2",
                Reason = "cache cooldown elapsed",
                Automatic = true,
                ClearedToolResultCount = 2,
                ClearedThinkingBlockCount = 1,
            },
            new SystemPermissionRetryMessage
            {
                Id = "permission-1",
                Content = "retrying tool after permission update",
                ToolName = "Bash",
                Attempt = 2,
                Reason = "user approved with narrower scope",
                UpdatedInput = JsonSerializer.SerializeToElement(new { command = "git status" }),
            },
            new SystemStopHookSummaryMessage
            {
                Id = "hook-1",
                Content = "stop hook completed",
                HookEvent = "stop",
                Success = true,
                DurationMs = 42,
                Summary = "all cleanup hooks succeeded",
            },
            new SystemTurnDurationMessage
            {
                Id = "turn-1",
                Content = "turn completed",
                TurnCount = 3,
                DurationMs = 1234,
                Model = "sonnet",
            },
            new SystemLocalCommandMessage
            {
                Id = "command-1",
                Content = "local command executed",
                Command = "dotnet test",
                WorkingDirectory = "/work/project",
                ExitCode = 0,
            },
            new SystemApiMetricsMessage
            {
                Id = "api-1",
                Content = "api metrics captured",
                Model = "sonnet",
                Usage = new TokenUsage
                {
                    InputTokens = 10,
                    OutputTokens = 20,
                    CacheReadInputTokens = 3,
                    CacheCreationInputTokens = 4,
                },
                DurationMs = 321,
                StopReason = "end_turn",
                Success = true,
            },
            new SystemMemorySavedMessage
            {
                Id = "memory-1",
                Content = "session memory saved",
                MemoryKind = "session",
                FilePath = "/tmp/session-memory.md",
                CharacterCount = 512,
            },
            new SystemAgentsKilledMessage
            {
                Id = "agents-1",
                Content = "agents stopped",
                AgentIds = ["agent-a", "agent-b"],
                Reason = "user requested shutdown",
            },
            new SystemAwaySummaryMessage
            {
                Id = "away-1",
                Content = "away summary",
                AwayDurationMs = 60000,
                Summary = "resumed after one minute",
            },
            new SystemScheduledTaskFireMessage
            {
                Id = "task-1",
                Content = "scheduled task fired",
                TaskName = "daily-refresh",
                ScheduledAt = DateTimeOffset.Parse("2026-04-14T08:00:00+08:00"),
                FiredAt = DateTimeOffset.Parse("2026-04-14T08:00:05+08:00"),
                Result = "completed",
            },
            new SystemBridgeStatusMessage
            {
                Id = "bridge-1",
                Content = "bridge connected",
                BridgeName = "mcp",
                Status = "connected",
                Detail = "stdio transport ready",
            },
            new ProgressMessage
            {
                Id = "progress-1",
                Content = "uploading artifact",
                Stage = "upload",
                Status = "running",
                Percent = 75,
            },
            new ToolUseSummaryMessage
            {
                Id = "tool-summary-1",
                Content = "tool run completed",
                ToolUseId = "tool-123",
                ToolName = "FileWrite",
                ResultPreview = "wrote 3 lines",
            },
            new AttachmentMessage
            {
                Id = "attachment-1",
                Content = "attached diff",
                AttachmentId = "att-1",
                AttachmentName = "patch.diff",
                MediaType = "text/x-diff",
                SourcePath = "/tmp/patch.diff",
                SizeBytes = 128,
            },
            new TombstoneMessage
            {
                Id = "tombstone-1",
                DeletedMessageId = "assistant-9",
                Reason = "redacted",
            },
            new SystemMessage
            {
                Id = "legacy-1",
                Content = "legacy system marker",
                Subtype = "legacy_marker",
            },
        };

        string? parentMessageId = null;
        foreach (var message in messages)
        {
            await store.AppendMessageAsync(session, message, parentMessageId);
            parentMessageId = message.Id;
        }

        var reloaded = await store.FindSessionAsync(session.SessionDirectory);
        Assert.NotNull(reloaded);

        var projection = await store.LoadProjectionAsync(reloaded!, new TranscriptLoadOptions());

        Assert.Equal(
            messages.Select(message => message.Id),
            projection.MessagesById.Values
                .OrderBy(message => message.Sequence)
                .Select(message => message.Message.Id));

        var compact = Assert.IsType<SystemCompactBoundaryMessage>(projection.MessagesById["compact-1"].Message);
        Assert.Equal("boundary-1", compact.BoundaryId);
        Assert.Equal(5, compact.FoldedMessageCount);

        var microcompact = Assert.IsType<SystemMicrocompactBoundaryMessage>(projection.MessagesById["microcompact-1"].Message);
        Assert.Equal(2, microcompact.ClearedToolResultCount);
        Assert.Equal(1, microcompact.ClearedThinkingBlockCount);

        var permission = Assert.IsType<SystemPermissionRetryMessage>(projection.MessagesById["permission-1"].Message);
        Assert.Equal("Bash", permission.ToolName);
        Assert.Equal("git status", permission.UpdatedInput?.GetProperty("command").GetString());

        var hook = Assert.IsType<SystemStopHookSummaryMessage>(projection.MessagesById["hook-1"].Message);
        Assert.True(hook.Success);
        Assert.Equal("all cleanup hooks succeeded", hook.Summary);

        var turn = Assert.IsType<SystemTurnDurationMessage>(projection.MessagesById["turn-1"].Message);
        Assert.Equal(3, turn.TurnCount);
        Assert.Equal("sonnet", turn.Model);

        var command = Assert.IsType<SystemLocalCommandMessage>(projection.MessagesById["command-1"].Message);
        Assert.Equal("dotnet test", command.Command);
        Assert.Equal(0, command.ExitCode);

        var api = Assert.IsType<SystemApiMetricsMessage>(projection.MessagesById["api-1"].Message);
        Assert.NotNull(api.Usage);
        Assert.Equal(37, api.Usage!.TotalTokens);

        var memory = Assert.IsType<SystemMemorySavedMessage>(projection.MessagesById["memory-1"].Message);
        Assert.Equal("/tmp/session-memory.md", memory.FilePath);
        Assert.Equal(512, memory.CharacterCount);

        var agents = Assert.IsType<SystemAgentsKilledMessage>(projection.MessagesById["agents-1"].Message);
        Assert.Equal(["agent-a", "agent-b"], agents.AgentIds);

        var away = Assert.IsType<SystemAwaySummaryMessage>(projection.MessagesById["away-1"].Message);
        Assert.Equal(60000, away.AwayDurationMs);

        var task = Assert.IsType<SystemScheduledTaskFireMessage>(projection.MessagesById["task-1"].Message);
        Assert.Equal("daily-refresh", task.TaskName);
        Assert.Equal(DateTimeOffset.Parse("2026-04-14T08:00:05+08:00"), task.FiredAt);

        var bridge = Assert.IsType<SystemBridgeStatusMessage>(projection.MessagesById["bridge-1"].Message);
        Assert.Equal("connected", bridge.Status);

        var progress = Assert.IsType<ProgressMessage>(projection.MessagesById["progress-1"].Message);
        Assert.Equal(75, progress.Percent);

        var toolSummary = Assert.IsType<ToolUseSummaryMessage>(projection.MessagesById["tool-summary-1"].Message);
        Assert.Equal("tool-123", toolSummary.ToolUseId);
        Assert.Equal("wrote 3 lines", toolSummary.ResultPreview);

        var attachment = Assert.IsType<AttachmentMessage>(projection.MessagesById["attachment-1"].Message);
        Assert.Equal("patch.diff", attachment.AttachmentName);
        Assert.Equal(128, attachment.SizeBytes);

        var tombstone = Assert.IsType<TombstoneMessage>(projection.MessagesById["tombstone-1"].Message);
        Assert.Equal("assistant-9", tombstone.DeletedMessageId);

        var legacy = Assert.IsType<SystemMessage>(projection.MessagesById["legacy-1"].Message);
        Assert.Equal("legacy_marker", legacy.Subtype);
        Assert.Equal("legacy system marker", legacy.Content);
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

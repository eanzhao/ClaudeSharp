using System.Net;
using Aexon.Cli;
using Aexon.Core.Context;
using Aexon.Core.Messages;
using Aexon.Core.Permissions;
using Aexon.Core.Query;
using Aexon.Core.Storage;
using Aexon.Core.Tools;

namespace Aexon.Core.Tests.Runtime;

/// <summary>
/// Contains tests for query Engine.
/// </summary>
public class QueryEngineTests
{
    [Fact]
    public async Task SessionMetadata_ApiUpdatesTitleTagsAndMode()
    {
        using var temp = new TempDirectory();
        var journal = new RecordingJournal();
        var engine = CreateEngine(
            temp.Root,
            journal,
            initialMessages: [],
            config: new QueryEngineConfig { Model = ClaudeModels.DefaultMainModel });

        await engine.SetSessionTitleAsync("alpha");
        await engine.AddSessionTagAsync("one");
        await engine.AddSessionTagAsync("two");
        await engine.RemoveSessionTagAsync("one");
        await engine.SetPermissionModeAsync(PermissionMode.Plan);

        var metadata = engine.SessionMetadata;
        Assert.Equal("alpha", metadata.Title);
        Assert.Equal(PermissionMode.Plan, metadata.Mode);
        Assert.Equal(["two"], metadata.Tags.OrderBy(tag => tag));
        Assert.Equal("alpha", journal.Metadata.Title);
        Assert.Equal(PermissionMode.Plan, journal.Metadata.Mode);
        Assert.Equal(["two"], journal.Metadata.Tags.OrderBy(tag => tag));
    }

    [Fact]
    public async Task EnterAndExitPlanModeAsync_RestoresPreviousPermissionMode()
    {
        using var temp = new TempDirectory();
        var journal = new RecordingJournal();
        var engine = CreateEngine(temp.Root, journal);

        await engine.SetPermissionModeAsync(PermissionMode.Auto);

        var entered = await engine.EnterPlanModeAsync();
        var restoredMode = await engine.ExitPlanModeAsync();

        Assert.True(entered);
        Assert.Equal(PermissionMode.Auto, restoredMode);
        Assert.False(engine.IsPlanModeActive);
        Assert.Equal(PermissionMode.Auto, engine.SessionMetadata.Mode);
        Assert.Equal(PermissionMode.Auto, journal.Metadata.Mode);
    }

    [Fact]
    public async Task CompactAsync_CreatesCheckpointAndReplacesActiveMessages()
    {
        using var temp = new TempDirectory();
        var journal = new RecordingJournal();
        var initialMessages = new ConversationMessage[]
        {
            UserMessage.FromText("first"),
            new AssistantMessage
            {
                Content = [new TextBlock("second")],
            },
            UserMessage.FromText("third"),
            new AssistantMessage
            {
                Content = [new TextBlock("fourth")],
            },
        };

        var engine = CreateEngine(
            temp.Root,
            journal,
            initialMessages: initialMessages);

        var result = await engine.CompactAsync(preserveTailCount: 2);

        Assert.NotNull(result);
        Assert.Equal(3, engine.Messages.Count);
        var summary = Assert.IsType<UserMessage>(engine.Messages[0]);
        Assert.True(summary.IsMeta);
        Assert.Contains("Conversation summary before compaction", Assert.IsType<TextBlock>(summary.Content[0]).Text);
        Assert.Equal(1, journal.CheckpointCount);
        Assert.NotNull(journal.LastCheckpointSummary);
        Assert.Equal(3, journal.LastCheckpointActiveMessages?.Count);
    }

    [Fact]
    public async Task MicrocompactAsync_ClearsOldThinkingAndToolResults()
    {
        using var temp = new TempDirectory();
        var journal = new RecordingJournal();
        var initialMessages = new ConversationMessage[]
        {
            UserMessage.FromToolResult("tool-1", "tool output"),
            new AssistantMessage
            {
                Content = [new ThinkingBlock("thinking"), new TextBlock("assistant text")],
            },
            UserMessage.FromText("tail"),
        };

        var engine = CreateEngine(
            temp.Root,
            journal,
            initialMessages: initialMessages);

        var result = await engine.MicrocompactAsync(preserveTailCount: 1);

        Assert.NotNull(result);
        Assert.Equal(1, journal.MicrocompactCount);
        var user = Assert.IsType<UserMessage>(engine.Messages[0]);
        Assert.Contains(MicrocompactPlaceholders.OldToolResult, Assert.IsType<ToolResultBlock>(user.Content[0]).Content);
        var assistant = Assert.IsType<AssistantMessage>(engine.Messages[1]);
        Assert.Contains(MicrocompactPlaceholders.OldThinking, Assert.IsType<ThinkingBlock>(assistant.Content[0]).Text);
    }

    [Fact]
    public async Task SubmitMessageAsync_EmitsAutoCompactionBeforeApiCall()
    {
        using var temp = new TempDirectory();
        var handler = new FakeAnthropicHandler();
        handler.EnqueueResponse(FakeAnthropicHandler.CreateMessageResponse("hello"));
        var client = TestSupport.CreateChatClient(handler);
        var engine = CreateEngine(
            temp.Root,
            new RecordingJournal(),
            client: client,
            initialMessages: BuildPressureMessages(),
            config: new QueryEngineConfig
            {
                EnableAutoCompact = true,
                EnableSessionMemoryCompact = true,
                ApproxContextWindowTokens = 800,
                AutoCompactBufferTokens = 50,
                AutoCompactPreserveTailCount = 2,
                AutoCompactMinimumMessageCount = 2,
                ApproxCharsPerToken = 4,
                AutoCompactWarningRatio = 0.25,
                AutoCompactBlockingRatio = 0.35,
                AutoCompactFailureLimit = 3,
            });

        var events = new List<QueryEvent>();
        await foreach (var evt in engine.SubmitMessageAsync("new request"))
        {
            events.Add(evt);
            if (evt is QueryCompleteEvent)
                break;
        }

        var compactionIndex = events.FindIndex(evt => evt is ContextCompactionEvent);
        var apiIndex = events.FindIndex(evt => evt is StatusEvent status && status.Status == "calling_api");

        Assert.True(compactionIndex >= 0);
        Assert.True(apiIndex >= 0);
        Assert.True(compactionIndex < apiIndex);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task SubmitMessageAsync_EmitsPromptCacheBreakEventAfterWarmCacheMiss()
    {
        using var temp = new TempDirectory();
        var handler = new FakeAnthropicHandler();
        handler.EnqueueResponse(FakeAnthropicHandler.CreateMessageResponse(
            text: "first",
            inputTokens: 50,
            outputTokens: 10,
            cacheCreationInputTokens: 1_200));
        handler.EnqueueResponse(FakeAnthropicHandler.CreateMessageResponse(
            text: "second",
            inputTokens: 60,
            outputTokens: 10));
        var client = TestSupport.CreateChatClient(handler);
        var engine = CreateEngine(
            temp.Root,
            new RecordingJournal(),
            client: client,
            config: new QueryEngineConfig
            {
                EnableAutoCompact = false,
                Model = ClaudeModels.DefaultMainModel,
            });

        await foreach (var _ in engine.SubmitMessageAsync("warm up"))
        {
        }

        var secondTurnEvents = new List<QueryEvent>();
        await foreach (var evt in engine.SubmitMessageAsync("trigger miss"))
            secondTurnEvents.Add(evt);

        var cacheEvent = Assert.Single(secondTurnEvents.OfType<PromptCacheStatusEvent>());
        Assert.True(cacheEvent.BreakDetected);
        Assert.Equal(0, cacheEvent.Usage.CacheReadInputTokens);
        Assert.Equal(0, cacheEvent.Usage.CacheCreationInputTokens);
    }

    [Fact]
    public async Task SubmitMessageAsync_RetriesRateLimitUsingRetryAfterHeaderAndTracksQuota()
    {
        using var temp = new TempDirectory();
        var responseObserver = new ApiResponseObserver();
        var handler = new FakeAnthropicHandler();
        handler.EnqueueResponse(FakeAnthropicHandler.CreateErrorResponse(
            HttpStatusCode.TooManyRequests,
            "rate_limit_error",
            "too many requests",
            ("retry-after", "7"),
            ("anthropic-ratelimit-requests-remaining", "0"),
            ("anthropic-ratelimit-requests-reset", "2026-04-15T02:00:00Z")));
        handler.EnqueueResponse(FakeAnthropicHandler.CreateMessageResponse(
            "hello after retry",
            headers:
            [
                ("anthropic-ratelimit-requests-limit", "10"),
                ("anthropic-ratelimit-requests-remaining", "9"),
                ("anthropic-ratelimit-requests-reset", "2026-04-15T02:05:00Z"),
                ("anthropic-ratelimit-tokens-limit", "50000"),
                ("anthropic-ratelimit-tokens-remaining", "48000"),
                ("anthropic-ratelimit-tokens-reset", "2026-04-15T02:05:00Z"),
            ]));

        var delays = new List<TimeSpan>();
        var engine = CreateEngine(
            temp.Root,
            new RecordingJournal(),
            client: TestSupport.CreateRetryingChatClient(
                handler,
                responseObserver,
                delayAsync: (delay, _) =>
                {
                    delays.Add(delay);
                    return Task.CompletedTask;
                }),
            config: new QueryEngineConfig
            {
                UseStreamingApi = false,
                EnableAutoCompact = false,
                ApiMaxRetryCount = 2,
            });

        var events = await CollectAsync(engine.SubmitMessageAsync("retry me"));

        var complete = Assert.IsType<QueryCompleteEvent>(events[^1]);
        Assert.True(complete.Success);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal([TimeSpan.FromSeconds(7)], delays);
        Assert.NotNull(engine.LatestQuotaStatus);
        Assert.Equal(9, engine.LatestQuotaStatus!.RequestsRemaining);
        Assert.Equal(48000, engine.LatestQuotaStatus.TokensRemaining);
    }

    [Fact]
    public async Task SubmitMessageAsync_RetriesTimeoutUsingExponentialBackoff()
    {
        using var temp = new TempDirectory();
        var handler = new FakeAnthropicHandler();
        handler.EnqueueException(new TaskCanceledException("request timed out"));
        handler.EnqueueResponse(FakeAnthropicHandler.CreateMessageResponse("timeout recovered"));

        var delays = new List<TimeSpan>();
        var engine = CreateEngine(
            temp.Root,
            new RecordingJournal(),
            client: TestSupport.CreateRetryingChatClient(
                handler,
                delayAsync: (delay, _) =>
                {
                    delays.Add(delay);
                    return Task.CompletedTask;
                }),
            config: new QueryEngineConfig
            {
                UseStreamingApi = false,
                EnableAutoCompact = false,
                ApiMaxRetryCount = 2,
                ApiRetryBaseDelay = TimeSpan.FromSeconds(2),
                ApiRetryMaxDelay = TimeSpan.FromSeconds(10),
            });

        var events = await CollectAsync(engine.SubmitMessageAsync("timeout"));

        var complete = Assert.IsType<QueryCompleteEvent>(events[^1]);
        Assert.True(complete.Success);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal([TimeSpan.FromSeconds(2)], delays);
    }

    [Fact]
    public async Task SubmitMessageAsync_DoesNotRetryBadRequest()
    {
        using var temp = new TempDirectory();
        var handler = new FakeAnthropicHandler();
        handler.EnqueueResponse(FakeAnthropicHandler.CreateErrorResponse(
            HttpStatusCode.BadRequest,
            "invalid_request_error",
            "bad request"));

        var delays = new List<TimeSpan>();
        var engine = CreateEngine(
            temp.Root,
            new RecordingJournal(),
            client: TestSupport.CreateRetryingChatClient(
                handler,
                delayAsync: (delay, _) =>
                {
                    delays.Add(delay);
                    return Task.CompletedTask;
                }),
            config: new QueryEngineConfig
            {
                UseStreamingApi = false,
                EnableAutoCompact = false,
                ApiMaxRetryCount = 3,
            });

        var events = await CollectAsync(engine.SubmitMessageAsync("bad request"));

        var complete = Assert.IsType<QueryCompleteEvent>(events[^1]);
        Assert.False(complete.Success);
        Assert.Single(handler.Requests);
        Assert.Empty(delays);
        Assert.Contains("bad request", complete.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ClearMessagesAsync_ResetsJournalHead()
    {
        using var temp = new TempDirectory();
        var journal = new RecordingJournal();
        var engine = CreateEngine(
            temp.Root,
            journal,
            initialMessages: [UserMessage.FromText("hello")]);

        await engine.ClearMessagesAsync();

        Assert.Empty(engine.Messages);
        Assert.Equal(1, journal.ResetHeadCount);
    }

    private static async Task<List<QueryEvent>> CollectAsync(IAsyncEnumerable<QueryEvent> events)
    {
        var result = new List<QueryEvent>();
        await foreach (var evt in events)
            result.Add(evt);
        return result;
    }

    private static QueryEngine CreateEngine(
        string workingDirectory,
        RecordingJournal journal,
        Microsoft.Extensions.AI.IChatClient? client = null,
        IReadOnlyList<ConversationMessage>? initialMessages = null,
        QueryEngineConfig? config = null)
    {
        var provider = new ContextProvider
        {
            WorkingDirectory = workingDirectory,
        };

        var tools = new ToolRegistry();
        var permissions = new DefaultPermissionChecker();
        var httpHandler = new FakeAnthropicHandler();
        var chatClient = client ?? TestSupport.CreateChatClient(httpHandler);

        return TestSupport.CreateQueryEngine(
            chatClient,
            tools,
            provider,
            permissions,
            config ?? new QueryEngineConfig(),
            journal: journal,
            initialMessages: initialMessages);
    }

    [Fact]
    public async Task RegisterAttachment_PersistsToJournalMetadata()
    {
        using var temp = new TempDirectory();
        var journal = new RecordingJournal();
        var engine = CreateEngine(
            temp.Root,
            journal,
            initialMessages: [],
            config: new QueryEngineConfig { Model = ClaudeModels.DefaultMainModel });

        var attachment = await engine.RegisterAttachmentAsync(
            "readme.md", "text/markdown", 2048, AttachmentSource.User, "/tmp/readme.md");

        Assert.NotNull(attachment.Id);
        Assert.Equal("readme.md", attachment.FileName);
        Assert.Equal("text/markdown", attachment.MimeType);
        Assert.Equal(2048, attachment.SizeBytes);
        Assert.Equal(AttachmentSource.User, attachment.Source);
        Assert.Equal("/tmp/readme.md", attachment.SourcePath);

        Assert.Single(engine.Attachments.GetAll());
        Assert.NotNull(engine.Attachments.Get(attachment.Id));

        var meta = engine.SessionMetadata;
        Assert.Single(meta.Attachments);
        Assert.True(meta.Attachments.ContainsKey(attachment.Id));

        Assert.Single(journal.Metadata.Attachments);
    }

    [Fact]
    public async Task RemoveAttachment_RemovesFromRegistryAndJournal()
    {
        using var temp = new TempDirectory();
        var journal = new RecordingJournal();
        var engine = CreateEngine(
            temp.Root,
            journal,
            initialMessages: [],
            config: new QueryEngineConfig { Model = ClaudeModels.DefaultMainModel });

        var attachment = await engine.RegisterAttachmentAsync(
            "data.csv", "text/csv", 512, AttachmentSource.Tool);

        var removed = await engine.RemoveAttachmentAsync(attachment.Id);
        Assert.True(removed);
        Assert.Empty(engine.Attachments.GetAll());
        Assert.Empty(engine.SessionMetadata.Attachments);
        Assert.Empty(journal.Metadata.Attachments);
    }

    [Fact]
    public async Task RemoveAttachment_ReturnsFalseForUnknownId()
    {
        using var temp = new TempDirectory();
        var journal = new RecordingJournal();
        var engine = CreateEngine(
            temp.Root,
            journal,
            initialMessages: [],
            config: new QueryEngineConfig { Model = ClaudeModels.DefaultMainModel });

        var removed = await engine.RemoveAttachmentAsync("nonexistent");
        Assert.False(removed);
    }

    [Fact]
    public void AttachmentRegistry_RestoresFromInitialMetadata()
    {
        using var temp = new TempDirectory();
        var journal = new RecordingJournal();
        var initialMetadata = new ConversationSessionMetadata();
        initialMetadata.Attachments["preloaded"] = new Attachment
        {
            Id = "preloaded",
            FileName = "old.txt",
            MimeType = "text/plain",
            SizeBytes = 100,
            Source = AttachmentSource.System,
        };

        var provider = new ContextProvider { WorkingDirectory = temp.Root };
        var tools = new ToolRegistry();
        var permissions = new DefaultPermissionChecker();
        var httpHandler = new FakeAnthropicHandler();
        var chatClient = TestSupport.CreateChatClient(httpHandler);

        var engine = TestSupport.CreateQueryEngine(
            chatClient, tools, provider, permissions,
            new QueryEngineConfig { Model = ClaudeModels.DefaultMainModel },
            journal: journal,
            initialMetadata: initialMetadata);

        Assert.NotNull(engine.Attachments.Get("preloaded"));
        Assert.Equal("old.txt", engine.Attachments.Get("preloaded")!.FileName);
    }

    private static IReadOnlyList<ConversationMessage> BuildPressureMessages()
    {
        var oldTimestamp = DateTimeOffset.UtcNow - TimeSpan.FromHours(4);
        return
        [
            new UserMessage
            {
                Timestamp = oldTimestamp,
                Content = [new TextBlock(new string('u', 1500))],
            },
            new AssistantMessage
            {
                Timestamp = oldTimestamp,
                Content =
                [
                    new TextBlock(new string('a', 1500)),
                    new ThinkingBlock(new string('t', 1200)),
                ],
            },
            UserMessage.FromToolResult("tool-1", new string('r', 1500)),
            new AssistantMessage
            {
                Timestamp = oldTimestamp,
                Content = [new TextBlock(new string('b', 1500))],
            },
        ];
    }
}

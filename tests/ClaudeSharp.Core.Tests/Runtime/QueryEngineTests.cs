using ClaudeSharp.Core.Context;
using ClaudeSharp.Core.Messages;
using ClaudeSharp.Core.Permissions;
using ClaudeSharp.Core.Query;
using ClaudeSharp.Core.Storage;
using ClaudeSharp.Core.Tools;

namespace ClaudeSharp.Core.Tests.Runtime;

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
        var client = TestSupport.CreateAnthropicClient(handler);
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

    private static QueryEngine CreateEngine(
        string workingDirectory,
        RecordingJournal journal,
        Anthropic.AnthropicClient? client = null,
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
        var anthropicClient = client ?? TestSupport.CreateAnthropicClient(httpHandler);

        return TestSupport.CreateQueryEngine(
            anthropicClient,
            tools,
            provider,
            permissions,
            config ?? new QueryEngineConfig(),
            journal: journal,
            initialMessages: initialMessages);
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

using System.Text.Json;
using Aexon.Core.Compaction;
using Aexon.Core.Context;
using Aexon.Core.Messages;
using Aexon.Core.Permissions;
using Aexon.Core.Query;
using Aexon.Core.Storage;
using Aexon.Core.Tools;
using Anthropic;
using Microsoft.Extensions.AI;

namespace Aexon.Core.Tests.Runtime;

/// <summary>
/// Contains tests for query Engine Branch.
/// </summary>
public sealed class QueryEngineBranchTests
{
    [Theory]
    [InlineData(ThinkingMode.Disabled)]
    [InlineData(ThinkingMode.Enabled)]
    [InlineData(ThinkingMode.Adaptive)]
    public async Task SubmitMessageAsync_SerializesHistoryAndThinkingMode(ThinkingMode thinkingMode)
    {
        using var temp = new TempDirectory();
        var handler = new FakeAnthropicHandler();
        handler.EnqueueResponse(FakeAnthropicHandler.CreateMessageResponse("ok"));

        var engine = CreateEngine(
            temp.Root,
            new RecordingJournal(),
            client: TestSupport.CreateChatClient(handler),
            initialMessages: BuildHistory(),
            config: new QueryEngineConfig
            {
                ThinkingMode = thinkingMode,
                MaxTurns = 1,
            });

        var events = await CollectAsync(engine.SubmitMessageAsync("latest question"));

        Assert.Contains(events, evt => evt is TextDeltaEvent delta && delta.Text == "ok");
        Assert.Equal(ClaudeModels.DefaultMainModel, engine.CurrentModel);
        Assert.Equal(2, engine.TotalUsage.TotalTokens);
        Assert.Single(handler.Requests);

        var body = handler.Bodies.Single();
        Assert.Contains("\"tool_use\"", body);
        Assert.Contains("\"tool_result\"", body);
        Assert.Contains("\"thinking\"", body);
    }

    [Fact]
    public async Task SubmitMessageAsync_ReportsClientExceptionAsFailure()
    {
        using var temp = new TempDirectory();
        var handler = new FakeAnthropicHandler();
        handler.EnqueueResponse(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent("not json", System.Text.Encoding.UTF8, "application/json"),
        });

        var engine = CreateEngine(
            temp.Root,
            new RecordingJournal(),
            client: TestSupport.CreateChatClient(handler));

        var events = await CollectAsync(engine.SubmitMessageAsync("ping"));

        var complete = Assert.IsType<QueryCompleteEvent>(events[^1]);
        Assert.False(complete.Success);
        Assert.Contains("invalid JSON", complete.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SubmitMessageAsync_EmitsAutoCompactFailureWhenPipelineThrows()
    {
        using var temp = new TempDirectory();
        var handler = new FakeAnthropicHandler();
        handler.EnqueueResponse(FakeAnthropicHandler.CreateMessageResponse("ok"));

        var engine = CreateEngine(
            temp.Root,
            new RecordingJournal(),
            client: TestSupport.CreateChatClient(handler),
            initialMessages: [UserMessage.FromText("seed")],
            config: new QueryEngineConfig
            {
                EnableAutoCompact = true,
                EnableSessionMemoryCompact = true,
                AutoCompactFailureLimit = 2,
            },
            contextPressurePipeline: new ThrowingContextPressurePipeline());

        var events = await CollectAsync(engine.SubmitMessageAsync("ping"));

        Assert.Contains(events, evt => evt is ContextCompactionEvent compact && compact.Mode == "failed");
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task SubmitMessageAsync_SkipsAutoCompactWhenDisabled()
    {
        using var temp = new TempDirectory();
        var handler = new FakeAnthropicHandler();
        handler.EnqueueResponse(FakeAnthropicHandler.CreateMessageResponse("ok"));

        var engine = CreateEngine(
            temp.Root,
            new RecordingJournal(),
            client: TestSupport.CreateChatClient(handler),
            config: new QueryEngineConfig
            {
                EnableAutoCompact = false,
            });

        var events = await CollectAsync(engine.SubmitMessageAsync("ping"));

        Assert.DoesNotContain(events, evt => evt is ContextCompactionEvent);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task StateManagementMethods_UpdateRuntimeState()
    {
        using var temp = new TempDirectory();
        var journal = new RecordingJournal();
        var engine = CreateEngine(temp.Root, journal);

        Assert.Equal(ClaudeModels.DefaultMainModel, engine.SetModel("sonnet"));
        Assert.Equal(ClaudeModels.DefaultMainModel, engine.CurrentModel);
        Assert.Equal(ClaudeModels.DefaultMainModel, await engine.SetModelAsync("default"));
        Assert.Equal(ClaudeModels.DefaultMainModel, engine.CurrentModel);
        Assert.Equal(journal.SessionId, engine.SessionId);
        Assert.Equal(journal.TranscriptPath, engine.TranscriptPath);
        Assert.Contains(journal.SessionUpdates, update => update.Model == ClaudeModels.DefaultMainModel);

        await engine.AddSessionTagAsync("alpha");
        await engine.AddSessionTagAsync("   ");
        await engine.RemoveSessionTagAsync("alpha");
        await engine.ClearSessionTagsAsync();
        await engine.SetSessionTitleAsync(" title ");
        Assert.Equal("title", engine.SessionMetadata.Title);
        await engine.SetSessionTitleAsync("   ");
        await engine.SetPermissionModeAsync(PermissionMode.Plan);
        engine.ClearMessages();

        Assert.Empty(engine.Messages);
        Assert.Equal(PermissionMode.Plan, engine.SessionMetadata.Mode);
        Assert.Null(engine.SessionMetadata.Title);
        Assert.Empty(engine.SessionMetadata.Tags);
        Assert.True(journal.ResetHeadCount >= 1);
    }

    [Fact]
    public async Task CompactionMethods_ReturnNullWhenThereIsNothingToCompact()
    {
        using var temp = new TempDirectory();
        var engine = CreateEngine(temp.Root, new RecordingJournal());

        Assert.Null(await engine.CompactAsync());
        Assert.Null(await engine.CompactUpToAsync(0));
        Assert.Null(await engine.CompactFromAsync(0));
        Assert.Null(await engine.SessionMemoryCompactAsync());
        Assert.Null(await engine.MicrocompactAsync());
    }

    private static QueryEngine CreateEngine(
        string workingDirectory,
        RecordingJournal journal,
        IChatClient? client = null,
        IReadOnlyList<ConversationMessage>? initialMessages = null,
        QueryEngineConfig? config = null,
        IContextPressurePipeline? contextPressurePipeline = null)
    {
        var provider = new ContextProvider
        {
            WorkingDirectory = workingDirectory,
        };

        var tools = new ToolRegistry();
        var permissions = new DefaultPermissionChecker();
        var handler = new FakeAnthropicHandler();
        var chatClient = client ?? TestSupport.CreateChatClient(handler);

        return TestSupport.CreateQueryEngine(
            chatClient,
            tools,
            provider,
            permissions,
            config ?? new QueryEngineConfig(),
            journal: journal,
            initialMessages: initialMessages,
            contextPressurePipeline: contextPressurePipeline);
    }

    private static IReadOnlyList<ConversationMessage> BuildHistory()
    {
        return
        [
            UserMessage.FromText("previous user"),
            new AssistantMessage
            {
                Content =
                [
                    new TextBlock("previous assistant"),
                    new ThinkingBlock("pondering", "sig-1"),
                    new ToolUseBlock
                    {
                        ToolUseId = "tool-1",
                        Name = "search",
                        Input = JsonSerializer.SerializeToElement(new { command = "search" }),
                    },
                ],
            },
            UserMessage.FromToolResult("tool-1", "tool output"),
        ];
    }

    private static async Task<List<QueryEvent>> CollectAsync(IAsyncEnumerable<QueryEvent> events)
    {
        var result = new List<QueryEvent>();
        await foreach (var evt in events)
            result.Add(evt);
        return result;
    }

    private sealed class ThrowingContextPressurePipeline : IContextPressurePipeline
    {
        public ContextPreparationResult Prepare(
            IReadOnlyList<ConversationMessage> messages,
            ContextPressureOptions options,
            DateTimeOffset? now = null) =>
            throw new InvalidOperationException("pressure failure");
    }
}

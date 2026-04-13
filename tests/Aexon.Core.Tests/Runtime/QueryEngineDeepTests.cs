using System.Net;
using System.Text;
using System.Text.Json;
using Aexon.Core.Compaction;
using Aexon.Core.Context;
using Aexon.Core.Messages;
using Aexon.Core.Permissions;
using Aexon.Core.Query;
using Aexon.Core.Storage;
using Aexon.Core.Tools;
using Anthropic;

namespace Aexon.Core.Tests.Runtime;

/// <summary>
/// Contains tests for query Engine Deep.
/// </summary>
public sealed class QueryEngineDeepTests
{
    [Fact]
    public async Task StateApis_ExposeSessionDataAndPersistModelChanges()
    {
        using var temp = new TempDirectory();
        var journal = new RecordingJournal();
        var assistant = new AssistantMessage
        {
            Content = [new TextBlock("hello")],
            Usage = new TokenUsage
            {
                InputTokens = 2,
                OutputTokens = 3,
            },
        };

        var engine = CreateEngine(
            temp.Root,
            journal,
            initialMessages: [assistant],
            config: new QueryEngineConfig
            {
                Model = ClaudeModels.DefaultMainModel,
            });

        Assert.Equal(assistant.Usage, engine.TotalUsage);
        Assert.Equal(journal.SessionId, engine.SessionId);
        Assert.Equal(journal.TranscriptPath, engine.TranscriptPath);
        Assert.Equal(ClaudeModels.DefaultMainModel, engine.CurrentModel);
        Assert.Equal("claude-opus-4-6", engine.SetModel("opus"));

        var resolved = await engine.SetModelAsync("haiku");

        Assert.Equal("claude-haiku-4-5", resolved);
        Assert.Equal("claude-haiku-4-5", engine.CurrentModel);
        Assert.Contains(journal.SessionUpdates, update => update.Model == "claude-haiku-4-5");

        await engine.AddSessionTagAsync("first");
        await engine.AddSessionTagAsync("second");
        await engine.RemoveSessionTagAsync(" ");
        await engine.ClearSessionTagsAsync();

        Assert.Empty(engine.SessionMetadata.Tags);

        engine.ClearMessages();
        Assert.Empty(engine.Messages);
        Assert.Equal(1, journal.ResetHeadCount);
    }

    [Fact]
    public async Task CompactionApis_HandleNullAndChangedResults()
    {
        using var temp = new TempDirectory();
        var journal = new RecordingJournal();
        var compactUpToEngine = CreateEngine(
            temp.Root,
            journal,
            initialMessages:
            [
                UserMessage.FromText("one"),
                new AssistantMessage { Content = [new TextBlock("two")] },
                UserMessage.FromText("three"),
                new AssistantMessage { Content = [new TextBlock("four")] },
            ]);

        var compactUpTo = await compactUpToEngine.CompactUpToAsync(2);
        Assert.NotNull(compactUpTo);
        Assert.Equal(1, journal.CheckpointCount);

        var compactFromEngine = CreateEngine(
            temp.Root,
            journal,
            initialMessages:
            [
                UserMessage.FromText("alpha"),
                new AssistantMessage { Content = [new TextBlock("beta")] },
                UserMessage.FromText("gamma"),
                new AssistantMessage { Content = [new TextBlock("delta")] },
            ]);

        var compactFrom = await compactFromEngine.CompactFromAsync(2);
        Assert.NotNull(compactFrom);
        Assert.Equal(2, journal.CheckpointCount);

        var sessionMemoryEngine = CreateEngine(
            temp.Root,
            journal,
            initialMessages:
            [
                UserMessage.FromText("older user"),
                new AssistantMessage { Content = [new TextBlock("older assistant")] },
                UserMessage.FromText("middle user"),
                new AssistantMessage { Content = [new TextBlock("middle assistant")] },
                UserMessage.FromText("tail user"),
            ]);

        var sessionMemory = await sessionMemoryEngine.SessionMemoryCompactAsync(preserveTailCount: 1);
        Assert.NotNull(sessionMemory);
        Assert.Equal(3, journal.CheckpointCount);

        var noMicrocompactEngine = CreateEngine(
            temp.Root,
            journal,
            initialMessages:
            [
                new AssistantMessage
                {
                    Timestamp = DateTimeOffset.UtcNow,
                    Content = [new ThinkingBlock("recent"), new TextBlock("assistant")],
                },
                UserMessage.FromText("tail"),
            ]);

        var noMicrocompact = await noMicrocompactEngine.MicrocompactAsync(
            preserveTailCount: 10,
            force: false);
        Assert.Null(noMicrocompact);
    }

    [Fact]
    public async Task SubmitMessageAsync_MapsPermissionProgressAndToolResults()
    {
        using var temp = new TempDirectory();
        var handler = new FakeAnthropicHandler();
        handler.EnqueueResponse(CreateToolUseResponse());
        handler.EnqueueResponse(FakeAnthropicHandler.CreateMessageResponse("done"));

        var journal = new RecordingJournal();
        var tool = new FakeTool { Name = "search" };
        var runtime = new ScriptedToolRuntime(tool);
        var engine = CreateEngine(
            temp.Root,
            journal,
            client: TestSupport.CreateAnthropicClient(handler),
            tools: BuildRegistry(tool),
            toolRuntime: runtime);

        var events = new List<QueryEvent>();
        await using var enumerator = engine.SubmitMessageAsync("use tool").GetAsyncEnumerator();
        while (await enumerator.MoveNextAsync())
        {
            events.Add(enumerator.Current);
            if (enumerator.Current is PermissionRequestEvent permissionRequest)
                permissionRequest.SetResponse(true);
        }

        Assert.Contains(events, evt => evt is ToolUseStartEvent start && start.ToolName == "search");
        Assert.Contains(events, evt => evt is PermissionRequestEvent request && request.ToolName == "search");
        Assert.Contains(events, evt => evt is ToolProgressEvent progress && progress.Message == "running");
        Assert.Contains(events, evt => evt is ToolResultEvent result && result.Result == "tool output");
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains(journal.AppendedMessages, message =>
            message is UserMessage user &&
            user.Content.OfType<ToolResultBlock>().Any(block => block.Content == "tool output"));
    }

    [Fact]
    public async Task SubmitMessageAsync_EmitsFailedThenSkippedCompactionEvents_WhenCircuitOpens()
    {
        using var temp = new TempDirectory();
        var handler = new FakeAnthropicHandler();
        handler.EnqueueResponse(FakeAnthropicHandler.CreateMessageResponse("first"));
        handler.EnqueueResponse(FakeAnthropicHandler.CreateMessageResponse("second"));
        var engine = CreateEngine(
            temp.Root,
            new RecordingJournal(),
            client: TestSupport.CreateAnthropicClient(handler),
            contextPressurePipeline: new ThrowingContextPressurePipeline(),
            config: new QueryEngineConfig
            {
                EnableAutoCompact = true,
                AutoCompactFailureLimit = 1,
            });

        var firstRun = await CollectAsync(engine.SubmitMessageAsync("hello"));
        var secondRun = await CollectAsync(engine.SubmitMessageAsync("again"));

        var failed = Assert.IsType<ContextCompactionEvent>(firstRun[0]);
        var skipped = Assert.IsType<ContextCompactionEvent>(secondRun[0]);

        Assert.Equal("failed", failed.Mode);
        Assert.Equal("skipped", skipped.Mode);
        Assert.Contains("circuit open", skipped.Reason);
    }

    [Fact]
    public async Task SubmitMessageAsync_SerializesExistingMessagesToolsAndThinkingConfig()
    {
        using var temp = new TempDirectory();
        var handler = new FakeAnthropicHandler();
        handler.EnqueueResponse(FakeAnthropicHandler.CreateMessageResponse("ok"));
        var tool = new FakeTool
        {
            Name = "search",
            Description = "Search files",
            InputSchema = JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new
                {
                    query = new { type = "string" },
                },
            }),
        };

        var engine = CreateEngine(
            temp.Root,
            new RecordingJournal(),
            client: TestSupport.CreateAnthropicClient(handler),
            tools: BuildRegistry(tool),
            initialMessages:
            [
                UserMessage.FromText("first"),
                new AssistantMessage
                {
                    Content =
                    [
                        new TextBlock("draft"),
                        new ToolUseBlock
                        {
                            ToolUseId = "tool-1",
                            Name = "search",
                            Input = JsonSerializer.SerializeToElement(new { query = "needle" }),
                        },
                        new ThinkingBlock("signed thinking", "sig-1"),
                        new ThinkingBlock("unsigned thinking"),
                    ],
                },
                UserMessage.FromToolResult("tool-1", "result text"),
            ],
            config: new QueryEngineConfig
            {
                ThinkingMode = ThinkingMode.Enabled,
                ThinkingBudgetTokens = 321,
            });

        await CollectAsync(engine.SubmitMessageAsync("follow up"));

        var request = JsonDocument.Parse(handler.Bodies[0]).RootElement;
        var messages = request.GetProperty("messages");
        var assistantMessage = messages[1];
        var assistantTypes = assistantMessage
            .GetProperty("content")
            .EnumerateArray()
            .Select(item => item.GetProperty("type").GetString())
            .ToArray();
        var userToolResult = messages[2]
            .GetProperty("content")[0];
        var thinking = request.GetProperty("thinking");
        var tools = request.GetProperty("tools");

        Assert.Contains("tool_use", assistantTypes);
        Assert.Contains("thinking", assistantTypes);
        Assert.DoesNotContain("unsigned thinking", assistantMessage.GetRawText());
        Assert.Equal("tool_result", userToolResult.GetProperty("type").GetString());
        Assert.Equal("result text", userToolResult.GetProperty("content").GetString());
        Assert.Equal("enabled", thinking.GetProperty("type").GetString());
        Assert.Equal(321, thinking.GetProperty("budget_tokens").GetInt32());
        Assert.Equal("search", tools[0].GetProperty("name").GetString());
    }

    private static QueryEngine CreateEngine(
        string workingDirectory,
        RecordingJournal journal,
        AnthropicClient? client = null,
        ToolRegistry? tools = null,
        IReadOnlyList<ConversationMessage>? initialMessages = null,
        QueryEngineConfig? config = null,
        IToolRuntime? toolRuntime = null,
        IContextPressurePipeline? contextPressurePipeline = null)
    {
        var provider = new ContextProvider
        {
            WorkingDirectory = workingDirectory,
        };

        return TestSupport.CreateQueryEngine(
            client ?? TestSupport.CreateAnthropicClient(new FakeAnthropicHandler()),
            tools ?? new ToolRegistry(),
            provider,
            new DefaultPermissionChecker(),
            config ?? new QueryEngineConfig
            {
                EnableAutoCompact = false,
            },
            journal: journal,
            initialMessages: initialMessages,
            toolRuntime: toolRuntime,
            contextPressurePipeline: contextPressurePipeline);
    }

    private static ToolRegistry BuildRegistry(params ITool[] tools)
    {
        var registry = new ToolRegistry();
        foreach (var tool in tools)
            registry.Register(tool);
        return registry;
    }

    private static async Task<List<QueryEvent>> CollectAsync(IAsyncEnumerable<QueryEvent> events)
    {
        var result = new List<QueryEvent>();
        await foreach (var evt in events)
            result.Add(evt);
        return result;
    }

    private static HttpResponseMessage CreateToolUseResponse()
    {
        var payload = JsonSerializer.Serialize(new
        {
            id = "msg-1",
            type = "message",
            role = "assistant",
            model = ClaudeModels.DefaultMainModel,
            stop_reason = "tool_use",
            stop_sequence = (string?)null,
            content = new object[]
            {
                new
                {
                    type = "tool_use",
                    id = "tool-1",
                    name = "search",
                    caller = new { type = "direct" },
                    input = new { query = "claude" },
                },
            },
            usage = new
            {
                input_tokens = 1,
                output_tokens = 1,
                cache_read_input_tokens = 0,
                cache_creation_input_tokens = 0,
            },
        });

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
    }

    private sealed class ScriptedToolRuntime : IToolRuntime
    {
        private readonly ITool _tool;

        public ScriptedToolRuntime(ITool tool)
        {
            _tool = tool;
        }

        public async IAsyncEnumerable<ToolRunUpdate> RunBatchAsync(
            IReadOnlyList<ToolUseBlock> invocations,
            ToolExecutionContext context,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var invocation = Assert.Single(invocations);
            var request = new ToolPermissionRequestUpdate
            {
                Invocation = invocation,
                Tool = _tool,
                Description = "Need approval",
                ObservedInput = invocation.Input,
            };

            yield return request;

            if (!await request.WaitForResponseAsync())
            {
                yield return new ToolCompletedUpdate(
                    new ToolRunOutcome(invocation, _tool, ToolResult.Error("denied")));
                yield break;
            }

            yield return new ToolProgressUpdate(
                invocation.ToolUseId,
                invocation.Name,
                new ToolProgress(invocation.ToolUseId, "progress", "running"));

            yield return new ToolCompletedUpdate(
                new ToolRunOutcome(invocation, _tool, ToolResult.Success("tool output")));
        }
    }

    private sealed class ThrowingContextPressurePipeline : IContextPressurePipeline
    {
        public ContextPreparationResult Prepare(
            IReadOnlyList<ConversationMessage> messages,
            ContextPressureOptions options,
            DateTimeOffset? now = null) =>
            throw new InvalidOperationException("pressure boom");
    }
}

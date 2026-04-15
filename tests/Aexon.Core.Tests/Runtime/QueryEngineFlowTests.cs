using System.Net;
using System.Text;
using System.Text.Json;
using Aexon.Core.Context;
using Aexon.Core.Messages;
using Aexon.Core.Permissions;
using Aexon.Core.Query;
using Aexon.Core.Storage;
using Aexon.Core.Tools;
using Aexon.Tools;

namespace Aexon.Core.Tests.Runtime;

/// <summary>
/// Contains tests for query Engine Flow.
/// </summary>
public sealed class QueryEngineFlowTests
{
    [Fact]
    public async Task SubmitMessageAsync_EmitsAssistantContentInOrder()
    {
        using var temp = new TempDirectory();
        var handler = new ScriptedAnthropicHandler();
        handler.EnqueueResponse(CreateMessageResponse(
            content:
            [
                new { type = "text", text = "hello" },
                new { type = "thinking", thinking = "pondering", signature = "sig-1" },
                new
                {
                    type = "tool_use",
                    id = "tool-1",
                    name = "search",
                    caller = new { type = "direct" },
                    input = new { command = "search" },
                },
            ],
            stopReason: "tool_use"));

        var engine = CreateEngine(
            temp.Root,
            handler,
            new ToolRegistry(),
            new RecordingJournal(),
            new StubPermissionChecker(),
            new QueryEngineConfig
            {
                MaxTurns = 1,
            });

        var events = await CollectAsync(engine.SubmitMessageAsync("find this"));

        Assert.Collection(
            events.Take(5),
            evt => Assert.IsType<StatusEvent>(evt),
            evt => Assert.IsType<TextDeltaEvent>(evt),
            evt => Assert.IsType<ThinkingDeltaEvent>(evt),
            evt => Assert.IsType<ToolUseStartEvent>(evt),
            evt => Assert.IsType<MessageEndEvent>(evt));

        var text = Assert.IsType<TextDeltaEvent>(events[1]);
        var thinking = Assert.IsType<ThinkingDeltaEvent>(events[2]);
        var toolUse = Assert.IsType<ToolUseStartEvent>(events[3]);
        var maxTurn = Assert.IsType<TextDeltaEvent>(events[5]);

        Assert.Equal("hello", text.Text);
        Assert.Equal("pondering", thinking.Text);
        Assert.Equal("tool-1", toolUse.ToolUseId);
        Assert.Equal("search", toolUse.ToolName);
        Assert.Contains("maximum turn limit of 1", maxTurn.Text);
        Assert.DoesNotContain(events, evt => evt is ToolResultEvent);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task SubmitMessageAsync_AppendsToolResultBeforeNextAssistantTurn()
    {
        using var temp = new TempDirectory();
        var handler = new ScriptedAnthropicHandler();
        handler.EnqueueResponse(CreateMessageResponse(
            content:
            [
                new
                {
                    type = "tool_use",
                    id = "tool-1",
                    name = "search",
                    caller = new { type = "direct" },
                    input = new { command = "search" },
                },
            ],
            stopReason: "tool_use"));
        handler.EnqueueResponse(CreateMessageResponse(
            content:
            [
                new { type = "text", text = "done" },
            ],
            stopReason: "end_turn"));

        var journal = new RecordingJournal();
        var tool = new FakeTool
        {
            Name = "search",
            PermissionHandler = (_, _) => Task.FromResult(PermissionResult.Allow()),
            ExecuteHandler = (_, _, _, _) => Task.FromResult(ToolResult.Success("tool output")),
        };

        var engine = CreateEngine(
            temp.Root,
            handler,
            BuildRegistry(tool),
            journal,
            new StubPermissionChecker(),
            new QueryEngineConfig
            {
                MaxTurns = 3,
            });

        var events = await CollectAsync(engine.SubmitMessageAsync("find this"));

        Assert.Contains(events, evt => evt is ToolResultEvent result &&
                                       result.ToolUseId == "tool-1" &&
                                       result.Result == "tool output");
        Assert.Equal(4, journal.AppendedMessages.Count);
        Assert.IsType<UserMessage>(journal.AppendedMessages[0]);
        Assert.IsType<AssistantMessage>(journal.AppendedMessages[1]);
        var toolResultMessage = Assert.IsType<UserMessage>(journal.AppendedMessages[2]);
        Assert.IsType<AssistantMessage>(journal.AppendedMessages[3]);
        Assert.Equal(journal.AppendedMessages[1].Id, journal.ParentMessageIds[2]);
        Assert.Equal("tool output", Assert.IsType<ToolResultBlock>(toolResultMessage.Content[0]).Content);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task SubmitMessageAsync_AllowsAskUserQuestionToolToContinueConversation()
    {
        using var temp = new TempDirectory();
        var handler = new ScriptedAnthropicHandler();
        handler.EnqueueResponse(CreateMessageResponse(
            content:
            [
                new
                {
                    type = "tool_use",
                    id = "tool-1",
                    name = "AskUserQuestion",
                    caller = new { type = "direct" },
                    input = new
                    {
                        question = "Which option should I use?",
                        options = new[] { "alpha", "beta" },
                    },
                },
            ],
            stopReason: "tool_use"));
        handler.EnqueueResponse(CreateMessageResponse(
            content:
            [
                new { type = "text", text = "Using beta." },
            ],
            stopReason: "end_turn"));

        var journal = new RecordingJournal();
        UserQuestionRequest? prompted = null;
        var engine = CreateEngine(
            temp.Root,
            handler,
            BuildRegistry(new AskUserQuestionTool()),
            journal,
            new StubPermissionChecker(),
            new QueryEngineConfig
            {
                MaxTurns = 3,
            },
            askUserQuestion: (request, _) =>
            {
                prompted = request;
                return Task.FromResult(new UserQuestionResponse("beta"));
            });

        var events = await CollectAsync(engine.SubmitMessageAsync("help me decide"));

        Assert.Contains(events, evt => evt is ToolResultEvent result &&
                                       result.ToolName == "AskUserQuestion" &&
                                       result.Result == "beta");
        Assert.Contains(events, evt => evt is TextDeltaEvent text && text.Text == "Using beta.");
        Assert.NotNull(prompted);
        Assert.Equal("Which option should I use?", prompted!.Question);
        Assert.Equal(["alpha", "beta"], prompted.Options);
        Assert.Equal(4, journal.AppendedMessages.Count);
        Assert.Equal("beta", Assert.IsType<ToolResultBlock>(
            Assert.IsType<UserMessage>(journal.AppendedMessages[2]).Content[0]).Content);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task SubmitMessageAsync_ReportsApiFailureAndStops()
    {
        using var temp = new TempDirectory();
        var handler = new FailingAnthropicHandler(new HttpRequestException("network boom"));

        var engine = CreateEngine(
            temp.Root,
            handler,
            new ToolRegistry(),
            new RecordingJournal(),
            new StubPermissionChecker());

        var events = await CollectAsync(engine.SubmitMessageAsync("ping"));

        var complete = Assert.IsType<QueryCompleteEvent>(events[^1]);
        Assert.False(complete.Success);
        Assert.Contains("I/O exception", complete.ErrorMessage);
        Assert.NotEmpty(handler.Requests);
    }

    private static QueryEngine CreateEngine(
        string workingDirectory,
        HttpClientHandlerBase handler,
        ToolRegistry tools,
        RecordingJournal journal,
        IPermissionChecker permissions,
        QueryEngineConfig? config = null,
        AskUserQuestionHandler? askUserQuestion = null)
    {
        var provider = new ContextProvider
        {
            WorkingDirectory = workingDirectory,
        };

        var client = TestSupport.CreateChatClient(handler);
        return TestSupport.CreateQueryEngine(
            client,
            tools,
            provider,
            permissions,
            config ?? new QueryEngineConfig(),
            journal: journal,
            askUserQuestion: askUserQuestion);
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

    private static HttpResponseMessage CreateMessageResponse(
        object[] content,
        string stopReason)
    {
        var payload = JsonSerializer.Serialize(new
        {
            id = "msg-1",
            type = "message",
            role = "assistant",
            model = ClaudeModels.DefaultMainModel,
            stop_reason = stopReason,
            stop_sequence = (string?)null,
            content,
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

    private abstract class HttpClientHandlerBase : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
    }

    private sealed class FailingAnthropicHandler : HttpClientHandlerBase
    {
        private readonly Exception _exception;

        public FailingAnthropicHandler(Exception exception)
        {
            _exception = exception;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromException<HttpResponseMessage>(_exception);
        }
    }

    private sealed class ScriptedAnthropicHandler : HttpClientHandlerBase
    {
        private readonly Queue<Func<HttpResponseMessage>> _actions = new();

        public void EnqueueResponse(HttpResponseMessage response) =>
            _actions.Enqueue(() => response);

        public void EnqueueException(Exception exception) =>
            _actions.Enqueue(() => throw exception);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (_actions.Count == 0)
                return Task.FromResult(CreateMessageResponse([new { type = "text", text = "ok" }], "end_turn"));

            return Task.FromResult(_actions.Dequeue()());
        }
    }
}

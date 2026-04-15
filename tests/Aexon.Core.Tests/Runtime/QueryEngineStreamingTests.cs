using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Aexon.Core.Context;
using Aexon.Core.Memory;
using Aexon.Core.Messages;
using Aexon.Core.Permissions;
using Aexon.Core.Query;
using Aexon.Core.Storage;
using Aexon.Core.Tools;
using Microsoft.Extensions.AI;

namespace Aexon.Core.Tests.Runtime;

/// <summary>
/// Contains tests for streaming through the MEAI IChatClient adapter.
/// </summary>
public sealed class QueryEngineStreamingTests
{
    [Fact]
    public async Task SubmitMessageAsync_StreamingProducesTextAndToolEvents()
    {
        using var temp = new TempDirectory();

        var tools = new ToolRegistry();
        tools.Register(new FakeTool
        {
            Name = "search",
            InputSchema = TestSupport.Json(new
            {
                type = "object",
                properties = new
                {
                    command = new { type = "string" },
                },
            }),
        });

        var fakeClient = new FakeStreamingChatClient(
        [
            new ChatResponseUpdate
            {
                ResponseId = "msg-stream-1",
                Role = ChatRole.Assistant,
                Contents = [new TextContent("Okay")],
            },
            new ChatResponseUpdate
            {
                Contents =
                [
                    new FunctionCallContent("toolu_01", "search",
                        new Dictionary<string, object?> { ["command"] = "search" }),
                ],
                FinishReason = ChatFinishReason.Stop,
            },
        ]);

        var engine = TestSupport.CreateQueryEngine(
            fakeClient,
            tools,
            new ContextProvider { WorkingDirectory = temp.Root },
            new DefaultPermissionChecker(),
            new QueryEngineConfig
            {
                UseStreamingApi = true,
                MaxTurns = 1,
                EnableAutoCompact = false,
            },
            journal: new RecordingJournal());

        var events = await CollectAsync(engine.SubmitMessageAsync("find this"));

        Assert.IsType<StatusEvent>(events[0]);
        Assert.Contains(events, e => e is TextDeltaEvent { Text: "Okay" });
        Assert.Contains(events, e => e is ToolUseStartEvent { ToolName: "search" });
        Assert.Contains(events, e => e is MessageEndEvent);

        var assistant = engine.Messages.OfType<AssistantMessage>().Single();
        Assert.Contains(assistant.Content, b => b is TextBlock { Text: "Okay" });
        Assert.Contains(assistant.Content, b =>
            b is ToolUseBlock { ToolUseId: "toolu_01", Name: "search" });
    }

    [Fact]
    public async Task SubmitMessageAsync_NonStreamingProducesToolBlocks()
    {
        using var temp = new TempDirectory();
        var handler = new FakeAnthropicHandler();
        handler.EnqueueResponse(FakeAnthropicHandler.CreateMessageResponse("hello"));

        var engine = TestSupport.CreateQueryEngine(
            TestSupport.CreateChatClient(handler),
            new ToolRegistry(),
            new ContextProvider { WorkingDirectory = temp.Root },
            new DefaultPermissionChecker(),
            new QueryEngineConfig
            {
                UseStreamingApi = false,
                EnableAutoCompact = false,
            },
            journal: new RecordingJournal());

        var events = await CollectAsync(engine.SubmitMessageAsync("hi"));

        Assert.Contains(events, e => e is TextDeltaEvent { Text: "hello" });
        Assert.Contains(events, e => e is QueryCompleteEvent { Success: true });
    }

    [Fact]
    public async Task SessionMemoryCompactAsync_PersistsSummaryToSessionMemoryFile()
    {
        using var temp = new TempDirectory();
        var layout = new MemdirLayout
        {
            MemoryBaseDirectory = temp.FullPath("mem"),
            ProjectRootDirectory = temp.Root,
        };
        layout.EnsureDirectories();
        var sessionMemoryFile = layout.CreateSessionMemoryFile("session-1");
        var provider = new ContextProvider
        {
            WorkingDirectory = temp.Root,
        };

        var engine = TestSupport.CreateQueryEngine(
            TestSupport.CreateChatClient(new FakeAnthropicHandler()),
            new ToolRegistry(),
            provider,
            new DefaultPermissionChecker(),
            new QueryEngineConfig
            {
                EnableAutoCompact = false,
            },
            journal: new RecordingJournal(),
            sessionMemoryFile: sessionMemoryFile,
            initialMessages:
            [
                UserMessage.FromText("older user"),
                new AssistantMessage { Content = [new TextBlock("older assistant")] },
                UserMessage.FromText("middle user"),
                new AssistantMessage { Content = [new TextBlock("middle assistant")] },
                UserMessage.FromText("tail user"),
            ]);

        var result = await engine.SessionMemoryCompactAsync(preserveTailCount: 1);

        Assert.NotNull(result);
        Assert.True(sessionMemoryFile.Exists);
        Assert.Equal(result!.SummaryText, await sessionMemoryFile.LoadAsync());
        Assert.Equal(result.SummaryText, provider.SessionMemoryContent);
    }

    [Fact]
    public async Task SubmitMessageAsync_ReconnectsInterruptedStreamWithoutDuplicatingText()
    {
        using var temp = new TempDirectory();
        var handler = new FakeAnthropicHandler();
        handler.EnqueueResponse(CreateStreamingResponse(
            ("message_start", new
            {
                type = "message_start",
                message = new
                {
                    id = "msg-stream-retry-1",
                    type = "message",
                    role = "assistant",
                    model = ClaudeModels.DefaultMainModel,
                    content = Array.Empty<object>(),
                    stop_reason = (string?)null,
                    stop_sequence = (string?)null,
                    usage = new
                    {
                        input_tokens = 1,
                        output_tokens = 0,
                        cache_read_input_tokens = 0,
                        cache_creation_input_tokens = 0,
                    },
                },
            }),
            ("content_block_start", new
            {
                type = "content_block_start",
                index = 0,
                content_block = new
                {
                    type = "text",
                    text = "",
                },
            }),
            ("content_block_delta", new
            {
                type = "content_block_delta",
                index = 0,
                delta = new
                {
                    type = "text_delta",
                    text = "Hello",
                },
            })));
        handler.EnqueueResponse(CreateStreamingResponse(
            ("message_start", new
            {
                type = "message_start",
                message = new
                {
                    id = "msg-stream-retry-2",
                    type = "message",
                    role = "assistant",
                    model = ClaudeModels.DefaultMainModel,
                    content = Array.Empty<object>(),
                    stop_reason = (string?)null,
                    stop_sequence = (string?)null,
                    usage = new
                    {
                        input_tokens = 1,
                        output_tokens = 0,
                        cache_read_input_tokens = 0,
                        cache_creation_input_tokens = 0,
                    },
                },
            }),
            ("content_block_start", new
            {
                type = "content_block_start",
                index = 0,
                content_block = new
                {
                    type = "text",
                    text = "",
                },
            }),
            ("content_block_delta", new
            {
                type = "content_block_delta",
                index = 0,
                delta = new
                {
                    type = "text_delta",
                    text = "Hello",
                },
            }),
            ("content_block_delta", new
            {
                type = "content_block_delta",
                index = 0,
                delta = new
                {
                    type = "text_delta",
                    text = " world",
                },
            }),
            ("content_block_stop", new
            {
                type = "content_block_stop",
                index = 0,
            }),
            ("message_delta", new
            {
                type = "message_delta",
                delta = new
                {
                    stop_reason = "end_turn",
                },
                usage = new
                {
                    input_tokens = 1,
                    output_tokens = 2,
                    cache_read_input_tokens = 0,
                    cache_creation_input_tokens = 0,
                },
            }),
            ("message_stop", new
            {
                type = "message_stop",
            })));

        var delays = new List<TimeSpan>();
        var engine = TestSupport.CreateQueryEngine(
            TestSupport.CreateRetryingChatClient(
                handler,
                delayAsync: (delay, _) =>
                {
                    delays.Add(delay);
                    return Task.CompletedTask;
                }),
            new ToolRegistry(),
            new ContextProvider
            {
                WorkingDirectory = temp.Root,
            },
            new DefaultPermissionChecker(),
            new QueryEngineConfig
            {
                UseStreamingApi = true,
                EnableAutoCompact = false,
                ApiMaxRetryCount = 1,
                ApiRetryBaseDelay = TimeSpan.FromSeconds(3),
            },
            journal: new RecordingJournal());

        var events = await CollectAsync(engine.SubmitMessageAsync("recover stream"));

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal([TimeSpan.FromSeconds(3)], delays);
        Assert.Equal(
            ["Hello", " world"],
            events.OfType<TextDeltaEvent>().Select(evt => evt.Text).ToArray());
        Assert.Equal(
            "Hello world",
            Assert.IsType<TextBlock>(
                Assert.IsType<AssistantMessage>(engine.Messages[^1]).Content[0]).Text);
        Assert.True(Assert.IsType<QueryCompleteEvent>(events[^1]).Success);
    }

    [Fact]
    public async Task SubmitMessageAsync_StreamingPreservesFinishReasonWithRetryMiddleware()
    {
        using var temp = new TempDirectory();
        var handler = new FakeAnthropicHandler();
        handler.EnqueueResponse(CreateStreamingResponse(
            ("message_start", new
            {
                type = "message_start",
                message = new
                {
                    id = "msg-stream-tool-1",
                    type = "message",
                    role = "assistant",
                    model = ClaudeModels.DefaultMainModel,
                    content = Array.Empty<object>(),
                    stop_reason = (string?)null,
                    stop_sequence = (string?)null,
                    usage = new
                    {
                        input_tokens = 1,
                        output_tokens = 0,
                        cache_read_input_tokens = 0,
                        cache_creation_input_tokens = 0,
                    },
                },
            }),
            ("content_block_start", new
            {
                type = "content_block_start",
                index = 0,
                content_block = new
                {
                    type = "text",
                    text = "",
                },
            }),
            ("content_block_delta", new
            {
                type = "content_block_delta",
                index = 0,
                delta = new
                {
                    type = "text_delta",
                    text = "Okay",
                },
            }),
            ("content_block_stop", new
            {
                type = "content_block_stop",
                index = 0,
            }),
            ("content_block_start", new
            {
                type = "content_block_start",
                index = 1,
                content_block = new
                {
                    type = "tool_use",
                    id = "toolu_01",
                    name = "search",
                    input = new { },
                },
            }),
            ("content_block_delta", new
            {
                type = "content_block_delta",
                index = 1,
                delta = new
                {
                    type = "input_json_delta",
                    partial_json = "{\"command\":\"search\"}",
                },
            }),
            ("content_block_stop", new
            {
                type = "content_block_stop",
                index = 1,
            }),
            ("message_delta", new
            {
                type = "message_delta",
                delta = new
                {
                    stop_reason = "tool_use",
                },
                usage = new
                {
                    input_tokens = 1,
                    output_tokens = 2,
                    cache_read_input_tokens = 0,
                    cache_creation_input_tokens = 0,
                },
            }),
            ("message_stop", new
            {
                type = "message_stop",
            })));

        var tools = new ToolRegistry();
        tools.Register(new FakeTool
        {
            Name = "search",
            InputSchema = TestSupport.Json(new
            {
                type = "object",
                properties = new
                {
                    command = new { type = "string" },
                },
            }),
        });

        var engine = TestSupport.CreateQueryEngine(
            TestSupport.CreateRetryingChatClient(handler),
            tools,
            new ContextProvider { WorkingDirectory = temp.Root },
            new DefaultPermissionChecker(),
            new QueryEngineConfig
            {
                UseStreamingApi = true,
                MaxTurns = 1,
                EnableAutoCompact = false,
            },
            journal: new RecordingJournal());

        var events = await CollectAsync(engine.SubmitMessageAsync("find this"));

        Assert.Contains(events, e => e is TextDeltaEvent { Text: "Okay" });
        Assert.Contains(events, e => e is MessageEndEvent { StopReason: "tool_calls" or "tool_use" });
        Assert.True(Assert.IsType<QueryCompleteEvent>(events[^1]).Success);
    }

    private static async Task<List<QueryEvent>> CollectAsync(IAsyncEnumerable<QueryEvent> events)
    {
        var result = new List<QueryEvent>();
        await foreach (var evt in events)
            result.Add(evt);
        return result;
    }

    private static HttpResponseMessage CreateStreamingResponse(params (string EventName, object Payload)[] events)
    {
        var builder = new StringBuilder();
        foreach (var (eventName, payload) in events)
        {
            builder.Append("event: ").Append(eventName).Append('\n');
            builder.Append("data: ").Append(JsonSerializer.Serialize(payload)).Append("\n\n");
        }

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(builder.ToString(), Encoding.UTF8, "text/event-stream"),
        };
    }
}

/// <summary>
/// In-memory IChatClient that yields pre-defined streaming updates.
/// Tests the QueryEngine streaming path without HTTP/SSE dependencies.
/// </summary>
internal sealed class FakeStreamingChatClient(IReadOnlyList<ChatResponseUpdate> updates) : IChatClient
{
    public ChatClientMetadata Metadata => new("fake");

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = updates.ToChatResponse();
        return Task.FromResult(response);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var update in updates)
        {
            await Task.CompletedTask;
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

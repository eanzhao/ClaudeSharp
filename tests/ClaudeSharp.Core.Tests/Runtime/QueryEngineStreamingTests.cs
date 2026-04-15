using System.Net;
using System.Text;
using System.Text.Json;
using ClaudeSharp.Core.Context;
using ClaudeSharp.Core.Memory;
using ClaudeSharp.Core.Messages;
using ClaudeSharp.Core.Permissions;
using ClaudeSharp.Core.Query;
using ClaudeSharp.Core.Storage;
using ClaudeSharp.Core.Tools;

namespace ClaudeSharp.Core.Tests.Runtime;

/// <summary>
/// Contains tests for query Engine Streaming.
/// </summary>
public sealed class QueryEngineStreamingTests
{
    [Fact]
    public async Task SubmitMessageAsync_UsesStreamingApiAndParsesToolUseDeltas()
    {
        using var temp = new TempDirectory();
        var handler = new FakeAnthropicHandler();
        handler.EnqueueResponse(CreateStreamingResponse(
            ("message_start", new
            {
                type = "message_start",
                message = new
                {
                    id = "msg-stream-1",
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
                    id = "toolu_01T1x1fJ34qAmk2tNTrN7Up6",
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
                    partial_json = "",
                },
            }),
            ("content_block_delta", new
            {
                type = "content_block_delta",
                index = 1,
                delta = new
                {
                    type = "input_json_delta",
                    partial_json = "{\"command\":",
                },
            }),
            ("content_block_delta", new
            {
                type = "content_block_delta",
                index = 1,
                delta = new
                {
                    type = "input_json_delta",
                    partial_json = "\"search\"}",
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
                    stop_sequence = (string?)null,
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
            TestSupport.CreateAnthropicClient(handler),
            tools,
            new ContextProvider
            {
                WorkingDirectory = temp.Root,
            },
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
        var messageStart = Assert.IsType<MessageStartEvent>(events[1]);
        var text = Assert.Single(events.OfType<TextDeltaEvent>(), evt => evt.Text == "Okay");
        var toolUse = Assert.Single(events.OfType<ToolUseStartEvent>());
        var messageEnd = Assert.Single(events.OfType<MessageEndEvent>());
        var maxTurn = Assert.Single(events.OfType<TextDeltaEvent>(), evt =>
            evt.Text.Contains("maximum turn limit of 1", StringComparison.Ordinal));

        Assert.Equal("msg-stream-1", messageStart.MessageId);
        Assert.Equal("Okay", text.Text);
        Assert.Equal("toolu_01T1x1fJ34qAmk2tNTrN7Up6", toolUse.ToolUseId);
        Assert.Equal("search", toolUse.ToolName);
        Assert.Equal("search", toolUse.Input.GetProperty("command").GetString());
        Assert.Equal("tool_use", messageEnd.StopReason);
        Assert.Equal(2, messageEnd.Usage?.OutputTokens);
        Assert.Contains("maximum turn limit of 1", maxTurn.Text);
        Assert.Contains("\"stream\":true", handler.Bodies[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SubmitMessageAsync_MergesMessageStartUsageWhenStreamingDeltaIsPartial()
    {
        using var temp = new TempDirectory();
        var handler = new FakeAnthropicHandler();
        handler.EnqueueResponse(CreateStreamingResponse(
            ("message_start", new
            {
                type = "message_start",
                message = new
                {
                    id = "msg-stream-usage",
                    type = "message",
                    role = "assistant",
                    model = ClaudeModels.DefaultMainModel,
                    content = Array.Empty<object>(),
                    stop_reason = (string?)null,
                    stop_sequence = (string?)null,
                    usage = new
                    {
                        input_tokens = 11,
                        output_tokens = 0,
                        cache_read_input_tokens = 7,
                        cache_creation_input_tokens = 13,
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
                    text = "Hi",
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
                    stop_sequence = (string?)null,
                },
                usage = new
                {
                    output_tokens = 2,
                },
            }),
            ("message_stop", new
            {
                type = "message_stop",
            })));

        var engine = TestSupport.CreateQueryEngine(
            TestSupport.CreateAnthropicClient(handler),
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
            },
            journal: new RecordingJournal());

        var events = await CollectAsync(engine.SubmitMessageAsync("hello"));
        var messageEnd = Assert.Single(events.OfType<MessageEndEvent>());

        Assert.Equal(11, messageEnd.Usage?.InputTokens);
        Assert.Equal(2, messageEnd.Usage?.OutputTokens);
        Assert.Equal(7, messageEnd.Usage?.CacheReadInputTokens);
        Assert.Equal(13, messageEnd.Usage?.CacheCreationInputTokens);
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
            TestSupport.CreateAnthropicClient(new FakeAnthropicHandler()),
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
    public async Task SubmitMessageAsync_FinalizesToolUseBlockWhenStreamingStopEventIsMissing()
    {
        using var temp = new TempDirectory();
        var handler = new FakeAnthropicHandler();
        handler.EnqueueResponse(CreateStreamingResponse(
            ("message_start", new
            {
                type = "message_start",
                message = new
                {
                    id = "msg-stream-2",
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
                    type = "tool_use",
                    id = "toolu_missing_stop",
                    name = "search",
                    input = new { },
                },
            }),
            ("content_block_delta", new
            {
                type = "content_block_delta",
                index = 0,
                delta = new
                {
                    type = "input_json_delta",
                    partial_json = "{\"command\":\"fallback\"}",
                },
            }),
            ("message_delta", new
            {
                type = "message_delta",
                delta = new
                {
                    stop_reason = "tool_use",
                    stop_sequence = (string?)null,
                },
                usage = new
                {
                    input_tokens = 1,
                    output_tokens = 1,
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
            TestSupport.CreateAnthropicClient(handler),
            tools,
            new ContextProvider
            {
                WorkingDirectory = temp.Root,
            },
            new DefaultPermissionChecker(),
            new QueryEngineConfig
            {
                UseStreamingApi = true,
                MaxTurns = 1,
                EnableAutoCompact = false,
            },
            journal: new RecordingJournal());

        var events = await CollectAsync(engine.SubmitMessageAsync("find this"));

        var toolUse = Assert.Single(events.OfType<ToolUseStartEvent>());
        Assert.Equal("toolu_missing_stop", toolUse.ToolUseId);
        Assert.Equal("fallback", toolUse.Input.GetProperty("command").GetString());
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

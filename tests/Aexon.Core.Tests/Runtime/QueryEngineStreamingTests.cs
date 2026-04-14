using System.Runtime.CompilerServices;
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

    private static async Task<List<QueryEvent>> CollectAsync(IAsyncEnumerable<QueryEvent> events)
    {
        var result = new List<QueryEvent>();
        await foreach (var evt in events)
            result.Add(evt);
        return result;
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

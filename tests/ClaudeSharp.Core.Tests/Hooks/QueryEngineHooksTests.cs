using System.Net;
using System.Text;
using System.Text.Json;
using Anthropic;
using ClaudeSharp.Core.Compaction;
using ClaudeSharp.Core.Context;
using ClaudeSharp.Core.Hooks;
using ClaudeSharp.Core.Messages;
using ClaudeSharp.Core.Permissions;
using ClaudeSharp.Core.Query;
using ClaudeSharp.Core.Storage;
using ClaudeSharp.Core.Tests.Runtime;
using ClaudeSharp.Core.Tools;

namespace ClaudeSharp.Core.Tests.Hooks;

/// <summary>
/// Contains tests for query Engine Hooks.
/// </summary>
public sealed class QueryEngineHooksTests
{
    [Fact]
    public async Task SubmitMessageAsync_FiresSessionStartStopAndClearEnd()
    {
        using var temp = new TempDirectory();
        var hooks = new HookRuntime();
        var observer = new CountingHookObserver();
        hooks.Register(observer);

        var engine = CreateEngine(
            temp.Root,
            hooks,
            new RecordingJournal(),
            client: TestSupport.CreateAnthropicClient(new SingleResponseHandler(FakeAnthropicHandler.CreateMessageResponse("hello"))));

        var events = await CollectAsync(engine.SubmitMessageAsync("ping"));
        await engine.ClearMessagesAsync();

        Assert.Contains(events, evt => evt is QueryCompleteEvent complete && complete.Success);
        Assert.Equal(1, observer.SessionStartCount);
        Assert.Equal(1, observer.StopCount);
        Assert.Equal(1, observer.SessionEndCount);
        Assert.True(observer.LastStop?.Success);
        Assert.True(observer.LastSessionEnd?.DueToClear);
    }

    [Fact]
    public async Task CompactAsync_FiresPreAndPostCompactHooks()
    {
        using var temp = new TempDirectory();
        var hooks = new HookRuntime();
        var observer = new CountingHookObserver();
        hooks.Register(observer);

        var engine = CreateEngine(
            temp.Root,
            hooks,
            new RecordingJournal(),
            initialMessages:
            [
                UserMessage.FromText("one"),
                new AssistantMessage { Content = [new TextBlock("two")] },
                UserMessage.FromText("three"),
                new AssistantMessage { Content = [new TextBlock("four")] },
            ]);

        var result = await engine.CompactAsync(2);

        Assert.NotNull(result);
        Assert.Equal(1, observer.PreCompactCount);
        Assert.Equal(1, observer.PostCompactCount);
        Assert.Equal(CompactionLifecycleKind.Conversation, observer.LastPostCompact?.KindOfCompaction);
        Assert.NotNull(observer.LastPostCompact?.ConversationResult);
    }

    [Fact]
    public async Task AutoCompaction_FiresLifecycleHooksAndSessionResetEndsSession()
    {
        using var temp = new TempDirectory();
        var hooks = new HookRuntime();
        var observer = new CountingHookObserver();
        hooks.Register(observer);

        var engine = CreateEngine(
            temp.Root,
            hooks,
            new RecordingJournal(),
            client: TestSupport.CreateAnthropicClient(new SingleResponseHandler(FakeAnthropicHandler.CreateMessageResponse("hello"))),
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

        await CollectAsync(engine.SubmitMessageAsync("new request"));

        Assert.Equal(1, observer.SessionStartCount);
        Assert.True(observer.PreCompactCount >= 1);
        Assert.True(observer.PostCompactCount >= 1);
        Assert.Equal(1, observer.StopCount);

        await engine.ClearMessagesAsync();

        Assert.Equal(1, observer.SessionEndCount);
        Assert.True(observer.LastSessionEnd?.DueToClear);
    }

    private static QueryEngine CreateEngine(
        string workingDirectory,
        IHookRuntime hooks,
        RecordingJournal journal,
        AnthropicClient? client = null,
        IReadOnlyList<ConversationMessage>? initialMessages = null,
        QueryEngineConfig? config = null)
    {
        var provider = new ContextProvider
        {
            WorkingDirectory = workingDirectory,
        };

        return TestSupport.CreateQueryEngine(
            client ?? TestSupport.CreateAnthropicClient(new SingleResponseHandler(FakeAnthropicHandler.CreateMessageResponse("ok"))),
            new ToolRegistry(),
            provider,
            new DefaultPermissionChecker(),
            config ?? new QueryEngineConfig
            {
                EnableAutoCompact = false,
            },
            journal: journal,
            initialMessages: initialMessages,
            hooks: hooks);
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

    private static async Task<List<QueryEvent>> CollectAsync(IAsyncEnumerable<QueryEvent> events)
    {
        var result = new List<QueryEvent>();
        await foreach (var evt in events)
            result.Add(evt);
        return result;
    }

    private sealed class CountingHookObserver : HookObserver
    {
        public int SessionStartCount { get; private set; }
        public int SessionEndCount { get; private set; }
        public int StopCount { get; private set; }
        public int PreCompactCount { get; private set; }
        public int PostCompactCount { get; private set; }
        public StopHookContext? LastStop { get; private set; }
        public SessionEndHookContext? LastSessionEnd { get; private set; }
        public CompactHookContext? LastPostCompact { get; private set; }

        public override ValueTask OnSessionStartAsync(SessionHookContext context, CancellationToken cancellationToken = default)
        {
            SessionStartCount++;
            return ValueTask.CompletedTask;
        }

        public override ValueTask OnSessionEndAsync(SessionEndHookContext context, CancellationToken cancellationToken = default)
        {
            SessionEndCount++;
            LastSessionEnd = context;
            return ValueTask.CompletedTask;
        }

        public override ValueTask OnStopAsync(StopHookContext context, CancellationToken cancellationToken = default)
        {
            StopCount++;
            LastStop = context;
            return ValueTask.CompletedTask;
        }

        public override ValueTask OnPreCompactAsync(CompactHookContext context, CancellationToken cancellationToken = default)
        {
            PreCompactCount++;
            return ValueTask.CompletedTask;
        }

        public override ValueTask OnPostCompactAsync(CompactHookContext context, CancellationToken cancellationToken = default)
        {
            PostCompactCount++;
            LastPostCompact = context;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SingleResponseHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public SingleResponseHandler(HttpResponseMessage response)
        {
            _response = response;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_response);
    }
}

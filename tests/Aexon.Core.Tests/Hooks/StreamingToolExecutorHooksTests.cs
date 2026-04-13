using System.Text.Json;
using Aexon.Core.Hooks;
using Aexon.Core.Messages;
using Aexon.Core.Permissions;
using Aexon.Core.Tools;
using Aexon.Core.Tests.Runtime;

namespace Aexon.Core.Tests.Hooks;

/// <summary>
/// Contains tests for streaming Tool Executor Hooks.
/// </summary>
public sealed class StreamingToolExecutorHooksTests
{
    [Fact]
    public async Task PreToolUseCanBlockAndSkipToolExecution()
    {
        var observer = new BlockingHookObserver();
        var hooks = new HookRuntime([observer]);
        var tool = new FakeTool
        {
            Name = "search",
            PermissionHandler = (_, _) => Task.FromResult(PermissionResult.Allow()),
            ExecuteHandler = (_, _, _, _) =>
            {
                observer.Executed = true;
                return Task.FromResult(ToolResult.Success("done"));
            },
        };

        var runtime = new StreamingToolExecutor(
            BuildRegistry(tool),
            new DefaultPermissionChecker(),
            hooks);

        var updates = await CollectAsync(runtime.RunBatchAsync(
            [BuildToolUse("search")],
            CreateContext(tool)));

        var completed = Assert.Single(updates.OfType<ToolCompletedUpdate>());
        Assert.True(completed.Outcome.Result.IsError);
        Assert.Contains("blocked by hook", completed.Outcome.Result.Data, StringComparison.OrdinalIgnoreCase);
        Assert.False(observer.Executed);
        Assert.Equal(1, observer.PreToolUseCount);
        Assert.Equal(0, observer.PermissionRequestCount);
        Assert.Equal(0, observer.PostToolUseCount);
    }

    [Fact]
    public async Task PermissionRequestHookCanAutoApproveAndPostHookSeesResult()
    {
        var observer = new RecordingHookObserver
        {
            PermissionRequestHandler = _ => ValueTask.FromResult(PermissionRequestHookResult.Allow("approved")),
        };
        var hooks = new HookRuntime([observer]);
        var tool = new FakeTool
        {
            Name = "search",
            PermissionHandler = (_, _) => Task.FromResult(PermissionResult.Ask("need approval")),
            ExecuteHandler = (_, _, _, _) => Task.FromResult(ToolResult.Success("tool output")),
        };

        var runtime = new StreamingToolExecutor(
            BuildRegistry(tool),
            new DefaultPermissionChecker(),
            hooks);

        var updates = await CollectAsync(runtime.RunBatchAsync(
            [BuildToolUse("search")],
            CreateContext(tool)));

        Assert.DoesNotContain(updates, update => update is ToolPermissionRequestUpdate);
        var completed = Assert.Single(updates.OfType<ToolCompletedUpdate>());
        Assert.Equal("tool output", completed.Outcome.Result.Data);
        Assert.Equal(1, observer.PreToolUseCount);
        Assert.Equal(1, observer.PermissionRequestCount);
        Assert.Equal(1, observer.PostToolUseCount);
        Assert.Equal("tool output", observer.LastPostToolUseResult?.Result.Data);
    }

    private static ToolRegistry BuildRegistry(params ITool[] tools)
    {
        var registry = new ToolRegistry();
        foreach (var tool in tools)
            registry.Register(tool);
        return registry;
    }

    private static ToolUseBlock BuildToolUse(string toolName) =>
        new()
        {
            ToolUseId = $"{toolName}-id",
            Name = toolName,
            Input = JsonSerializer.SerializeToElement(new { command = toolName }),
        };

    private static ToolExecutionContext CreateContext(params ITool[] tools) =>
        new()
        {
            WorkingDirectory = "/tmp",
            PermissionContext = new PermissionContext(),
            Tools = tools,
            Messages = [],
            CancellationToken = CancellationToken.None,
        };

    private static async Task<List<ToolRunUpdate>> CollectAsync(IAsyncEnumerable<ToolRunUpdate> updates)
    {
        var result = new List<ToolRunUpdate>();
        await foreach (var update in updates)
            result.Add(update);
        return result;
    }

    private sealed class BlockingHookObserver : HookObserver
    {
        public int PreToolUseCount { get; private set; }
        public int PermissionRequestCount { get; private set; }
        public int PostToolUseCount { get; private set; }
        public bool Executed { get; set; }

        public override ValueTask<PreToolUseHookResult> OnPreToolUseAsync(
            PreToolUseHookContext context,
            CancellationToken cancellationToken = default)
        {
            PreToolUseCount++;
            return ValueTask.FromResult(PreToolUseHookResult.Block("blocked by hook"));
        }

        public override ValueTask<PermissionRequestHookResult> OnPermissionRequestAsync(
            PermissionRequestHookContext context,
            CancellationToken cancellationToken = default)
        {
            PermissionRequestCount++;
            return ValueTask.FromResult(PermissionRequestHookResult.NoDecision());
        }

        public override ValueTask OnPostToolUseAsync(
            PostToolUseHookContext context,
            CancellationToken cancellationToken = default)
        {
            PostToolUseCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingHookObserver : HookObserver
    {
        public int PreToolUseCount { get; private set; }
        public int PermissionRequestCount { get; private set; }
        public int PostToolUseCount { get; private set; }
        public PostToolUseHookContext? LastPostToolUseResult { get; private set; }
        public Func<PermissionRequestHookContext, ValueTask<PermissionRequestHookResult>>? PermissionRequestHandler { get; init; }

        public override ValueTask<PreToolUseHookResult> OnPreToolUseAsync(
            PreToolUseHookContext context,
            CancellationToken cancellationToken = default)
        {
            PreToolUseCount++;
            return ValueTask.FromResult(PreToolUseHookResult.Continue());
        }

        public override ValueTask<PermissionRequestHookResult> OnPermissionRequestAsync(
            PermissionRequestHookContext context,
            CancellationToken cancellationToken = default)
        {
            PermissionRequestCount++;
            return PermissionRequestHandler?.Invoke(context) ?? ValueTask.FromResult(PermissionRequestHookResult.NoDecision());
        }

        public override ValueTask OnPostToolUseAsync(
            PostToolUseHookContext context,
            CancellationToken cancellationToken = default)
        {
            PostToolUseCount++;
            LastPostToolUseResult = context;
            return ValueTask.CompletedTask;
        }
    }
}

using ClaudeSharp.Core.Messages;
using ClaudeSharp.Core.Permissions;
using ClaudeSharp.Core.Tools;

namespace ClaudeSharp.Core.Tests.Runtime;

public class StreamingToolExecutorTests
{
    [Fact]
    public async Task ConcurrentTools_StartTogetherBeforeEitherCompletes()
    {
        var started = 0;
        var allStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task<ToolResult> Handler(string name, JsonElement input, ToolExecutionContext context, IProgress<ToolProgress>? progress, CancellationToken ct)
        {
            if (Interlocked.Increment(ref started) == 2)
                allStarted.TrySetResult();

            return WaitAndReturn(name, release.Task, ct);
        }

        var toolA = new FakeTool
        {
            Name = "alpha",
            ConcurrencySafe = true,
            ExecuteHandler = (input, context, progress, ct) => Handler("alpha", input, context, progress, ct),
        };
        var toolB = new FakeTool
        {
            Name = "beta",
            ConcurrencySafe = true,
            ExecuteHandler = (input, context, progress, ct) => Handler("beta", input, context, progress, ct),
        };

        var runtime = new StreamingToolExecutor(
            BuildRegistry(toolA, toolB),
            new DefaultPermissionChecker());

        var updatesTask = CollectAsync(runtime.RunBatchAsync(
            [
                BuildToolUse("alpha"),
                BuildToolUse("beta"),
            ],
            BuildContext([toolA, toolB])));

        await allStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(2, started);
        release.SetResult();

        var updates = await updatesTask.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(2, updates.OfType<ToolCompletedUpdate>().Count());
        Assert.All(updates.OfType<ToolCompletedUpdate>(), update =>
        {
            Assert.False(update.Outcome.Result.IsError);
        });
    }

    [Fact]
    public async Task SequentialTools_RunInInputOrder()
    {
        var log = new List<string>();
        var toolA = new FakeTool
        {
            Name = "alpha",
            ConcurrencySafe = false,
            ExecuteHandler = (_, _, _, _) =>
            {
                log.Add("alpha");
                return Task.FromResult(ToolResult.Success("alpha ok"));
            },
        };
        var toolB = new FakeTool
        {
            Name = "beta",
            ConcurrencySafe = false,
            ExecuteHandler = (_, _, _, _) =>
            {
                log.Add("beta");
                return Task.FromResult(ToolResult.Success("beta ok"));
            },
        };

        var runtime = new StreamingToolExecutor(
            BuildRegistry(toolA, toolB),
            new DefaultPermissionChecker());

        var updates = await CollectAsync(runtime.RunBatchAsync(
            [
                BuildToolUse("alpha"),
                BuildToolUse("beta"),
            ],
            BuildContext([toolA, toolB])));

        Assert.Equal(["alpha", "beta"], log);
        Assert.Equal(["alpha", "beta"], updates
            .OfType<ToolCompletedUpdate>()
            .Select(update => update.Outcome.Invocation.Name));
    }

    [Fact]
    public async Task PermissionAsk_YieldsPermissionRequest_AndCanBeApproved()
    {
        var tool = new FakeTool
        {
            Name = "alpha",
            ExecuteHandler = (_, _, _, _) => Task.FromResult(ToolResult.Success("done")),
        };
        var runtime = new StreamingToolExecutor(
            BuildRegistry(tool),
            new StubPermissionChecker
            {
                Handler = (_, _, _) => Task.FromResult(PermissionResult.Ask("need approval")),
            });

        await using var enumerator = runtime.RunBatchAsync(
            [BuildToolUse("alpha")],
            BuildContext([tool]))
            .GetAsyncEnumerator();

        Assert.True(await enumerator.MoveNextAsync());
        var request = Assert.IsType<ToolPermissionRequestUpdate>(enumerator.Current);
        request.SetResponse(true);

        Assert.True(await enumerator.MoveNextAsync());
        var completed = Assert.IsType<ToolCompletedUpdate>(enumerator.Current);
        Assert.False(completed.Outcome.Result.IsError);
        Assert.False(await enumerator.MoveNextAsync());
    }

    [Fact]
    public async Task PermissionDeny_ReturnsErrorWithoutExecutingTool()
    {
        var executed = false;
        var tool = new FakeTool
        {
            Name = "alpha",
            ExecuteHandler = (_, _, _, _) =>
            {
                executed = true;
                return Task.FromResult(ToolResult.Success("done"));
            },
        };
        var runtime = new StreamingToolExecutor(
            BuildRegistry(tool),
            new StubPermissionChecker
            {
                Handler = (_, _, _) => Task.FromResult(PermissionResult.Deny("nope")),
            });

        var updates = await CollectAsync(runtime.RunBatchAsync(
            [BuildToolUse("alpha")],
            BuildContext([tool])));

        var completed = Assert.Single(updates.OfType<ToolCompletedUpdate>());
        Assert.True(completed.Outcome.Result.IsError);
        Assert.Contains("nope", completed.Outcome.Result.Data);
        Assert.False(executed);
    }

    [Fact]
    public async Task ResultIsTruncatedWhenToolExceedsLimit()
    {
        var tool = new FakeTool
        {
            Name = "alpha",
            MaxResultSize = 40,
            ExecuteHandler = (_, _, _, _) => Task.FromResult(ToolResult.Success(new string('x', 200))),
        };
        var runtime = new StreamingToolExecutor(
            BuildRegistry(tool),
            new DefaultPermissionChecker());

        var updates = await CollectAsync(runtime.RunBatchAsync(
            [BuildToolUse("alpha")],
            BuildContext([tool])));

        var completed = Assert.Single(updates.OfType<ToolCompletedUpdate>());
        Assert.True(completed.Outcome.Result.Data.Contains("truncated", StringComparison.OrdinalIgnoreCase));
        Assert.True(completed.Outcome.Result.Data.Length <= tool.MaxResultSize + 30);
    }

    [Fact]
    public async Task ResultIsNotTruncatedWhenLimitIsZero()
    {
        var tool = new FakeTool
        {
            Name = "alpha",
            MaxResultSize = 0,
            ExecuteHandler = (_, _, _, _) => Task.FromResult(ToolResult.Success(new string('x', 200))),
        };
        var runtime = new StreamingToolExecutor(
            BuildRegistry(tool),
            new DefaultPermissionChecker());

        var updates = await CollectAsync(runtime.RunBatchAsync(
            [BuildToolUse("alpha")],
            BuildContext([tool])));

        var completed = Assert.Single(updates.OfType<ToolCompletedUpdate>());
        Assert.False(completed.Outcome.Result.Data.Contains("truncated", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(200, completed.Outcome.Result.Data.Length);
    }

    [Fact]
    public async Task UpdatedInputFromPermissionResult_IsPassedToToolExecution()
    {
        var tool = new FakeTool
        {
            Name = "alpha",
            ExecuteHandler = (input, _, _, _) =>
            {
                Assert.Equal("git status --short", input.GetProperty("command").GetString());
                return Task.FromResult(ToolResult.Success("done"));
            },
        };
        var runtime = new StreamingToolExecutor(
            BuildRegistry(tool),
            new StubPermissionChecker
            {
                Handler = (_, _, _) =>
                    Task.FromResult(PermissionResult.Allow(TestSupport.Json(new { command = "git status --short" }))),
            });

        var updates = await CollectAsync(runtime.RunBatchAsync(
            [BuildToolUse("alpha")],
            BuildContext([tool])));

        Assert.Single(updates.OfType<ToolCompletedUpdate>());
        Assert.Single(tool.ExecutedInputs);
    }

    [Fact]
    public async Task OperationCanceledDuringExecution_IsConvertedToCancelledError()
    {
        var tool = new FakeTool
        {
            Name = "alpha",
            ExecuteHandler = (_, _, _, ct) => throw new OperationCanceledException(ct),
        };
        var runtime = new StreamingToolExecutor(
            BuildRegistry(tool),
            new DefaultPermissionChecker());

        var updates = await CollectAsync(runtime.RunBatchAsync(
            [BuildToolUse("alpha")],
            BuildContext([tool])));

        var completed = Assert.Single(updates.OfType<ToolCompletedUpdate>());
        Assert.True(completed.Outcome.Result.IsError);
        Assert.Contains("cancelled", completed.Outcome.Result.Data, StringComparison.OrdinalIgnoreCase);
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
            Input = TestSupport.Json(new { command = toolName }),
        };

    private static ToolExecutionContext BuildContext(IReadOnlyList<ITool> tools) =>
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

    private static async Task<ToolResult> WaitAndReturn(
        string name,
        Task gate,
        CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        return ToolResult.Success($"{name} ok");
    }
}

using Aexon.Core.Messages;
using Aexon.Core.Permissions;
using Aexon.Core.Tests.Runtime;
using Aexon.Core.Tools;

namespace Aexon.Core.Tests.Tools;

/// <summary>
/// Verifies that <see cref="ToolBatchExecutionMode"/> overrides per-tool
/// <see cref="ITool.IsConcurrencySafe"/> declarations on a batch.
/// </summary>
public class StreamingToolExecutorModeTests
{
    [Fact]
    public async Task Parallel_WithUnsafeTools_StartsAllBeforeAnyCompletes()
    {
        var started = 0;
        var allStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task<ToolResult> Handler(string name, CancellationToken ct)
        {
            if (Interlocked.Increment(ref started) == 3)
                allStarted.TrySetResult();
            return WaitAndReturn(name, release.Task, ct);
        }

        var toolA = new FakeTool
        {
            Name = "alpha",
            ConcurrencySafe = false,
            ExecuteHandler = (_, _, _, ct) => Handler("alpha", ct),
        };
        var toolB = new FakeTool
        {
            Name = "beta",
            ConcurrencySafe = false,
            ExecuteHandler = (_, _, _, ct) => Handler("beta", ct),
        };
        var toolC = new FakeTool
        {
            Name = "gamma",
            ConcurrencySafe = false,
            ExecuteHandler = (_, _, _, ct) => Handler("gamma", ct),
        };

        var runtime = new StreamingToolExecutor(
            BuildRegistry(toolA, toolB, toolC),
            new DefaultPermissionChecker(),
            mode: ToolBatchExecutionMode.Parallel);

        var updatesTask = CollectAsync(runtime.RunBatchAsync(
            [
                BuildToolUse("alpha"),
                BuildToolUse("beta"),
                BuildToolUse("gamma"),
            ],
            BuildContext([toolA, toolB, toolC])));

        await allStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(3, started);
        release.SetResult();

        var updates = await updatesTask.WaitAsync(TimeSpan.FromSeconds(2));
        var completions = updates.OfType<ToolCompletedUpdate>().ToList();
        Assert.Equal(3, completions.Count);
        Assert.All(completions, update => Assert.False(update.Outcome.Result.IsError));
    }

    [Fact]
    public async Task Sequential_WithSafeTools_RunsOneAtATimeInInputOrder()
    {
        var active = 0;
        var maxActive = 0;
        var log = new List<string>();
        var lockObj = new object();

        async Task<ToolResult> Handler(string name)
        {
            lock (lockObj)
            {
                active++;
                if (active > maxActive) maxActive = active;
                log.Add(name);
            }

            // Hold the "active" window long enough for parallel scheduling to
            // overlap if the mode override is not honoured.
            await Task.Delay(40);

            lock (lockObj)
            {
                active--;
            }

            return ToolResult.Success($"{name} ok");
        }

        var toolA = new FakeTool
        {
            Name = "alpha",
            ConcurrencySafe = true,
            ExecuteHandler = (_, _, _, _) => Handler("alpha"),
        };
        var toolB = new FakeTool
        {
            Name = "beta",
            ConcurrencySafe = true,
            ExecuteHandler = (_, _, _, _) => Handler("beta"),
        };
        var toolC = new FakeTool
        {
            Name = "gamma",
            ConcurrencySafe = true,
            ExecuteHandler = (_, _, _, _) => Handler("gamma"),
        };

        var runtime = new StreamingToolExecutor(
            BuildRegistry(toolA, toolB, toolC),
            new DefaultPermissionChecker(),
            mode: ToolBatchExecutionMode.Sequential);

        var updates = await CollectAsync(runtime.RunBatchAsync(
            [
                BuildToolUse("alpha"),
                BuildToolUse("beta"),
                BuildToolUse("gamma"),
            ],
            BuildContext([toolA, toolB, toolC])));

        Assert.Equal(1, maxActive);
        Assert.Equal(["alpha", "beta", "gamma"], log);
        Assert.Equal(3, updates.OfType<ToolCompletedUpdate>().Count());
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

    private static async Task<ToolResult> WaitAndReturn(string name, Task gate, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        return ToolResult.Success($"{name} ok");
    }
}

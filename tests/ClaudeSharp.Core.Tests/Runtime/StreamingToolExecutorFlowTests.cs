using System.Text.Json;
using ClaudeSharp.Core.Messages;
using ClaudeSharp.Core.Permissions;
using ClaudeSharp.Core.Tools;

namespace ClaudeSharp.Core.Tests.Runtime;

public sealed class StreamingToolExecutorFlowTests
{
    [Fact]
    public async Task UnknownTool_ReturnsErrorWithoutExecutingAnything()
    {
        var tool = new FakeTool
        {
            Name = "alpha",
        };
        var runtime = new StreamingToolExecutor(
            BuildRegistry(new ITool[] { tool }),
            new DefaultPermissionChecker());

        var updates = await CollectAsync(runtime.RunBatchAsync(
            [BuildToolUse("missing")],
            CreateContext(new ITool[] { tool })));

        var completed = Assert.Single(updates.OfType<ToolCompletedUpdate>());
        Assert.True(completed.Outcome.Result.IsError);
        Assert.Contains("Unknown tool", completed.Outcome.Result.Data);
        Assert.Empty(tool.ExecutedInputs);
    }

    [Fact]
    public async Task ValidationFailure_ReturnsErrorWithoutExecutingTool()
    {
        var tool = new FakeTool
        {
            Name = "alpha",
            ValidateHandler = (_, _) => Task.FromResult(ValidationResult.Invalid("bad input")),
        };
        var runtime = new StreamingToolExecutor(
            BuildRegistry(new ITool[] { tool }),
            new DefaultPermissionChecker());

        var updates = await CollectAsync(runtime.RunBatchAsync(
            [BuildToolUse("alpha")],
            CreateContext(new ITool[] { tool })));

        var completed = Assert.Single(updates.OfType<ToolCompletedUpdate>());
        Assert.True(completed.Outcome.Result.IsError);
        Assert.Contains("bad input", completed.Outcome.Result.Data);
        Assert.Empty(tool.ExecutedInputs);
    }

    [Fact]
    public async Task ToolException_IsConvertedToErrorResult()
    {
        var tool = new FakeTool
        {
            Name = "alpha",
            PermissionHandler = (_, _) => Task.FromResult(PermissionResult.Allow()),
            ExecuteHandler = (_, _, _, _) => throw new InvalidOperationException("boom"),
        };
        var runtime = new StreamingToolExecutor(
            BuildRegistry(new ITool[] { tool }),
            new DefaultPermissionChecker());

        var updates = await CollectAsync(runtime.RunBatchAsync(
            [BuildToolUse("alpha")],
            CreateContext(new ITool[] { tool })));

        var completed = Assert.Single(updates.OfType<ToolCompletedUpdate>());
        Assert.True(completed.Outcome.Result.IsError);
        Assert.Contains("boom", completed.Outcome.Result.Data);
        Assert.Single(tool.ExecutedInputs);
    }

    [Fact]
    public async Task UserDeniesPermission_StopsToolExecution()
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
            BuildRegistry(new ITool[] { tool }),
            new StubPermissionChecker
            {
                Handler = (_, _, _) => Task.FromResult(PermissionResult.Ask("approve?")),
            });

        await using var enumerator = runtime.RunBatchAsync(
            [BuildToolUse("alpha")],
            CreateContext(new ITool[] { tool }))
            .GetAsyncEnumerator();

        Assert.True(await enumerator.MoveNextAsync());
        var request = Assert.IsType<ToolPermissionRequestUpdate>(enumerator.Current);
        request.SetResponse(false);

        Assert.True(await enumerator.MoveNextAsync());
        var completed = Assert.IsType<ToolCompletedUpdate>(enumerator.Current);
        Assert.True(completed.Outcome.Result.IsError);
        Assert.Contains("User denied", completed.Outcome.Result.Data);
        Assert.False(executed);
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
}

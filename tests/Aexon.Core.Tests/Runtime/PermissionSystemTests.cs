using Aexon.Core.Permissions;
using Aexon.Core.Tools;

namespace Aexon.Core.Tests.Runtime;

/// <summary>
/// Contains tests for permission System.
/// </summary>
public class PermissionSystemTests
{
    [Fact]
    public async Task DenyRule_WinsBeforeToolPermission()
    {
        var checker = new DefaultPermissionChecker();
        var context = CreateContext(PermissionMode.Default);
        context.PermissionContext.AddRule(PermissionBehavior.Deny, "Shell", "git status");

        var tool = new FakeTool
        {
            Name = "Shell",
            PermissionHandler = (_, _) => Task.FromResult(PermissionResult.Allow()),
        };

        var result = await checker.CheckAsync(tool, TestSupport.Json(new { command = "git status" }), context);

        Assert.Equal(PermissionBehavior.Deny, result.Behavior);
        Assert.Contains("Denied by rule", result.Message);
    }

    [Fact]
    public async Task AskRule_ReturnsAsk()
    {
        var checker = new DefaultPermissionChecker();
        var context = CreateContext(PermissionMode.Default);
        context.PermissionContext.AddRule(PermissionBehavior.Ask, "Shell", "git status");

        var tool = new FakeTool
        {
            Name = "Shell",
            PermissionHandler = (_, _) => Task.FromResult(PermissionResult.Allow()),
        };

        var result = await checker.CheckAsync(tool, TestSupport.Json(new { command = "git status" }), context);

        Assert.Equal(PermissionBehavior.Ask, result.Behavior);
        Assert.Contains("Rule requires confirmation", result.Message);
    }

    [Fact]
    public async Task PlanMode_AllowsReadOnlyTools()
    {
        var checker = new DefaultPermissionChecker();
        var context = CreateContext(PermissionMode.Plan);

        var tool = new FakeTool
        {
            Name = "ReadTool",
            ReadOnly = true,
            PermissionHandler = (_, _) => Task.FromResult(PermissionResult.Ask("need approval")),
        };

        var result = await checker.CheckAsync(tool, TestSupport.Json(new { path = "README.md" }), context);

        Assert.Equal(PermissionBehavior.Allow, result.Behavior);
    }

    [Fact]
    public async Task BypassMode_AllowsEvenWhenToolWouldAsk()
    {
        var checker = new DefaultPermissionChecker();
        var context = CreateContext(PermissionMode.Bypass);

        var tool = new FakeTool
        {
            Name = "Shell",
            PermissionHandler = (_, _) => Task.FromResult(PermissionResult.Ask("need approval")),
        };

        var result = await checker.CheckAsync(tool, TestSupport.Json(new { command = "touch file.txt" }), context);

        Assert.Equal(PermissionBehavior.Allow, result.Behavior);
    }

    [Fact]
    public async Task ToolPermissionDeny_WinsBeforeAnyRules()
    {
        var checker = new DefaultPermissionChecker();
        var context = CreateContext(PermissionMode.Default);

        var tool = new FakeTool
        {
            Name = "Shell",
            PermissionHandler = (_, _) => Task.FromResult(PermissionResult.Deny("tool says no")),
        };

        var result = await checker.CheckAsync(tool, TestSupport.Json(new { command = "touch file.txt" }), context);

        Assert.Equal(PermissionBehavior.Deny, result.Behavior);
        Assert.Contains("tool says no", result.Message);
    }

    [Fact]
    public async Task AllowRule_ReturnsAllowBeforeAskFallback()
    {
        var checker = new DefaultPermissionChecker();
        var context = CreateContext(PermissionMode.Default);
        context.PermissionContext.AddRule(PermissionBehavior.Allow, "Shell", "git status");

        var tool = new FakeTool
        {
            Name = "Shell",
            PermissionHandler = (_, _) => Task.FromResult(PermissionResult.Ask("need approval")),
        };

        var result = await checker.CheckAsync(tool, TestSupport.Json(new { command = "git status" }), context);

        Assert.Equal(PermissionBehavior.Allow, result.Behavior);
    }

    [Fact]
    public async Task AutoMode_AllowsNonReadOnlyTools()
    {
        var checker = new DefaultPermissionChecker();
        var context = CreateContext(PermissionMode.Auto);

        var tool = new FakeTool
        {
            Name = "Shell",
            PermissionHandler = (_, _) => Task.FromResult(PermissionResult.Ask("need approval")),
        };

        var result = await checker.CheckAsync(tool, TestSupport.Json(new { command = "echo hi" }), context);

        Assert.Equal(PermissionBehavior.Allow, result.Behavior);
    }

    [Fact]
    public async Task ToolPermissionAllowWithUpdatedInput_IsPreserved()
    {
        var checker = new DefaultPermissionChecker();
        var context = CreateContext(PermissionMode.Default);
        var updatedInput = TestSupport.Json(new { command = "git status --short" });

        var tool = new FakeTool
        {
            Name = "Shell",
            PermissionHandler = (_, _) => Task.FromResult(PermissionResult.Allow(updatedInput)),
        };

        var result = await checker.CheckAsync(tool, TestSupport.Json(new { command = "git status" }), context);

        Assert.Equal(PermissionBehavior.Allow, result.Behavior);
        Assert.True(result.UpdatedInput.HasValue);
        Assert.Equal("git status --short", result.UpdatedInput!.Value.GetProperty("command").GetString());
    }

    private static ToolExecutionContext CreateContext(PermissionMode mode)
    {
        return new ToolExecutionContext
        {
            WorkingDirectory = "/tmp",
            PermissionContext = new PermissionContext { Mode = mode },
            Tools = [],
            Messages = [],
            CancellationToken = CancellationToken.None,
        };
    }
}

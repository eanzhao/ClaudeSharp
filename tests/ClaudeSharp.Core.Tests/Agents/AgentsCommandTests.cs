using ClaudeSharp.Commands;
using ClaudeSharp.Core.Agents;
using ClaudeSharp.Core.Commands;
using ClaudeSharp.Core.Permissions;
using ClaudeSharp.Core.Tools;

namespace ClaudeSharp.Core.Tests.Agents;

/// <summary>
/// Contains tests for the agents slash command.
/// </summary>
public sealed class AgentsCommandTests
{
    [Fact]
    public async Task ExecuteAsync_CanFilterAndPaginateOverviewResults()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        runtime.CreateWorkItem("Inspect parser", owner: "subagent");

        var firstQueued = runtime.StartBackgroundRun(
            "First queued run",
            owner: "subagent",
            initialStatus: AgentBackgroundRunStatus.Queued);
        runtime.StartBackgroundRun(
            "Second queued run",
            owner: "subagent",
            initialStatus: AgentBackgroundRunStatus.Queued);
        runtime.AppendBackgroundRunOutput(firstQueued.Id, "queued");

        var lines = new List<string>();
        var command = new AgentsCommand();

        await command.ExecuteAsync(
            "list --kind background_runs --status queued --offset 1 --limit 1",
            CreateContext(runtime, lines));

        var output = string.Join(Environment.NewLine, lines);
        Assert.Contains("Background runs:", output, StringComparison.Ordinal);
        Assert.Contains("Showing background runs 2-2 of 2.", output, StringComparison.Ordinal);
        Assert.DoesNotContain(firstQueued.Id, output, StringComparison.Ordinal);
        Assert.Contains("background-run-2", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_ReportsInvalidOverviewArguments()
    {
        var lines = new List<string>();
        var command = new AgentsCommand();

        await command.ExecuteAsync(
            "list --limit 0",
            CreateContext(new InMemoryAgentTaskRuntime(), lines));

        var output = string.Join(Environment.NewLine, lines);
        Assert.Contains("--limit must be a positive integer", output, StringComparison.Ordinal);
        Assert.Contains("Usage: /agents", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_CanRenderSummary()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var completed = runtime.CreateWorkItem("Completed task", owner: "subagent");
        runtime.UpdateWorkItem(completed.Id, item => item.Status = AgentWorkItemStatus.Completed);
        var blocked = runtime.CreateWorkItem("Blocked task", owner: "subagent");
        runtime.UpdateWorkItem(blocked.Id, item => item.Status = AgentWorkItemStatus.Blocked);

        var running = runtime.StartBackgroundRun(
            "Running run",
            owner: "subagent",
            workItemId: blocked.Id,
            initialStatus: AgentBackgroundRunStatus.Running);
        var stopped = runtime.StartBackgroundRun(
            "Stopped run",
            owner: "subagent",
            workItemId: completed.Id,
            initialStatus: AgentBackgroundRunStatus.Stopped);
        runtime.UpdateBackgroundRun(stopped.Id, run => run.StopReason = "completed");

        var lines = new List<string>();
        var command = new AgentsCommand();

        await command.ExecuteAsync(
            "summary --owner subagent --recent-limit 2",
            CreateContext(runtime, lines));

        var output = string.Join(Environment.NewLine, lines);
        Assert.Contains("Agent summary (owner: subagent):", output, StringComparison.Ordinal);
        Assert.Contains("Work items: 2", output, StringComparison.Ordinal);
        Assert.Contains("- Completed: 1", output, StringComparison.Ordinal);
        Assert.Contains("- Blocked: 1", output, StringComparison.Ordinal);
        Assert.Contains("Background runs: 2", output, StringComparison.Ordinal);
        Assert.Contains("- Running: 1", output, StringComparison.Ordinal);
        Assert.Contains("- Stopped: 1", output, StringComparison.Ordinal);
        Assert.Contains("Active background runs:", output, StringComparison.Ordinal);
        Assert.Contains(running.Id, output, StringComparison.Ordinal);
        Assert.Contains("Recent finished background runs:", output, StringComparison.Ordinal);
        Assert.Contains(stopped.Id, output, StringComparison.Ordinal);
    }

    private static CommandContext CreateContext(
        IAgentTaskRuntime runtime,
        List<string> lines) =>
        new()
        {
            WriteLine = lines.Add,
            Tools = new ToolRegistry(),
            QueryEngine = null!,
            PermissionContext = new PermissionContext(),
            AgentTaskRuntime = runtime,
            Commands = [],
        };
}

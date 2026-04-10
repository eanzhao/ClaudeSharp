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
        var awaiting = runtime.CreateWorkItem("Awaiting approval", owner: "subagent");
        runtime.UpdateWorkItem(awaiting.Id, item => item.Status = AgentWorkItemStatus.AwaitingApproval);
        var awaitingResume = runtime.CreateWorkItem("Awaiting resume", owner: "subagent");
        runtime.UpdateWorkItem(awaitingResume.Id, item => item.Status = AgentWorkItemStatus.AwaitingResume);
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
            "summary --owner subagent --recent-limit 4",
            CreateContext(runtime, lines));

        var output = string.Join(Environment.NewLine, lines);
        Assert.Contains("Agent summary (owner: subagent):", output, StringComparison.Ordinal);
        Assert.Contains("Work items: 4", output, StringComparison.Ordinal);
        Assert.Contains("- Completed: 1", output, StringComparison.Ordinal);
        Assert.Contains("- AwaitingApproval: 1", output, StringComparison.Ordinal);
        Assert.Contains("- AwaitingResume: 1", output, StringComparison.Ordinal);
        Assert.Contains("- Blocked: 1", output, StringComparison.Ordinal);
        Assert.Contains("Background runs: 2", output, StringComparison.Ordinal);
        Assert.Contains("- Running: 1", output, StringComparison.Ordinal);
        Assert.Contains("- Stopped: 1", output, StringComparison.Ordinal);
        Assert.Contains("Active background runs:", output, StringComparison.Ordinal);
        Assert.Contains(running.Id, output, StringComparison.Ordinal);
        Assert.Contains("Recent finished background runs:", output, StringComparison.Ordinal);
        Assert.Contains(stopped.Id, output, StringComparison.Ordinal);
        Assert.Contains(awaiting.Id, output, StringComparison.Ordinal);
        Assert.Contains(awaitingResume.Id, output, StringComparison.Ordinal);
        Assert.Contains("Needs attention:", output, StringComparison.Ordinal);
        Assert.Contains("Wait for subagent to finish background-run-1", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_CanRenderAttentionView()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var awaitingApproval = runtime.CreateWorkItem("Awaiting approval", owner: "subagent");
        runtime.UpdateWorkItem(awaitingApproval.Id, item =>
        {
            item.Status = AgentWorkItemStatus.AwaitingApproval;
            item.ApprovalRequestId = "agent-message-1";
        });
        var awaitingResume = runtime.CreateWorkItem("Awaiting resume", owner: "subagent");
        runtime.UpdateWorkItem(awaitingResume.Id, item =>
        {
            item.Status = AgentWorkItemStatus.AwaitingResume;
            item.Owner = "subagent";
        });
        runtime.StartBackgroundRun(
            "Current run",
            owner: "subagent",
            workItemId: awaitingResume.Id,
            initialStatus: AgentBackgroundRunStatus.Running);

        var lines = new List<string>();
        var command = new AgentsCommand();

        await command.ExecuteAsync(
            "attention --owner subagent --limit 5",
            CreateContext(runtime, lines));

        var output = string.Join(Environment.NewLine, lines);
        Assert.Contains("Agent attention (owner: subagent):", output, StringComparison.Ordinal);
        Assert.Contains(awaitingApproval.Id, output, StringComparison.Ordinal);
        Assert.Contains("Summary: Waiting for approval response.", output, StringComparison.Ordinal);
        Assert.Contains("Next: Run /mailbox respond agent-message-1 approve|reject", output, StringComparison.Ordinal);
        Assert.Contains(awaitingResume.Id, output, StringComparison.Ordinal);
        Assert.Contains("Active run: background-run-1", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_ReportsInvalidAttentionArguments()
    {
        var lines = new List<string>();
        var command = new AgentsCommand();

        await command.ExecuteAsync(
            "attention --limit 0",
            CreateContext(new InMemoryAgentTaskRuntime(), lines));

        var output = string.Join(Environment.NewLine, lines);
        Assert.Contains("--limit must be a positive integer", output, StringComparison.Ordinal);
        Assert.Contains("Usage: /agents attention", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_CanResumeApprovedWorkItem()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var messages = new InMemoryAgentMessageRuntime();
        var activations = new InMemoryAgentMessageActivationRuntime();
        var request = messages.SendMessage(
            "subagent",
            "main",
            AgentMessageKind.PlanApprovalRequest,
            "Approve this runtime plan",
            subject: "Runtime plan");
        var approval = messages.SendMessage(
            "main",
            "subagent",
            AgentMessageKind.PlanApprovalResponse,
            "Looks good",
            relatedMessageId: request.Id,
            protocol: new AgentMessageProtocol
            {
                ActionName = "plan-approval-approved",
            });
        var workItem = runtime.CreateWorkItem("Inspect runtime", owner: "subagent");
        runtime.UpdateWorkItem(workItem.Id, item =>
        {
            item.Status = AgentWorkItemStatus.AwaitingResume;
            item.ApprovalRequestId = request.Id;
            item.ApprovalThreadId = request.ThreadId;
        });
        activations.RegisterOwner(
            "subagent",
            (trigger, _) =>
            {
                Assert.Equal(approval.Id, trigger.Message.Id);
                AgentWorkItemApprovalCoordinator.TryResumeApprovedWorkItem(runtime, workItem.Id);
                return Task.FromResult(AgentMessageActivationResult.Reactivated(
                    "subagent",
                    "background-run-9",
                    workItem.Id,
                    "Triggered by approval."));
            });

        var lines = new List<string>();
        var command = new AgentsCommand();

        await command.ExecuteAsync(
            $"resume {workItem.Id}",
            CreateContext(runtime, lines, messageRuntime: messages, activationRuntime: activations));

        var output = string.Join(Environment.NewLine, lines);
        Assert.Contains($"Resumed {workItem.Id}.", output, StringComparison.Ordinal);
        Assert.Contains("Reactivated subagent as background-run-9", output, StringComparison.Ordinal);
        Assert.Equal(AgentWorkItemStatus.InProgress, runtime.GetWorkItem(workItem.Id)!.Status);
    }

    [Fact]
    public async Task ExecuteAsync_ReportsInvalidResumeArguments()
    {
        var lines = new List<string>();
        var command = new AgentsCommand();

        await command.ExecuteAsync(
            "resume",
            CreateContext(new InMemoryAgentTaskRuntime(), lines));

        var output = string.Join(Environment.NewLine, lines);
        Assert.Contains("Usage: /agents resume <work-item-id>", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_CanTailRecentBackgroundRunOutput()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var run = runtime.StartBackgroundRun("Inspect runtime", owner: "subagent");
        runtime.AppendBackgroundRunOutput(run.Id, "line 1");
        runtime.AppendBackgroundRunOutput(run.Id, "line 2");
        runtime.AppendBackgroundRunOutput(run.Id, "line 3");
        runtime.StopBackgroundRun(run.Id, "completed");

        var lines = new List<string>();
        var command = new AgentsCommand();

        await command.ExecuteAsync(
            $"tail {run.Id} --last 2",
            CreateContext(runtime, lines));

        var output = string.Join(Environment.NewLine, lines);
        Assert.Contains($"Background run output: {run.Id}", output, StringComparison.Ordinal);
        Assert.Contains("Showing output entries 2-3 of 3.", output, StringComparison.Ordinal);
        Assert.DoesNotContain("line 1", output, StringComparison.Ordinal);
        Assert.Contains("line 2", output, StringComparison.Ordinal);
        Assert.Contains("line 3", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_CanFollowBackgroundRunUntilCompletion()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var run = runtime.StartBackgroundRun("Inspect runtime", owner: "subagent");
        runtime.AppendBackgroundRunOutput(run.Id, "line 1");

        var delayCalls = 0;
        var lines = new List<string>();
        var command = new AgentsCommand();

        await command.ExecuteAsync(
            $"tail {run.Id} --last 1 --follow --poll-ms 1",
            CreateContext(
                runtime,
                lines,
                async (_, _) =>
                {
                    delayCalls++;
                    if (delayCalls == 1)
                    {
                        runtime.AppendBackgroundRunOutput(run.Id, "line 2");
                        runtime.StopBackgroundRun(run.Id, "completed");
                    }

                    await Task.CompletedTask;
                }));

        var output = string.Join(Environment.NewLine, lines);
        Assert.Contains("line 1", output, StringComparison.Ordinal);
        Assert.Contains("[tail] Following", output, StringComparison.Ordinal);
        Assert.Contains("line 2", output, StringComparison.Ordinal);
        Assert.Contains($"[tail] {run.Id} finished with status Stopped.", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_ReportsInvalidTailArguments()
    {
        var lines = new List<string>();
        var command = new AgentsCommand();

        await command.ExecuteAsync(
            "tail background-run-1 --last 0",
            CreateContext(new InMemoryAgentTaskRuntime(), lines));

        var output = string.Join(Environment.NewLine, lines);
        Assert.Contains("--last must be a positive integer", output, StringComparison.Ordinal);
        Assert.Contains("Usage: /agents tail", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_CanPruneOldTerminalHistory()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var oldItem = runtime.CreateWorkItem("old item");
        runtime.UpdateWorkItem(oldItem.Id, item => item.Status = AgentWorkItemStatus.Completed);
        var oldRun = runtime.StartBackgroundRun(
            "old run",
            workItemId: oldItem.Id,
            initialStatus: AgentBackgroundRunStatus.Stopped);

        var newItem = runtime.CreateWorkItem("new item");
        runtime.UpdateWorkItem(newItem.Id, item => item.Status = AgentWorkItemStatus.Completed);
        var newRun = runtime.StartBackgroundRun(
            "new run",
            workItemId: newItem.Id,
            initialStatus: AgentBackgroundRunStatus.Stopped);

        var lines = new List<string>();
        var command = new AgentsCommand();

        await command.ExecuteAsync(
            "prune --keep-runs 1 --keep-work-items 0",
            CreateContext(runtime, lines));

        var output = string.Join(Environment.NewLine, lines);
        Assert.Contains("Pruned 1 background run(s) and 1 work item(s).", output, StringComparison.Ordinal);
        Assert.Null(runtime.GetBackgroundRun(oldRun.Id));
        Assert.Null(runtime.GetWorkItem(oldItem.Id));
        Assert.NotNull(runtime.GetBackgroundRun(newRun.Id));
        Assert.NotNull(runtime.GetWorkItem(newItem.Id));
    }

    [Fact]
    public async Task ExecuteAsync_ReportsInvalidPruneArguments()
    {
        var lines = new List<string>();
        var command = new AgentsCommand();

        await command.ExecuteAsync(
            "prune --keep-runs -1",
            CreateContext(new InMemoryAgentTaskRuntime(), lines));

        var output = string.Join(Environment.NewLine, lines);
        Assert.Contains("--keep-runs must be a non-negative integer", output, StringComparison.Ordinal);
        Assert.Contains("Usage: /agents prune", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_CanWaitForBackgroundRunCompletion()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var run = runtime.StartBackgroundRun("Inspect runtime", owner: "subagent");
        runtime.AppendBackgroundRunOutput(run.Id, "line 1");

        var delayCalls = 0;
        var lines = new List<string>();
        var command = new AgentsCommand();

        await command.ExecuteAsync(
            $"wait {run.Id} --poll-ms 1 --include-output",
            CreateContext(
                runtime,
                lines,
                async (_, _) =>
                {
                    delayCalls++;
                    if (delayCalls == 1)
                    {
                        runtime.AppendBackgroundRunOutput(run.Id, "line 2");
                        runtime.StopBackgroundRun(run.Id, "completed");
                    }

                    await Task.CompletedTask;
                }));

        var output = string.Join(Environment.NewLine, lines);
        Assert.Contains($"  {run.Id} finished with status Stopped", output, StringComparison.Ordinal);
        Assert.Contains("Background run:", output, StringComparison.Ordinal);
        Assert.Contains("line 1", output, StringComparison.Ordinal);
        Assert.Contains("line 2", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_CanTimeOutWhileWaiting()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var run = runtime.StartBackgroundRun("Inspect runtime", owner: "subagent");
        var lines = new List<string>();
        var command = new AgentsCommand();

        await command.ExecuteAsync(
            $"wait {run.Id} --poll-ms 1 --timeout-ms 2",
            CreateContext(
                runtime,
                lines,
                (_, _) => Task.CompletedTask));

        var output = string.Join(Environment.NewLine, lines);
        Assert.Contains("Timed out after", output, StringComparison.Ordinal);
        Assert.Contains(run.Id, output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_ReportsInvalidWaitArguments()
    {
        var lines = new List<string>();
        var command = new AgentsCommand();

        await command.ExecuteAsync(
            "wait background-run-1 --timeout-ms 0",
            CreateContext(new InMemoryAgentTaskRuntime(), lines));

        var output = string.Join(Environment.NewLine, lines);
        Assert.Contains("--timeout-ms must be a positive integer", output, StringComparison.Ordinal);
        Assert.Contains("Usage: /agents wait", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_CanWaitForAnyOfMultipleRuns()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var firstRun = runtime.StartBackgroundRun("Inspect runtime A", owner: "subagent");
        var secondRun = runtime.StartBackgroundRun("Inspect runtime B", owner: "subagent");
        runtime.AppendBackgroundRunOutput(secondRun.Id, "line 2");

        var delayCalls = 0;
        var lines = new List<string>();
        var command = new AgentsCommand();

        await command.ExecuteAsync(
            $"wait any {firstRun.Id} {secondRun.Id} --poll-ms 1 --include-output",
            CreateContext(
                runtime,
                lines,
                async (_, _) =>
                {
                    delayCalls++;
                    if (delayCalls == 1)
                        runtime.StopBackgroundRun(secondRun.Id, "completed");

                    await Task.CompletedTask;
                }));

        var output = string.Join(Environment.NewLine, lines);
        Assert.Contains("Wait finished after", output, StringComparison.Ordinal);
        Assert.Contains("Completed runs:", output, StringComparison.Ordinal);
        Assert.Contains($"{secondRun.Id}: Stopped", output, StringComparison.Ordinal);
        Assert.Contains("Still running:", output, StringComparison.Ordinal);
        Assert.Contains($"{firstRun.Id}: Running", output, StringComparison.Ordinal);
        Assert.Contains("Background run:", output, StringComparison.Ordinal);
        Assert.Contains("line 2", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_CanWaitForAllOfMultipleRuns()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var firstRun = runtime.StartBackgroundRun("Inspect runtime A", owner: "subagent");
        var secondRun = runtime.StartBackgroundRun("Inspect runtime B", owner: "subagent");

        var delayCalls = 0;
        var lines = new List<string>();
        var command = new AgentsCommand();

        await command.ExecuteAsync(
            $"wait all {firstRun.Id} {secondRun.Id} --poll-ms 1",
            CreateContext(
                runtime,
                lines,
                async (_, _) =>
                {
                    delayCalls++;
                    if (delayCalls == 1)
                        runtime.StopBackgroundRun(firstRun.Id, "completed");
                    else if (delayCalls == 2)
                        runtime.StopBackgroundRun(secondRun.Id, "completed");

                    await Task.CompletedTask;
                }));

        var output = string.Join(Environment.NewLine, lines);
        Assert.Contains("All 2 background run(s) finished", output, StringComparison.Ordinal);
        Assert.Contains($"{firstRun.Id}: Stopped", output, StringComparison.Ordinal);
        Assert.Contains($"{secondRun.Id}: Stopped", output, StringComparison.Ordinal);
    }

    private static CommandContext CreateContext(
        IAgentTaskRuntime runtime,
        List<string> lines,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
        IAgentMessageRuntime? messageRuntime = null,
        IAgentMessageActivationRuntime? activationRuntime = null) =>
        new()
        {
            WriteLine = lines.Add,
            Tools = new ToolRegistry(),
            QueryEngine = null!,
            PermissionContext = new PermissionContext(),
            AgentTaskRuntime = runtime,
            AgentMessageRuntime = messageRuntime,
            AgentMessageActivationRuntime = activationRuntime,
            Commands = [],
            DelayAsync = delayAsync,
            CancellationToken = CancellationToken.None,
        };
}

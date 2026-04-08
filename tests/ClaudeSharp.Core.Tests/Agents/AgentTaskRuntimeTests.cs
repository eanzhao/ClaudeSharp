using ClaudeSharp.Core.Agents;

namespace ClaudeSharp.Core.Tests.Agents;

/// <summary>
/// Contains tests for agent Task Runtime.
/// </summary>
public sealed class AgentTaskRuntimeTests
{
    [Fact]
    public void WorkItemsAndBackgroundRunsStaySeparated()
    {
        var runtime = new InMemoryAgentTaskRuntime();

        var workItem = runtime.CreateWorkItem("Write tests", "cover new runtime");
        runtime.UpdateWorkItem(workItem.Id, item =>
        {
            item.Owner = "alice";
            item.Status = AgentWorkItemStatus.InProgress;
            item.AddBlock("task-2");
            item.AddBlockedBy("task-1");
        });

        var backgroundRun = runtime.StartBackgroundRun("nightly consolidation", "daemon");
        runtime.AppendBackgroundRunOutput(backgroundRun.Id, "started");
        runtime.AppendBackgroundRunOutput(backgroundRun.Id, "still running");
        runtime.StopBackgroundRun(backgroundRun.Id, "finished");

        Assert.Single(runtime.ListWorkItems());
        Assert.Single(runtime.ListBackgroundRuns());

        var fetchedWorkItem = Assert.Single(runtime.ListWorkItems());
        Assert.Equal("alice", fetchedWorkItem.Owner);
        Assert.Equal(AgentWorkItemStatus.InProgress, fetchedWorkItem.Status);
        Assert.Equal(["task-2"], fetchedWorkItem.Blocks);
        Assert.Equal(["task-1"], fetchedWorkItem.BlockedBy);

        var fetchedRun = Assert.Single(runtime.ListBackgroundRuns());
        Assert.Equal(AgentBackgroundRunStatus.Stopped, fetchedRun.Status);
        Assert.Equal("finished", fetchedRun.StopReason);
        Assert.Equal(["started", "still running"], fetchedRun.Output);
        Assert.NotNull(fetchedRun.StoppedAt);
    }

    [Fact]
    public void UpdateAndStopReturnFalseForMissingIds()
    {
        var runtime = new InMemoryAgentTaskRuntime();

        Assert.False(runtime.UpdateWorkItem("missing", _ => { }));
        Assert.False(runtime.UpdateBackgroundRun("missing", _ => { }));
        Assert.False(runtime.AppendBackgroundRunOutput("missing", "output"));
        Assert.False(runtime.RegisterBackgroundRunCancellation("missing", () => { }));
        Assert.Equal(
            AgentBackgroundRunCancellationResult.NotFound,
            runtime.RequestBackgroundRunCancellation("missing"));
        Assert.False(runtime.StopBackgroundRun("missing"));
        Assert.False(runtime.FailBackgroundRun("missing"));
        Assert.False(runtime.CancelBackgroundRun("missing"));
        Assert.Null(runtime.GetWorkItem("missing"));
        Assert.Null(runtime.GetBackgroundRun("missing"));
        Assert.Empty(runtime.ListWorkItems());
        Assert.Empty(runtime.ListBackgroundRuns());
    }

    [Fact]
    public void UpdateBackgroundRunAndFailTrackLatestState()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var run = runtime.StartBackgroundRun("research");

        Assert.True(runtime.UpdateBackgroundRun(run.Id, item => item.Owner = "subagent"));
        Assert.True(runtime.FailBackgroundRun(run.Id, "network timeout"));

        var stored = Assert.Single(runtime.ListBackgroundRuns());
        Assert.Equal("subagent", stored.Owner);
        Assert.Equal(AgentBackgroundRunStatus.Failed, stored.Status);
        Assert.Equal("network timeout", stored.StopReason);
        Assert.NotNull(stored.StoppedAt);
    }

    [Fact]
    public void RequestBackgroundRunCancellation_UpdatesStateAndInvokesController()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var run = runtime.StartBackgroundRun("research");
        var cancelled = false;

        Assert.True(runtime.RegisterBackgroundRunCancellation(run.Id, () => cancelled = true));

        var result = runtime.RequestBackgroundRunCancellation(run.Id, "user requested");

        Assert.Equal(AgentBackgroundRunCancellationResult.Requested, result);
        Assert.True(cancelled);

        var stored = Assert.Single(runtime.ListBackgroundRuns());
        Assert.Equal(AgentBackgroundRunStatus.CancellationRequested, stored.Status);
        Assert.Equal("user requested", stored.StopReason);
    }

    [Fact]
    public void CancelBackgroundRun_MarksTerminalState()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var run = runtime.StartBackgroundRun("research");

        Assert.True(runtime.CancelBackgroundRun(run.Id, "cancelled"));

        var stored = Assert.Single(runtime.ListBackgroundRuns());
        Assert.Equal(AgentBackgroundRunStatus.Cancelled, stored.Status);
        Assert.Equal("cancelled", stored.StopReason);
        Assert.NotNull(stored.StoppedAt);
    }

    [Fact]
    public void ListMethodsReturnStableOrdering()
    {
        var runtime = new InMemoryAgentTaskRuntime();

        var later = runtime.CreateWorkItem("later");
        var earlier = runtime.CreateWorkItem("earlier");
        runtime.StartBackgroundRun("zeta");
        runtime.StartBackgroundRun("alpha");

        Assert.Equal([later.Id, earlier.Id], runtime.ListWorkItems().Select(item => item.Id));
        Assert.Equal(2, runtime.ListBackgroundRuns().Count);
    }
}

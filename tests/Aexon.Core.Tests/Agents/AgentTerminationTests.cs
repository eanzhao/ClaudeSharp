using Aexon.Core.Agents;

namespace Aexon.Core.Tests.Agents;

public sealed class AgentTerminationTests
{
    [Fact]
    public void AllocateSubagentId_ReturnsIncrementingIds()
    {
        var runtime = new InMemoryAgentTaskRuntime();

        var first = runtime.AllocateSubagentId();
        var second = runtime.AllocateSubagentId();

        Assert.Equal("subagent-1", first);
        Assert.Equal("subagent-2", second);
    }

    [Fact]
    public void AllocateSubagentId_ContinuesFromRestoredState()
    {
        var restoredWorkItem = new AgentWorkItem
        {
            Id = "work-item-1",
            SubagentId = "subagent-5",
            Title = "restored",
        };

        var runtime = new InMemoryAgentTaskRuntime(workItems: [restoredWorkItem]);

        var next = runtime.AllocateSubagentId();

        Assert.Equal("subagent-6", next);
    }

    [Fact]
    public void CreateWorkItem_StoresSubagentId()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var subagentId = runtime.AllocateSubagentId();

        var item = runtime.CreateWorkItem("test", subagentId: subagentId);

        Assert.Equal(subagentId, item.SubagentId);
        Assert.Equal(subagentId, runtime.GetWorkItem(item.Id)!.SubagentId);
    }

    [Fact]
    public void StartBackgroundRun_StoresSubagentId()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var subagentId = runtime.AllocateSubagentId();

        var run = runtime.StartBackgroundRun("research", subagentId: subagentId);

        Assert.Equal(subagentId, run.SubagentId);
        Assert.Equal(subagentId, runtime.GetBackgroundRun(run.Id)!.SubagentId);
    }

    [Fact]
    public void StopBackgroundRun_WithTerminationInfo_SetsTerminationAndStopReason()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var run = runtime.StartBackgroundRun("research", subagentId: "subagent-1");
        var termination = AgentTerminationInfo.Completed("all done");

        runtime.StopBackgroundRun(run.Id, termination);

        var stored = runtime.GetBackgroundRun(run.Id)!;
        Assert.Equal(AgentBackgroundRunStatus.Stopped, stored.Status);
        Assert.Equal("all done", stored.StopReason);
        Assert.NotNull(stored.TerminationInfo);
        Assert.Equal(AgentTerminationKind.Completed, stored.TerminationInfo.Kind);
        Assert.Equal(AgentTerminationSource.Agent, stored.TerminationInfo.Source);
        Assert.Equal("all done", stored.TerminationInfo.Reason);
    }

    [Fact]
    public void FailBackgroundRun_WithTerminationInfo_SetsTermination()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var run = runtime.StartBackgroundRun("research");
        var termination = AgentTerminationInfo.Failed("network error", AgentTerminationSource.System);

        runtime.FailBackgroundRun(run.Id, termination);

        var stored = runtime.GetBackgroundRun(run.Id)!;
        Assert.Equal(AgentBackgroundRunStatus.Failed, stored.Status);
        Assert.NotNull(stored.TerminationInfo);
        Assert.Equal(AgentTerminationKind.Failed, stored.TerminationInfo.Kind);
        Assert.Equal(AgentTerminationSource.System, stored.TerminationInfo.Source);
    }

    [Fact]
    public void CancelBackgroundRun_WithTerminationInfo_SetsTermination()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var run = runtime.StartBackgroundRun("research");
        var termination = AgentTerminationInfo.Cancelled("user requested", AgentTerminationSource.User);

        runtime.CancelBackgroundRun(run.Id, termination);

        var stored = runtime.GetBackgroundRun(run.Id)!;
        Assert.Equal(AgentBackgroundRunStatus.Cancelled, stored.Status);
        Assert.NotNull(stored.TerminationInfo);
        Assert.Equal(AgentTerminationKind.Cancelled, stored.TerminationInfo.Kind);
        Assert.Equal(AgentTerminationSource.User, stored.TerminationInfo.Source);
    }

    [Fact]
    public void RequestBackgroundRunCancellation_WithTerminationInfo_SetsTermination()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var run = runtime.StartBackgroundRun("research");
        runtime.RegisterBackgroundRunCancellation(run.Id, () => { });
        var termination = AgentTerminationInfo.Cancelled("stop it", AgentTerminationSource.User);

        var result = runtime.RequestBackgroundRunCancellation(run.Id, termination);

        Assert.Equal(AgentBackgroundRunCancellationResult.Requested, result);
        var stored = runtime.GetBackgroundRun(run.Id)!;
        Assert.Equal(AgentBackgroundRunStatus.CancellationRequested, stored.Status);
        Assert.NotNull(stored.TerminationInfo);
        Assert.Equal(AgentTerminationKind.Cancelled, stored.TerminationInfo.Kind);
        Assert.Equal(AgentTerminationSource.User, stored.TerminationInfo.Source);
    }

    [Fact]
    public void GetLastTerminationEvent_ReturnsNullBeforeTermination()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var run = runtime.StartBackgroundRun("research");

        Assert.Null(runtime.GetLastTerminationEvent(run.Id));
    }

    [Fact]
    public void GetLastTerminationEvent_ReturnsEventAfterStop()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var run = runtime.StartBackgroundRun("research", subagentId: "subagent-1", workItemId: "work-item-1");
        var termination = AgentTerminationInfo.Completed("done");

        runtime.StopBackgroundRun(run.Id, termination);

        var evt = runtime.GetLastTerminationEvent(run.Id);
        Assert.NotNull(evt);
        Assert.Equal("subagent-1", evt.SubagentId);
        Assert.Equal(run.Id, evt.BackgroundRunId);
        Assert.Equal("work-item-1", evt.WorkItemId);
        Assert.Equal(AgentTerminationKind.Completed, evt.Kind);
        Assert.Equal(AgentTerminationSource.Agent, evt.Source);
        Assert.Equal("done", evt.Reason);
    }

    [Fact]
    public void GetLastTerminationEvent_FallsBackToRunIdWhenNoSubagentId()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var run = runtime.StartBackgroundRun("research");

        runtime.FailBackgroundRun(run.Id, AgentTerminationInfo.Failed("error"));

        var evt = runtime.GetLastTerminationEvent(run.Id);
        Assert.NotNull(evt);
        Assert.Equal(run.Id, evt.SubagentId);
    }

    [Fact]
    public void TerminationEvent_FromRun_CapturesToImmediateSnapshot()
    {
        var run = new AgentBackgroundRun
        {
            Id = "background-run-1",
            SubagentId = "subagent-1",
            Name = "test",
            WorkItemId = "work-item-1",
        };
        var termination = AgentTerminationInfo.TimedOut("exceeded 30s");

        var evt = AgentTerminationEvent.FromRun(run, termination);

        Assert.Equal("subagent-1", evt.SubagentId);
        Assert.Equal("background-run-1", evt.BackgroundRunId);
        Assert.Equal("work-item-1", evt.WorkItemId);
        Assert.Equal(AgentTerminationKind.TimedOut, evt.Kind);
        Assert.Equal(AgentTerminationSource.System, evt.Source);
        Assert.Equal("exceeded 30s", evt.Reason);
    }

    [Fact]
    public void AgentTerminationInfo_FactoryMethods_SetCorrectDefaults()
    {
        var completed = AgentTerminationInfo.Completed("done");
        Assert.Equal(AgentTerminationKind.Completed, completed.Kind);
        Assert.Equal(AgentTerminationSource.Agent, completed.Source);

        var cancelled = AgentTerminationInfo.Cancelled("user stop");
        Assert.Equal(AgentTerminationKind.Cancelled, cancelled.Kind);
        Assert.Equal(AgentTerminationSource.User, cancelled.Source);

        var failed = AgentTerminationInfo.Failed("crash");
        Assert.Equal(AgentTerminationKind.Failed, failed.Kind);
        Assert.Equal(AgentTerminationSource.Agent, failed.Source);

        var timedOut = AgentTerminationInfo.TimedOut("30s");
        Assert.Equal(AgentTerminationKind.TimedOut, timedOut.Kind);
        Assert.Equal(AgentTerminationSource.System, timedOut.Source);
    }

    [Fact]
    public void StopWithStringReason_BackwardsCompat_SetsTerminationInfo()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var run = runtime.StartBackgroundRun("research");

        runtime.StopBackgroundRun(run.Id, "finished");

        var stored = runtime.GetBackgroundRun(run.Id)!;
        Assert.Equal("finished", stored.StopReason);
        Assert.NotNull(stored.TerminationInfo);
        Assert.Equal(AgentTerminationKind.Completed, stored.TerminationInfo.Kind);
    }

    [Fact]
    public void FailWithStringReason_BackwardsCompat_SetsTerminationInfo()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var run = runtime.StartBackgroundRun("research");

        runtime.FailBackgroundRun(run.Id, "error");

        var stored = runtime.GetBackgroundRun(run.Id)!;
        Assert.Equal("error", stored.StopReason);
        Assert.NotNull(stored.TerminationInfo);
        Assert.Equal(AgentTerminationKind.Failed, stored.TerminationInfo.Kind);
    }

    [Fact]
    public void CancelWithStringReason_BackwardsCompat_SetsTerminationInfo()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var run = runtime.StartBackgroundRun("research");

        runtime.CancelBackgroundRun(run.Id, "cancelled");

        var stored = runtime.GetBackgroundRun(run.Id)!;
        Assert.Equal("cancelled", stored.StopReason);
        Assert.NotNull(stored.TerminationInfo);
        Assert.Equal(AgentTerminationKind.Cancelled, stored.TerminationInfo.Kind);
    }

    [Fact]
    public void PruneHistory_CleansUpTerminationEvents()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var run = runtime.StartBackgroundRun("old");
        runtime.StopBackgroundRun(run.Id, AgentTerminationInfo.Completed("done"));

        Assert.NotNull(runtime.GetLastTerminationEvent(run.Id));

        runtime.PruneHistory(new AgentRetentionPolicy
        {
            RetainTerminalBackgroundRuns = 0,
        });

        Assert.Null(runtime.GetLastTerminationEvent(run.Id));
    }

    [Fact]
    public void Clone_PreservesSubagentIdAndTerminationInfo()
    {
        var run = new AgentBackgroundRun
        {
            Id = "background-run-1",
            SubagentId = "subagent-1",
            Name = "test",
        };
        run.Stop(AgentTerminationInfo.Completed("done"));

        var clone = run.Clone();

        Assert.Equal("subagent-1", clone.SubagentId);
        Assert.NotNull(clone.TerminationInfo);
        Assert.Equal(AgentTerminationKind.Completed, clone.TerminationInfo.Kind);
    }

    [Fact]
    public void WorkItem_Clone_PreservesSubagentId()
    {
        var item = new AgentWorkItem
        {
            Id = "work-item-1",
            SubagentId = "subagent-1",
            Title = "test",
        };

        var clone = item.Clone();

        Assert.Equal("subagent-1", clone.SubagentId);
    }
}

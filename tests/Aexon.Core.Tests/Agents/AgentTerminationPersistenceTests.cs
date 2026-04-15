using System.Text.Json;
using Aexon.Core.Agents;
using Aexon.Core.Storage;

namespace Aexon.Core.Tests.Agents;

public sealed class AgentTerminationPersistenceTests
{
    [Fact]
    public void WorkItemEntry_RoundTrips_SubagentId()
    {
        var item = new AgentWorkItem
        {
            Id = "work-item-1",
            SubagentId = "subagent-3",
            Title = "test task",
            Owner = "alice",
        };

        var entry = AgentTaskPersistence.CreateWorkItemEntry(item);
        var snapshot = AgentTaskPersistence.Restore([entry]);

        var restored = Assert.Single(snapshot.WorkItems);
        Assert.Equal("subagent-3", restored.SubagentId);
        Assert.Equal("work-item-1", restored.Id);
    }

    [Fact]
    public void BackgroundRunEntry_RoundTrips_SubagentIdAndTermination()
    {
        var run = new AgentBackgroundRun
        {
            Id = "background-run-1",
            SubagentId = "subagent-2",
            Name = "research",
            Owner = "bob",
            WorkItemId = "work-item-1",
        };
        run.Stop(AgentTerminationInfo.Completed("done"));

        var entry = AgentTaskPersistence.CreateBackgroundRunEntry(run);
        var snapshot = AgentTaskPersistence.Restore([entry]);

        var restored = Assert.Single(snapshot.BackgroundRuns);
        Assert.Equal("subagent-2", restored.SubagentId);
        Assert.Equal(AgentBackgroundRunStatus.Stopped, restored.Status);
        Assert.NotNull(restored.TerminationInfo);
        Assert.Equal(AgentTerminationKind.Completed, restored.TerminationInfo.Kind);
        Assert.Equal(AgentTerminationSource.Agent, restored.TerminationInfo.Source);
        Assert.Equal("done", restored.TerminationInfo.Reason);
    }

    [Fact]
    public void BackgroundRunEntry_WithoutTermination_RestoresAsNull()
    {
        var run = new AgentBackgroundRun
        {
            Id = "background-run-1",
            Name = "research",
            Status = AgentBackgroundRunStatus.Running,
        };

        var entry = AgentTaskPersistence.CreateBackgroundRunEntry(run);
        var snapshot = AgentTaskPersistence.Restore([entry]);

        var restored = Assert.Single(snapshot.BackgroundRuns);
        Assert.Null(restored.TerminationInfo);
    }

    [Fact]
    public void TerminationEventEntry_RoundTrips()
    {
        var terminationEvent = new AgentTerminationEvent
        {
            SubagentId = "subagent-1",
            BackgroundRunId = "background-run-1",
            WorkItemId = "work-item-1",
            Kind = AgentTerminationKind.Failed,
            Source = AgentTerminationSource.System,
            Reason = "process exited",
        };

        var entry = AgentTaskPersistence.CreateTerminationEventEntry(terminationEvent);
        var snapshot = AgentTaskPersistence.Restore([entry]);

        var restored = Assert.Single(snapshot.TerminationEvents);
        Assert.Equal("subagent-1", restored.SubagentId);
        Assert.Equal("background-run-1", restored.BackgroundRunId);
        Assert.Equal("work-item-1", restored.WorkItemId);
        Assert.Equal(AgentTerminationKind.Failed, restored.Kind);
        Assert.Equal(AgentTerminationSource.System, restored.Source);
        Assert.Equal("process exited", restored.Reason);
    }

    [Fact]
    public void Restore_DeletedBackgroundRun_AlsoRemovesTerminationEvent()
    {
        var run = new AgentBackgroundRun
        {
            Id = "background-run-1",
            SubagentId = "subagent-1",
            Name = "research",
        };
        run.Stop(AgentTerminationInfo.Completed("done"));

        var terminationEvent = AgentTerminationEvent.FromRun(
            run,
            run.TerminationInfo!);

        var entries = new[]
        {
            AgentTaskPersistence.CreateBackgroundRunEntry(run),
            AgentTaskPersistence.CreateTerminationEventEntry(terminationEvent),
            AgentTaskPersistence.CreateBackgroundRunDeletedEntry("background-run-1"),
        };

        var snapshot = AgentTaskPersistence.Restore(entries);

        Assert.Empty(snapshot.BackgroundRuns);
        Assert.Empty(snapshot.TerminationEvents);
    }

    [Fact]
    public void Restore_MixedEntries_WithSubagentIds()
    {
        var item = new AgentWorkItem
        {
            Id = "work-item-1",
            SubagentId = "subagent-1",
            Title = "task",
        };
        var run = new AgentBackgroundRun
        {
            Id = "background-run-1",
            SubagentId = "subagent-1",
            Name = "run",
            WorkItemId = "work-item-1",
        };

        var entries = new[]
        {
            AgentTaskPersistence.CreateWorkItemEntry(item),
            AgentTaskPersistence.CreateBackgroundRunEntry(run),
        };

        var snapshot = AgentTaskPersistence.Restore(entries);

        Assert.Single(snapshot.WorkItems);
        Assert.Single(snapshot.BackgroundRuns);
        Assert.Equal("subagent-1", snapshot.WorkItems[0].SubagentId);
        Assert.Equal("subagent-1", snapshot.BackgroundRuns[0].SubagentId);
    }

    [Fact]
    public void TerminationEventEntry_HasCorrectEventType()
    {
        var terminationEvent = new AgentTerminationEvent
        {
            SubagentId = "subagent-1",
            BackgroundRunId = "background-run-1",
            Kind = AgentTerminationKind.Cancelled,
            Source = AgentTerminationSource.User,
        };

        var entry = AgentTaskPersistence.CreateTerminationEventEntry(terminationEvent);

        Assert.Equal("agent-termination", entry.EventType);
    }
}

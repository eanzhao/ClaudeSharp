using ClaudeSharp.Core.Agents;
using ClaudeSharp.Core.Storage;
using ClaudeSharp.Core.Tests.Runtime;
using ClaudeSharp.Core.Tests.Storage;

namespace ClaudeSharp.Core.Tests.Agents;

/// <summary>
/// Contains tests for the persistent agent task runtime.
/// </summary>
public sealed class PersistentAgentTaskRuntimeTests
{
    [Fact]
    public async Task CreateAsync_PersistsAndRestoresWorkItemsAndBackgroundRuns()
    {
        var journal = new RecordingJournal();
        var runtime = await PersistentAgentTaskRuntime.CreateAsync(journal);

        var workItem = runtime.CreateWorkItem("Inspect runtime", owner: "subagent");
        runtime.UpdateWorkItem(workItem.Id, item => item.Status = AgentWorkItemStatus.Completed);

        var backgroundRun = runtime.StartBackgroundRun(
            "Inspect runtime",
            owner: "subagent",
            workItemId: workItem.Id);
        runtime.AppendBackgroundRunOutput(backgroundRun.Id, "Summary: all good");
        runtime.StopBackgroundRun(backgroundRun.Id, "completed");

        Assert.Contains(journal.MetadataEntries, entry => entry.EventType == AgentTaskPersistence.WorkItemEventType);
        Assert.Contains(journal.MetadataEntries, entry => entry.EventType == AgentTaskPersistence.BackgroundRunEventType);
        Assert.Contains(journal.MetadataEntries, entry => entry.EventType == AgentTaskPersistence.BackgroundOutputEventType);

        var restoredJournal = new RecordingJournal();
        var restored = await PersistentAgentTaskRuntime.CreateAsync(
            restoredJournal,
            journal.MetadataEntries);

        var restoredWorkItem = Assert.Single(restored.ListWorkItems());
        Assert.Equal(AgentWorkItemStatus.Completed, restoredWorkItem.Status);
        Assert.Equal("Inspect runtime", restoredWorkItem.Title);

        var restoredRun = Assert.Single(restored.ListBackgroundRuns());
        Assert.Equal(AgentBackgroundRunStatus.Stopped, restoredRun.Status);
        Assert.Equal(workItem.Id, restoredRun.WorkItemId);
        Assert.Equal(["Summary: all good"], restoredRun.Output);
    }

    [Fact]
    public async Task CreateAsync_NormalizesRecoveredRunningBackgroundRuns()
    {
        var sourceJournal = new RecordingJournal();
        var source = await PersistentAgentTaskRuntime.CreateAsync(sourceJournal);

        var workItem = source.CreateWorkItem("Inspect runtime", owner: "subagent");
        source.UpdateWorkItem(workItem.Id, item => item.Status = AgentWorkItemStatus.InProgress);
        source.StartBackgroundRun(
            "Inspect runtime",
            owner: "subagent",
            workItemId: workItem.Id);

        var recoveryJournal = new RecordingJournal();
        var recovered = await PersistentAgentTaskRuntime.CreateAsync(
            recoveryJournal,
            sourceJournal.MetadataEntries);

        var recoveredWorkItem = Assert.Single(recovered.ListWorkItems());
        Assert.Equal(AgentWorkItemStatus.Blocked, recoveredWorkItem.Status);

        var recoveredRun = Assert.Single(recovered.ListBackgroundRuns());
        Assert.Equal(AgentBackgroundRunStatus.Failed, recoveredRun.Status);
        Assert.Contains("previous ClaudeSharp process exited", recoveredRun.StopReason, StringComparison.Ordinal);

        Assert.Contains(recoveryJournal.MetadataEntries, entry => entry.EventType == AgentTaskPersistence.WorkItemEventType);
        Assert.Contains(recoveryJournal.MetadataEntries, entry => entry.EventType == AgentTaskPersistence.BackgroundRunEventType);
    }

    [Fact]
    public async Task CreateAsync_NormalizesRecoveredQueuedBackgroundRuns()
    {
        var sourceJournal = new RecordingJournal();
        var source = await PersistentAgentTaskRuntime.CreateAsync(sourceJournal);

        var workItem = source.CreateWorkItem("Inspect runtime", owner: "subagent");
        source.UpdateWorkItem(workItem.Id, item => item.Status = AgentWorkItemStatus.InProgress);
        source.StartBackgroundRun(
            "Inspect runtime",
            owner: "subagent",
            workItemId: workItem.Id,
            initialStatus: AgentBackgroundRunStatus.Queued);

        var recoveryJournal = new RecordingJournal();
        var recovered = await PersistentAgentTaskRuntime.CreateAsync(
            recoveryJournal,
            sourceJournal.MetadataEntries);

        var recoveredWorkItem = Assert.Single(recovered.ListWorkItems());
        Assert.Equal(AgentWorkItemStatus.Blocked, recoveredWorkItem.Status);

        var recoveredRun = Assert.Single(recovered.ListBackgroundRuns());
        Assert.Equal(AgentBackgroundRunStatus.Failed, recoveredRun.Status);
        Assert.Contains("still queued", recoveredRun.StopReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAsync_NormalizesRecoveredCancellationRequestedRuns()
    {
        var workItem = new AgentWorkItem
        {
            Id = "work-item-1",
            Title = "Inspect runtime",
            Owner = "subagent",
            Status = AgentWorkItemStatus.InProgress,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        };
        var backgroundRun = new AgentBackgroundRun
        {
            Id = "background-run-1",
            Name = "Inspect runtime",
            Owner = "subagent",
            WorkItemId = workItem.Id,
            Status = AgentBackgroundRunStatus.CancellationRequested,
            StopReason = "user requested",
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        };

        var metadataEntries = new[]
        {
            AgentTaskPersistence.CreateWorkItemEntry(workItem),
            AgentTaskPersistence.CreateBackgroundRunEntry(backgroundRun),
        };

        var journal = new RecordingJournal();
        var runtime = await PersistentAgentTaskRuntime.CreateAsync(journal, metadataEntries);

        var restoredWorkItem = Assert.Single(runtime.ListWorkItems());
        Assert.Equal(AgentWorkItemStatus.Cancelled, restoredWorkItem.Status);

        var restoredRun = Assert.Single(runtime.ListBackgroundRuns());
        Assert.Equal(AgentBackgroundRunStatus.Cancelled, restoredRun.Status);
        Assert.Equal("user requested", restoredRun.StopReason);
    }

    [Fact]
    public async Task CreateAsync_RestoresStateFromTranscriptStoreProjection()
    {
        using var temp = new TempDirectoryScope(nameof(CreateAsync_RestoresStateFromTranscriptStoreProjection));
        var store = new JsonlTranscriptStore(temp.RootPath);
        var session = await store.CreateSessionAsync("/work/project", "claude-sonnet-4-6");
        var journal = new ConversationJournal(store, session);

        var runtime = await PersistentAgentTaskRuntime.CreateAsync(journal);
        var workItem = runtime.CreateWorkItem("Inspect runtime", owner: "subagent");
        var backgroundRun = runtime.StartBackgroundRun(
            "Inspect runtime",
            owner: "subagent",
            workItemId: workItem.Id);
        runtime.AppendBackgroundRunOutput(backgroundRun.Id, "Summary: all good");
        runtime.StopBackgroundRun(backgroundRun.Id, "completed");
        runtime.UpdateWorkItem(workItem.Id, item => item.Status = AgentWorkItemStatus.Completed);

        var reloadedSession = await store.FindSessionAsync(session.SessionId);
        Assert.NotNull(reloadedSession);

        var projection = await store.LoadProjectionAsync(
            reloadedSession!,
            new TranscriptLoadOptions());
        var restoredJournal = new RecordingJournal();
        var restoredRuntime = await PersistentAgentTaskRuntime.CreateAsync(
            restoredJournal,
            projection.MetadataEntries);

        Assert.Equal(AgentWorkItemStatus.Completed, Assert.Single(restoredRuntime.ListWorkItems()).Status);

        var restoredRun = Assert.Single(restoredRuntime.ListBackgroundRuns());
        Assert.Equal(AgentBackgroundRunStatus.Stopped, restoredRun.Status);
        Assert.Equal(["Summary: all good"], restoredRun.Output);
    }
}

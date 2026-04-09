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
        runtime.UpdateWorkItem(workItem.Id, item =>
        {
            item.Status = AgentWorkItemStatus.Completed;
            item.SourceKind = AgentWorkItemSourceKinds.MailboxPlanApproval;
            item.SourceId = "agent-message-11";
            item.SourceThreadId = "thread-9";
        });

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
        Assert.Equal(AgentWorkItemSourceKinds.MailboxPlanApproval, restoredWorkItem.SourceKind);
        Assert.Equal("agent-message-11", restoredWorkItem.SourceId);
        Assert.Equal("thread-9", restoredWorkItem.SourceThreadId);

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

    [Fact]
    public async Task PruneHistory_PersistsDeletedEntriesAcrossRestore()
    {
        var journal = new RecordingJournal();
        var runtime = await PersistentAgentTaskRuntime.CreateAsync(journal);

        var oldItem = runtime.CreateWorkItem("old item", owner: "subagent");
        runtime.UpdateWorkItem(oldItem.Id, item => item.Status = AgentWorkItemStatus.Completed);
        var oldRun = runtime.StartBackgroundRun(
            "old run",
            owner: "subagent",
            workItemId: oldItem.Id);
        runtime.StopBackgroundRun(oldRun.Id, "completed");

        var newItem = runtime.CreateWorkItem("new item", owner: "subagent");
        runtime.UpdateWorkItem(newItem.Id, item => item.Status = AgentWorkItemStatus.Completed);
        var newRun = runtime.StartBackgroundRun(
            "new run",
            owner: "subagent",
            workItemId: newItem.Id);
        runtime.StopBackgroundRun(newRun.Id, "completed");

        var pruneResult = runtime.PruneHistory(new AgentRetentionPolicy
        {
            RetainTerminalBackgroundRuns = 1,
            RetainTerminalWorkItems = 0,
        });

        Assert.Equal([oldRun.Id], pruneResult.RemovedBackgroundRunIds);
        Assert.Equal([oldItem.Id], pruneResult.RemovedWorkItemIds);
        Assert.Contains(journal.MetadataEntries, entry => entry.EventType == AgentTaskPersistence.BackgroundRunDeletedEventType);
        Assert.Contains(journal.MetadataEntries, entry => entry.EventType == AgentTaskPersistence.WorkItemDeletedEventType);

        var restored = await PersistentAgentTaskRuntime.CreateAsync(
            new RecordingJournal(),
            journal.MetadataEntries);

        var restoredItem = Assert.Single(restored.ListWorkItems());
        var restoredRun = Assert.Single(restored.ListBackgroundRuns());
        Assert.Equal(newItem.Id, restoredItem.Id);
        Assert.Equal(newRun.Id, restoredRun.Id);
    }

    [Fact]
    public async Task CreateAsync_AppliesAutoPrunePolicyAndPersistsDeletes()
    {
        var journal = new RecordingJournal();
        var runtime = await PersistentAgentTaskRuntime.CreateAsync(
            journal,
            autoPrunePolicy: new AgentRetentionPolicy
            {
                RetainTerminalBackgroundRuns = 1,
                RetainTerminalWorkItems = 0,
            });

        var oldItem = runtime.CreateWorkItem("old item", owner: "subagent");
        runtime.UpdateWorkItem(oldItem.Id, item => item.Status = AgentWorkItemStatus.Completed);
        var oldRun = runtime.StartBackgroundRun(
            "old run",
            owner: "subagent",
            workItemId: oldItem.Id);
        runtime.StopBackgroundRun(oldRun.Id, "completed");

        var newItem = runtime.CreateWorkItem("new item", owner: "subagent");
        runtime.UpdateWorkItem(newItem.Id, item => item.Status = AgentWorkItemStatus.Completed);
        var newRun = runtime.StartBackgroundRun(
            "new run",
            owner: "subagent",
            workItemId: newItem.Id);
        runtime.StopBackgroundRun(newRun.Id, "completed");

        Assert.Equal([newItem.Id], runtime.ListWorkItems().Select(item => item.Id));
        Assert.Equal([newRun.Id], runtime.ListBackgroundRuns().Select(run => run.Id));
        Assert.Contains(journal.MetadataEntries, entry => entry.EventType == AgentTaskPersistence.BackgroundRunDeletedEventType);
        Assert.Contains(journal.MetadataEntries, entry => entry.EventType == AgentTaskPersistence.WorkItemDeletedEventType);
    }
}

using Aexon.Core.Cron;
using Aexon.Core.Storage;
using Aexon.Core.Tests.Runtime;
using Aexon.Core.Tests.Storage;

namespace Aexon.Core.Tests.Cron;

public sealed class PersistentCronRuntimeTests
{
    [Fact]
    public async Task CreateAsync_PersistsAndRestoresJobs()
    {
        var journal = new RecordingJournal();
        var runtime = await PersistentCronRuntime.CreateAsync(journal);

        runtime.CreateJob("backup", "0 2 * * *", "tar czf /tmp/backup.tar.gz /data", "Nightly backup");
        runtime.CreateJob("cleanup", "0 * * * *", "rm /tmp/*.log");
        runtime.DeleteJob("cleanup");

        Assert.Contains(journal.MetadataEntries, e => e.EventType == CronPersistence.JobEventType);
        Assert.Contains(journal.MetadataEntries, e => e.EventType == CronPersistence.JobDeletedEventType);

        var restored = await PersistentCronRuntime.CreateAsync(
            new RecordingJournal(),
            journal.MetadataEntries);

        var job = Assert.Single(restored.ListJobs());
        Assert.Equal("backup", job.Id);
        Assert.Equal("0 2 * * *", job.Schedule);
        Assert.Equal("tar czf /tmp/backup.tar.gz /data", job.Command);
        Assert.Equal("Nightly backup", job.Description);
    }

    [Fact]
    public async Task RecordExecution_PersistsHistory()
    {
        var journal = new RecordingJournal();
        var runtime = await PersistentCronRuntime.CreateAsync(journal);

        runtime.CreateJob("test-job", "*/5 * * * *", "echo hello");
        runtime.RecordExecution(new CronExecutionRecord
        {
            Id = "exec-1",
            JobId = "test-job",
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            CompletedAt = DateTimeOffset.UtcNow,
            Success = true,
            Output = "hello",
        });

        Assert.Contains(journal.MetadataEntries, e => e.EventType == CronPersistence.ExecutionEventType);

        var restored = await PersistentCronRuntime.CreateAsync(
            new RecordingJournal(),
            journal.MetadataEntries);

        var history = restored.ListHistory("test-job");
        var record = Assert.Single(history);
        Assert.Equal("exec-1", record.Id);
        Assert.True(record.Success);
        Assert.Equal("hello", record.Output);

        var restoredJob = restored.GetJob("test-job");
        Assert.NotNull(restoredJob);
        Assert.NotNull(restoredJob!.LastRunAt);
    }

    [Fact]
    public async Task CreateAsync_RestoresFromTranscriptStoreProjection()
    {
        using var temp = new TempDirectoryScope(nameof(CreateAsync_RestoresFromTranscriptStoreProjection));
        var store = new JsonlTranscriptStore(temp.RootPath);
        var session = await store.CreateSessionAsync("/work/project", "claude-sonnet-4-6");
        var journal = new ConversationJournal(store, session);

        var runtime = await PersistentCronRuntime.CreateAsync(journal);
        runtime.CreateJob("build", "0 */2 * * *", "dotnet build", "Build every 2 hours");
        runtime.CreateJob("temp", "0 0 * * *", "echo temp");
        runtime.DeleteJob("temp");

        var reloadedSession = await store.FindSessionAsync(session.SessionId);
        Assert.NotNull(reloadedSession);

        var projection = await store.LoadProjectionAsync(
            reloadedSession!,
            new TranscriptLoadOptions());
        var restored = await PersistentCronRuntime.CreateAsync(
            new RecordingJournal(),
            projection.MetadataEntries);

        var job = Assert.Single(restored.ListJobs());
        Assert.Equal("build", job.Id);
        Assert.Equal("0 */2 * * *", job.Schedule);
        Assert.Equal("Build every 2 hours", job.Description);
    }
}

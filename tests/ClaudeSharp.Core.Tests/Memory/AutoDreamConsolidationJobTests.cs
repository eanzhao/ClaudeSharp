using ClaudeSharp.Core.Memory;
using ClaudeSharp.Core.Tests.Runtime;

namespace ClaudeSharp.Core.Tests.Memory;

/// <summary>
/// Contains tests for autodream consolidation jobs.
/// </summary>
public sealed class AutoDreamConsolidationJobTests
{
    [Fact]
    public async Task TryAcquireAsync_ReturnsNullWhenLockIsHeldAndReleasesOnDispose()
    {
        using var temp = new TempDirectory();
        var lockPath = temp.FullPath("mem", "projects", "project", "memory", "autodream", "consolidation.lock");
        var info = new AutoDreamConsolidationJobInfo(
            "project-1",
            "team-a",
            "session-1",
            "test",
            DateTimeOffset.UtcNow,
            Environment.MachineName);

        var first = await AutoDreamConsolidationJob.TryAcquireAsync(lockPath, info);
        Assert.NotNull(first);
        Assert.True(File.Exists(lockPath));

        var second = await AutoDreamConsolidationJob.TryAcquireAsync(lockPath, info);
        Assert.Null(second);

        await first!.DisposeAsync();
        Assert.False(File.Exists(lockPath));

        await using var third = await AutoDreamConsolidationJob.TryAcquireAsync(lockPath, info);
        Assert.NotNull(third);
    }
}

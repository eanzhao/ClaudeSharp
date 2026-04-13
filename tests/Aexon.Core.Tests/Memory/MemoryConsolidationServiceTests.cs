using Aexon.Core.Memory;
using Aexon.Core.Tests.Runtime;

namespace Aexon.Core.Tests.Memory;

/// <summary>
/// Contains tests for memory consolidation service.
/// </summary>
public sealed class MemoryConsolidationServiceTests
{
    [Fact]
    public async Task ConsolidateSessionAsync_AppendsToTeamAndProjectMemory()
    {
        using var temp = new TempDirectory();
        var layout = new MemdirLayout
        {
            MemoryBaseDirectory = temp.FullPath("mem"),
            ProjectRootDirectory = temp.FullPath("workspace", "project"),
        };
        layout.EnsureDirectories();

        var sessionFile = layout.CreateSessionMemoryFile("session-1");
        await sessionFile.SaveAsync("""
        Fix the parser

        - keep tool_use blocks intact
        - improve tests
        """);

        var service = new MemoryConsolidationService(layout, "team alpha");
        var result = await service.ConsolidateSessionAsync(sessionFile);

        var teamFile = layout.CreateTeamMemoryFile("team alpha");
        var teamContent = await teamFile.LoadAsync();
        var projectContent = await File.ReadAllTextAsync(layout.MemoryIndexPath);

        Assert.True(result.AcquiredLock);
        Assert.True(result.Consolidated);
        Assert.Equal("session-1", result.SessionId);
        Assert.Equal("team alpha", result.TeamName);
        Assert.NotNull(result.SummaryExcerpt);
        Assert.Contains("Fix the parser", teamContent);
        Assert.Contains("session-1", teamContent);
        Assert.Contains("Consolidation", projectContent);
        Assert.Contains("team alpha", projectContent);
        Assert.False(File.Exists(layout.AutoDreamLockPath));
    }

    [Fact]
    public async Task ConsolidateSessionAsync_ReturnsSkippedWhenLockAlreadyExists()
    {
        using var temp = new TempDirectory();
        var layout = new MemdirLayout
        {
            MemoryBaseDirectory = temp.FullPath("mem"),
            ProjectRootDirectory = temp.FullPath("workspace", "project"),
        };
        layout.EnsureDirectories();

        await File.WriteAllTextAsync(layout.AutoDreamLockPath, "busy");
        var sessionFile = layout.CreateSessionMemoryFile("session-1");
        await sessionFile.SaveAsync("content");

        var service = new MemoryConsolidationService(layout, "team alpha");
        var result = await service.ConsolidateSessionAsync(sessionFile);

        Assert.False(result.AcquiredLock);
        Assert.False(result.Consolidated);
        Assert.Contains("already running", result.Message, StringComparison.Ordinal);
        Assert.Null(await layout.CreateTeamMemoryFile("team alpha").LoadAsync());
    }
}

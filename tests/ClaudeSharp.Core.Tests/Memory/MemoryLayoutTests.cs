using ClaudeSharp.Core.Memory;
using ClaudeSharp.Core.Tests.Runtime;

namespace ClaudeSharp.Core.Tests.Memory;

/// <summary>
/// Contains tests for memory Layout.
/// </summary>
public sealed class MemoryLayoutTests
{
    [Fact]
    public void MemdirLayout_UsesStableProjectIdAndResolvesExpectedPaths()
    {
        using var temp = new TempDirectory();

        var layoutA = new MemdirLayout
        {
            MemoryBaseDirectory = temp.FullPath("mem"),
            ProjectRootDirectory = temp.FullPath("workspace", "project"),
        };
        var layoutB = new MemdirLayout
        {
            MemoryBaseDirectory = temp.FullPath("mem"),
            ProjectRootDirectory = temp.FullPath("workspace", "project") + Path.DirectorySeparatorChar,
        };

        layoutA.EnsureDirectories();

        Assert.Equal(layoutA.ProjectId, layoutB.ProjectId);
        Assert.StartsWith(temp.FullPath("mem"), layoutA.ProjectMemoryDirectory);
        Assert.EndsWith("MEMORY.md", layoutA.MemoryIndexPath);
        Assert.Contains("sessions", layoutA.SessionMemoryDirectory);
        Assert.Contains("team", layoutA.TeamMemoryDirectory);
        Assert.Contains("autodream", layoutA.AutoDreamDirectory);
        Assert.EndsWith("consolidation.lock", layoutA.AutoDreamLockPath);
        Assert.EndsWith(
            Path.Combine("session-1", "SESSION_MEMORY.md"),
            layoutA.GetSessionMemoryPath("Session 1"));
        Assert.EndsWith(
            Path.Combine("team-alpha", "TEAM_MEMORY.md"),
            layoutA.GetTeamMemoryPath("Team Alpha"));
        Assert.True(Directory.Exists(layoutA.ProjectMemoryDirectory));
        Assert.True(Directory.Exists(layoutA.SessionMemoryDirectory));
        Assert.True(Directory.Exists(layoutA.TeamMemoryDirectory));
        Assert.True(Directory.Exists(layoutA.AutoDreamDirectory));
    }
}

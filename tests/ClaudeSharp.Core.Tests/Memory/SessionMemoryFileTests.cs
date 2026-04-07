using ClaudeSharp.Core.Memory;
using ClaudeSharp.Core.Tests.Runtime;

namespace ClaudeSharp.Core.Tests.Memory;

/// <summary>
/// Contains tests for session Memory File.
/// </summary>
public sealed class SessionMemoryFileTests
{
    [Fact]
    public async Task SessionMemoryFile_SaveAndLoadRoundTrips()
    {
        using var temp = new TempDirectory();
        var layout = new MemdirLayout
        {
            MemoryBaseDirectory = temp.FullPath("mem"),
            ProjectRootDirectory = temp.FullPath("workspace", "project"),
        };

        var file = layout.CreateSessionMemoryFile("session-1");

        Assert.Equal("session-1", file.SessionId);
        Assert.Equal(layout.ProjectRootDirectory, file.ProjectRootDirectory);
        Assert.False(file.Exists);
        Assert.Null(await layout.CreateSessionMemoryFile("missing").LoadAsync());

        await file.SaveAsync("session summary");

        Assert.True(file.Exists);
        Assert.Equal("session summary", await file.LoadAsync());
    }
}

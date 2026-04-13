using Aexon.Core.Memory;
using Aexon.Core.Tests.Runtime;

namespace Aexon.Core.Tests.Memory;

/// <summary>
/// Contains tests for team Memory File.
/// </summary>
public sealed class TeamMemoryFileTests
{
    [Fact]
    public async Task TeamMemoryFile_SaveAppendAndLoadRoundTrips()
    {
        using var temp = new TempDirectory();
        var layout = new MemdirLayout
        {
            MemoryBaseDirectory = temp.FullPath("mem"),
            ProjectRootDirectory = temp.FullPath("workspace", "project"),
        };

        var file = layout.CreateTeamMemoryFile("team alpha");

        Assert.Equal("team alpha", file.TeamName);
        Assert.Equal(layout.ProjectRootDirectory, file.ProjectRootDirectory);
        Assert.False(file.Exists);
        Assert.Null(await layout.CreateTeamMemoryFile("missing").LoadAsync());

        await file.SaveAsync("team summary");
        await file.AppendAsync("\nmore context");

        Assert.True(file.Exists);
        Assert.Equal("team summary\nmore context", await file.LoadAsync());
    }
}

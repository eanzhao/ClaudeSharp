using Aexon.Core.Hooks;
using Aexon.Core.Memory;
using Aexon.Core.Storage;
using Aexon.Core.Tests.Runtime;

namespace Aexon.Core.Tests.Hooks;

/// <summary>
/// Contains tests for session-memory consolidation hook observer.
/// </summary>
public sealed class SessionMemoryConsolidationHookObserverTests
{
    [Fact]
    public async Task OnSessionEndAsync_ConsolidatesSessionMemoryIntoTeamAndProjectFiles()
    {
        using var temp = new TempDirectory();
        var layout = new MemdirLayout
        {
            MemoryBaseDirectory = temp.FullPath("mem"),
            ProjectRootDirectory = temp.FullPath("workspace", "project"),
        };
        layout.EnsureDirectories();

        await layout.CreateSessionMemoryFile("session-1").SaveAsync("""
        Keep the agent runtime recoverable.
        Preserve tool protocol boundaries.
        """);

        var observer = new SessionMemoryConsolidationHookObserver(
            new MemoryConsolidationService(layout, "team alpha"));

        await observer.OnSessionEndAsync(
            new SessionEndHookContext(
                "session-1",
                layout.ProjectRootDirectory,
                "claude-sonnet-4-20250514",
                new ConversationSessionMetadata(),
                4,
                dueToClear: false));

        var teamContent = await layout.CreateTeamMemoryFile("team alpha").LoadAsync();
        var projectContent = await File.ReadAllTextAsync(layout.MemoryIndexPath);

        Assert.Contains("Keep the agent runtime recoverable.", teamContent);
        Assert.Contains("session-1", projectContent);
    }

    [Fact]
    public async Task OnSessionEndAsync_IgnoresClearEvents()
    {
        using var temp = new TempDirectory();
        var layout = new MemdirLayout
        {
            MemoryBaseDirectory = temp.FullPath("mem"),
            ProjectRootDirectory = temp.FullPath("workspace", "project"),
        };
        layout.EnsureDirectories();

        await layout.CreateSessionMemoryFile("session-1").SaveAsync("content");

        var observer = new SessionMemoryConsolidationHookObserver(
            new MemoryConsolidationService(layout, "team alpha"));

        await observer.OnSessionEndAsync(
            new SessionEndHookContext(
                "session-1",
                layout.ProjectRootDirectory,
                "claude-sonnet-4-20250514",
                new ConversationSessionMetadata(),
                1,
                dueToClear: true));

        Assert.Null(await layout.CreateTeamMemoryFile("team alpha").LoadAsync());
        Assert.False(File.Exists(layout.MemoryIndexPath));
    }
}

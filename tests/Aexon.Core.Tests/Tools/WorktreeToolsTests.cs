using System.Diagnostics;
using System.Text.Json;
using Aexon.Core.Agents;
using Aexon.Core.Permissions;
using Aexon.Core.Tests.Runtime;
using Aexon.Core.Tools;
using Aexon.Tools;

namespace Aexon.Core.Tests.Tools;

public sealed class WorktreeToolsTests
{
    [Fact]
    public async Task WorktreeTools_CanCreateListAndForceExitDirtyWorktrees()
    {
        using var temp = new TempDirectory();
        var repo = temp.CreateDirectory("repo");
        var workspaceRoot = temp.CreateDirectory("worktrees");
        await InitializeGitRepositoryAsync(repo);

        var runtime = new InMemoryAgentManagedWorktreeRuntime(
            new GitWorktreeAgentWorkspaceManager(workspaceRoot));
        var enterTool = new EnterWorktreeTool(runtime);
        var exitTool = new ExitWorktreeTool(runtime);
        var context = CreateContext(repo);

        var created = await enterTool.ExecuteAsync(
            Json(new { path = ".", name = "feature-1" }),
            context);
        var worktree = Assert.Single(runtime.List());
        File.WriteAllText(Path.Combine(worktree.RootDirectory, "tracked.txt"), "changed");

        var listed = await enterTool.ExecuteAsync(
            Json(new { list_only = true }),
            context);
        var blockedExit = await exitTool.ExecuteAsync(
            Json(new { id = worktree.Id }),
            context);
        var forcedExit = await exitTool.ExecuteAsync(
            Json(new { id = worktree.Id, force = true }),
            context);

        Assert.False(created.IsError);
        Assert.Contains(worktree.Id, created.Data, StringComparison.Ordinal);
        Assert.False(listed.IsError);
        Assert.Contains(worktree.Id, listed.Data, StringComparison.Ordinal);
        Assert.True(blockedExit.IsError);
        Assert.Contains("uncommitted changes", blockedExit.Data, StringComparison.Ordinal);
        Assert.False(forcedExit.IsError);
        Assert.Empty(runtime.List());
        Assert.False(Directory.Exists(worktree.RootDirectory));
    }

    [Fact]
    public async Task EnterWorktreeTool_ListOnly_WhenEmpty_ReturnsNone()
    {
        using var temp = new TempDirectory();
        var runtime = new InMemoryAgentManagedWorktreeRuntime(
            new GitWorktreeAgentWorkspaceManager(temp.CreateDirectory("worktrees")));
        var tool = new EnterWorktreeTool(runtime);

        var result = await tool.ExecuteAsync(
            Json(new { list_only = true }),
            CreateContext(temp.CreateDirectory("repo")));

        Assert.False(result.IsError);
        Assert.Contains("Managed worktrees:", result.Data, StringComparison.Ordinal);
        Assert.Contains("(none)", result.Data, StringComparison.Ordinal);
    }

    [Fact]
    public void EnterWorktreeTool_ReadOnlyAndConcurrencySafe_OnlyWhenListOnly()
    {
        var tool = new EnterWorktreeTool(new InMemoryAgentManagedWorktreeRuntime());

        Assert.True(tool.IsReadOnly(Json(new { list_only = true })));
        Assert.True(tool.IsConcurrencySafe(Json(new { list_only = true })));
        Assert.False(tool.IsReadOnly(Json(new { list_only = false })));
        Assert.False(tool.IsConcurrencySafe(Json(new { list_only = false })));
        Assert.False(tool.IsReadOnly(Json(new { path = "." })));
        Assert.False(tool.IsConcurrencySafe(Json(new { path = "." })));
    }

    [Fact]
    public async Task ExitWorktreeTool_ValidateInputAsync_RejectsMissingOrBlankId()
    {
        var tool = new ExitWorktreeTool(new InMemoryAgentManagedWorktreeRuntime());
        var context = CreateContext(Environment.CurrentDirectory);

        var missing = await tool.ValidateInputAsync(Json(new { }), context);
        var blank = await tool.ValidateInputAsync(Json(new { id = "   " }), context);

        Assert.False(missing.IsValid);
        Assert.Equal("id is required.", missing.Message);
        Assert.False(blank.IsValid);
        Assert.Equal("id is required.", blank.Message);
    }

    [Fact]
    public async Task ExitWorktreeTool_UnknownId_ReturnsNotFoundMessage()
    {
        var tool = new ExitWorktreeTool(new InMemoryAgentManagedWorktreeRuntime());

        var result = await tool.ExecuteAsync(
            Json(new { id = "worktree-404" }),
            CreateContext(Environment.CurrentDirectory));

        Assert.True(result.IsError);
        Assert.Contains("No managed worktree matched", result.Data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExitWorktreeTool_CleanWorktree_ExitsWithoutForce()
    {
        using var temp = new TempDirectory();
        var repo = temp.CreateDirectory("repo");
        var workspaceRoot = temp.CreateDirectory("worktrees");
        await InitializeGitRepositoryAsync(repo);

        var runtime = new InMemoryAgentManagedWorktreeRuntime(
            new GitWorktreeAgentWorkspaceManager(workspaceRoot));
        var enterTool = new EnterWorktreeTool(runtime);
        var exitTool = new ExitWorktreeTool(runtime);
        var context = CreateContext(repo);

        var created = await enterTool.ExecuteAsync(
            Json(new { path = ".", name = "clean-branch" }),
            context);
        var worktree = Assert.Single(runtime.List());
        var exited = await exitTool.ExecuteAsync(
            Json(new { id = worktree.Id }),
            context);

        Assert.False(created.IsError);
        Assert.False(exited.IsError);
        Assert.Contains($"'{worktree.Id}'", exited.Data, StringComparison.Ordinal);
        Assert.Empty(runtime.List());
        Assert.False(Directory.Exists(worktree.RootDirectory));
    }

    [Fact]
    public async Task EnterWorktreeTool_CreateAndList_SurfaceNameInResultText()
    {
        using var temp = new TempDirectory();
        var repo = temp.CreateDirectory("repo");
        var workspaceRoot = temp.CreateDirectory("worktrees");
        await InitializeGitRepositoryAsync(repo);

        var runtime = new InMemoryAgentManagedWorktreeRuntime(
            new GitWorktreeAgentWorkspaceManager(workspaceRoot));
        var enterTool = new EnterWorktreeTool(runtime);
        var context = CreateContext(repo);

        var created = await enterTool.ExecuteAsync(
            Json(new { path = ".", name = "feature-name" }),
            context);
        var worktree = Assert.Single(runtime.List());
        var listed = await enterTool.ExecuteAsync(
            Json(new { list_only = true }),
            context);

        Assert.False(created.IsError);
        Assert.False(listed.IsError);
        Assert.Contains("name: feature-name", created.Data, StringComparison.Ordinal);
        Assert.Contains(worktree.Id, listed.Data, StringComparison.Ordinal);
        Assert.Contains("(feature-name)", listed.Data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnterWorktreeTool_CleanupUnchanged_PrunesPriorCleanWorktreeAndReportsAutoCleanedCount()
    {
        using var temp = new TempDirectory();
        var repo = temp.CreateDirectory("repo");
        var workspaceRoot = temp.CreateDirectory("worktrees");
        await InitializeGitRepositoryAsync(repo);

        var runtime = new InMemoryAgentManagedWorktreeRuntime(
            new GitWorktreeAgentWorkspaceManager(workspaceRoot));
        var tool = new EnterWorktreeTool(runtime);
        var context = CreateContext(repo);

        var first = await tool.ExecuteAsync(
            Json(new { path = ".", name = "first-clean" }),
            context);
        var firstWorktree = Assert.Single(runtime.List());
        var second = await tool.ExecuteAsync(
            Json(new { path = ".", name = "second-clean", cleanup_unchanged = true }),
            context);
        var remaining = Assert.Single(runtime.List());

        Assert.False(first.IsError);
        Assert.False(second.IsError);
        Assert.Contains("auto_cleaned: 1", second.Data, StringComparison.Ordinal);
        Assert.Contains("name: second-clean", second.Data, StringComparison.Ordinal);
        Assert.Equal("second-clean", remaining.Name);
        Assert.NotEqual(firstWorktree.Id, remaining.Id);
        Assert.False(Directory.Exists(firstWorktree.RootDirectory));
    }

    private static ToolExecutionContext CreateContext(string workingDirectory) =>
        new()
        {
            WorkingDirectory = workingDirectory,
            PermissionContext = new PermissionContext(),
            Tools = [],
            Messages = [],
            CancellationToken = CancellationToken.None,
        };

    private static JsonElement Json(object value) =>
        JsonSerializer.SerializeToElement(value);

    private static async Task InitializeGitRepositoryAsync(string workingDirectory)
    {
        await RunGitAsync(workingDirectory, "init");
        await RunGitAsync(workingDirectory, "config", "user.email", "test@example.com");
        await RunGitAsync(workingDirectory, "config", "user.name", "Aexon Tests");
        File.WriteAllText(Path.Combine(workingDirectory, "tracked.txt"), "initial");
        await RunGitAsync(workingDirectory, "add", "tracked.txt");
        await RunGitAsync(workingDirectory, "commit", "-m", "init");
    }

    private static async Task RunGitAsync(string workingDirectory, params string[] arguments)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        process.Start();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.True(process.ExitCode == 0, stderr);
    }
}

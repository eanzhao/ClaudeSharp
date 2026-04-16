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

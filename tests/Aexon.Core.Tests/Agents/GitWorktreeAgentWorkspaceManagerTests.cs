using System.Diagnostics;
using Aexon.Core.Agents;
using Aexon.Core.Tests.Runtime;

namespace Aexon.Core.Tests.Agents;

/// <summary>
/// Contains tests for the git worktree agent workspace manager.
/// </summary>
public sealed class GitWorktreeAgentWorkspaceManagerTests
{
    [Fact]
    public async Task AcquireAsync_InGitRepo_CreatesMirroredWorkspaceAndCleansUp()
    {
        using var temp = new TempDirectory();
        var repo = temp.CreateDirectory("repo");
        var workspaceRoot = temp.CreateDirectory("isolated-workspaces");

        await InitializeGitRepositoryAsync(repo);

        File.WriteAllText(Path.Combine(repo, "tracked.txt"), "modified");
        File.Delete(Path.Combine(repo, "removed.txt"));
        File.WriteAllText(Path.Combine(repo, "untracked.txt"), "new file");
        var nested = Path.Combine(repo, "src", "subagent");
        Directory.CreateDirectory(nested);

        var manager = new GitWorktreeAgentWorkspaceManager(workspaceRoot);
        var lease = await manager.AcquireAsync(nested);

        Assert.True(lease.IsIsolated, lease.Description);
        Assert.StartsWith(workspaceRoot, lease.RootDirectory, StringComparison.Ordinal);
        Assert.Equal(Path.Combine(lease.RootDirectory, "src", "subagent"), lease.WorkingDirectory);
        Assert.Equal("modified", File.ReadAllText(Path.Combine(lease.RootDirectory, "tracked.txt")));
        Assert.False(File.Exists(Path.Combine(lease.RootDirectory, "removed.txt")));
        Assert.Equal("new file", File.ReadAllText(Path.Combine(lease.RootDirectory, "untracked.txt")));
        Assert.True(Directory.Exists(lease.WorkingDirectory));

        await lease.DisposeAsync();

        Assert.False(Directory.Exists(lease.RootDirectory));
    }

    [Fact]
    public async Task AcquireAsync_OutsideGitRepo_FallsBackToOriginalDirectory()
    {
        using var temp = new TempDirectory();
        var plainDirectory = temp.CreateDirectory("plain");
        var workspaceRoot = temp.CreateDirectory("isolated-workspaces");
        var manager = new GitWorktreeAgentWorkspaceManager(workspaceRoot);

        var lease = await manager.AcquireAsync(plainDirectory);

        Assert.False(lease.IsIsolated);
        Assert.Equal(plainDirectory, lease.WorkingDirectory);
        Assert.Equal(plainDirectory, lease.RootDirectory);
    }

    private static async Task InitializeGitRepositoryAsync(string workingDirectory)
    {
        await RunGitAsync(workingDirectory, "init");
        await RunGitAsync(workingDirectory, "config", "user.email", "test@example.com");
        await RunGitAsync(workingDirectory, "config", "user.name", "Aexon Tests");

        Directory.CreateDirectory(Path.Combine(workingDirectory, "src"));
        File.WriteAllText(Path.Combine(workingDirectory, "tracked.txt"), "initial");
        File.WriteAllText(Path.Combine(workingDirectory, "removed.txt"), "delete me");
        File.WriteAllText(Path.Combine(workingDirectory, "src", "existing.txt"), "hello");

        await RunGitAsync(workingDirectory, "add", "tracked.txt", "removed.txt", "src/existing.txt");
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

        Assert.True(
            process.ExitCode == 0,
            $"git {string.Join(' ', arguments)} failed: {stderr}");
    }
}

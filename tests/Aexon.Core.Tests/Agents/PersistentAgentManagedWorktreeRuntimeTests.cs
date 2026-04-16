using System.Diagnostics;
using Aexon.Core.Agents;
using Aexon.Core.Tests.Runtime;

namespace Aexon.Core.Tests.Agents;

public sealed class PersistentAgentManagedWorktreeRuntimeTests
{
    [Fact]
    public async Task CreateAsync_RestoresAndCleansManagedWorktrees()
    {
        using var temp = new TempDirectory();
        var repo = temp.CreateDirectory("repo");
        var workspaceRoot = temp.CreateDirectory("worktrees");
        await InitializeGitRepositoryAsync(repo);

        var journal = new RecordingJournal();
        var runtime = await PersistentAgentManagedWorktreeRuntime.CreateAsync(
            journal,
            workspaceManager: new GitWorktreeAgentWorkspaceManager(workspaceRoot));
        var entered = await runtime.EnterAsync(repo, name: "persisted");

        var restored = await PersistentAgentManagedWorktreeRuntime.CreateAsync(
            new RecordingJournal(),
            journal.MetadataEntries,
            new GitWorktreeAgentWorkspaceManager(workspaceRoot));
        var restoredWorktree = Assert.Single(restored.List());

        var exited = await restored.ExitAsync(restoredWorktree.Id, force: true);

        Assert.Equal(entered.Worktree.Id, restoredWorktree.Id);
        Assert.Equal(AgentManagedWorktreeExitStatus.Exited, exited.Status);
        Assert.False(Directory.Exists(restoredWorktree.RootDirectory));
    }

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

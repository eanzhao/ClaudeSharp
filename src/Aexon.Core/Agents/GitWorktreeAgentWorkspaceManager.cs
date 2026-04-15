using System.Diagnostics;

namespace Aexon.Core.Agents;

/// <summary>
/// Creates isolated subagent workspaces by using git worktrees and mirroring the current workspace state.
/// </summary>
public sealed class GitWorktreeAgentWorkspaceManager : IAgentWorkspaceManager
{
    private static readonly HashSet<string> IgnoredDirectoryNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".git",
            "node_modules",
            "bin",
            "obj",
        };

    public GitWorktreeAgentWorkspaceManager(string? workspaceRootDirectory = null)
    {
        WorkspaceRootDirectory = Path.GetFullPath(
            workspaceRootDirectory ??
            Path.Combine(Path.GetTempPath(), "claudesharp-agent-workspaces"));
    }

    public string WorkspaceRootDirectory { get; }

    public async Task<AgentWorkspaceLease> AcquireAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        var fullWorkingDirectory = Path.GetFullPath(workingDirectory);
        var repoRoot = await TryResolveRepoRootAsync(fullWorkingDirectory, cancellationToken);
        if (repoRoot == null)
        {
            return AgentWorkspaceLease.Passthrough(
                fullWorkingDirectory,
                "No git repository found.");
        }

        var relativePath = await TryResolveRepoRelativePathAsync(
            fullWorkingDirectory,
            cancellationToken);
        if (relativePath == null)
        {
            return AgentWorkspaceLease.Passthrough(
                fullWorkingDirectory,
                "Working directory is outside the git repository root.");
        }

        var workspaceRoot = CreateWorkspaceRoot(repoRoot);
        if (!IsSafeWorkspaceLocation(repoRoot, workspaceRoot))
        {
            return AgentWorkspaceLease.Passthrough(
                fullWorkingDirectory,
                "Workspace root cannot be nested inside the source repository.");
        }

        try
        {
            Directory.CreateDirectory(WorkspaceRootDirectory);

            var addResult = await RunGitAsync(
                repoRoot,
                ["worktree", "add", "--detach", workspaceRoot, "HEAD"],
                cancellationToken);
            if (addResult.ExitCode != 0)
            {
                TryDeleteDirectory(workspaceRoot);
                return AgentWorkspaceLease.Passthrough(
                    fullWorkingDirectory,
                    $"git worktree add failed: {addResult.Stderr.Trim()}");
            }

            MirrorDirectory(repoRoot, workspaceRoot);

            var isolatedWorkingDirectory = relativePath is "." or ""
                ? workspaceRoot
                : Path.GetFullPath(Path.Combine(workspaceRoot, relativePath));

            if (Directory.Exists(fullWorkingDirectory) &&
                !Directory.Exists(isolatedWorkingDirectory))
            {
                Directory.CreateDirectory(isolatedWorkingDirectory);
            }

            return new AgentWorkspaceLease(
                isolatedWorkingDirectory,
                workspaceRoot,
                isIsolated: true,
                description: "Temporary git worktree snapshot",
                disposeAsync: () => ReleaseAsync(repoRoot, workspaceRoot, CancellationToken.None));
        }
        catch (Exception ex)
        {
            TryDeleteDirectory(workspaceRoot);
            return AgentWorkspaceLease.Passthrough(
                fullWorkingDirectory,
                $"Failed to create isolated worktree, using the original directory instead: {ex.Message}");
        }
    }

    private string CreateWorkspaceRoot(string repoRoot)
    {
        var repositoryName = Path.GetFileName(
            repoRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(repositoryName))
            repositoryName = "repo";

        return Path.Combine(
            WorkspaceRootDirectory,
            $"{repositoryName}-{Guid.NewGuid():N}");
    }

    private static bool IsSafeWorkspaceLocation(string repoRoot, string workspaceRoot)
    {
        var relative = Path.GetRelativePath(repoRoot, workspaceRoot);
        return IsOutsideRoot(relative);
    }

    private static bool IsOutsideRoot(string relativePath) =>
        relativePath.StartsWith("..", StringComparison.Ordinal) ||
        Path.IsPathRooted(relativePath);

    private static async Task<string?> TryResolveRepoRootAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var result = await RunGitAsync(
            workingDirectory,
            ["rev-parse", "--show-toplevel"],
            cancellationToken);

        if (result.ExitCode != 0)
            return null;

        var root = result.Stdout.Trim();
        return string.IsNullOrWhiteSpace(root)
            ? null
            : Path.GetFullPath(root);
    }

    private static async Task<string?> TryResolveRepoRelativePathAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var result = await RunGitAsync(
            workingDirectory,
            ["rev-parse", "--show-prefix"],
            cancellationToken);

        if (result.ExitCode != 0)
            return null;

        var prefix = result.Stdout.Trim();
        if (string.IsNullOrWhiteSpace(prefix))
            return ".";

        return prefix
            .TrimEnd('/', '\\')
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
    }

    private static async ValueTask ReleaseAsync(
        string repoRoot,
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        try
        {
            await RunGitAsync(
                repoRoot,
                ["worktree", "remove", "--force", workspaceRoot],
                cancellationToken);
        }
        catch
        {
            // Fall through to best-effort directory cleanup below.
        }

        TryDeleteDirectory(workspaceRoot);
    }

    private static void MirrorDirectory(string sourceRoot, string targetRoot)
    {
        MirrorDirectoryRecursive(sourceRoot, targetRoot);
    }

    private static void MirrorDirectoryRecursive(string sourceDirectory, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);

        var sourceEntries = Directory.EnumerateFileSystemEntries(sourceDirectory).ToArray();
        var sourceNames = sourceEntries
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var targetEntry in Directory.EnumerateFileSystemEntries(targetDirectory))
        {
            var name = Path.GetFileName(targetEntry);
            if (ShouldSkipName(name))
                continue;

            if (sourceNames.Contains(name))
                continue;

            DeleteEntry(targetEntry);
        }

        foreach (var sourceEntry in sourceEntries)
        {
            var name = Path.GetFileName(sourceEntry);
            if (ShouldSkipName(name))
                continue;

            var targetEntry = Path.Combine(targetDirectory, name);
            if (Directory.Exists(sourceEntry))
            {
                if (File.Exists(targetEntry))
                    File.Delete(targetEntry);

                MirrorDirectoryRecursive(sourceEntry, targetEntry);
                continue;
            }

            if (Directory.Exists(targetEntry))
                Directory.Delete(targetEntry, recursive: true);

            Directory.CreateDirectory(Path.GetDirectoryName(targetEntry)!);
            File.Copy(sourceEntry, targetEntry, overwrite: true);
        }
    }

    private static bool ShouldSkipName(string? name) =>
        !string.IsNullOrWhiteSpace(name) &&
        IgnoredDirectoryNames.Contains(name);

    private static void DeleteEntry(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
        else if (File.Exists(path))
            File.Delete(path);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best effort cleanup only.
        }
    }

    private static async Task<GitCommandResult> RunGitAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
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
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return new GitCommandResult(
            process.ExitCode,
            await stdoutTask,
            await stderrTask);
    }

    private sealed record GitCommandResult(
        int ExitCode,
        string Stdout,
        string Stderr);
}

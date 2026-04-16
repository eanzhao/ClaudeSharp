using System.Diagnostics;

namespace Aexon.Core.Agents;

internal sealed record GitCommandResult(
    int ExitCode,
    string Stdout,
    string Stderr);

internal static class GitWorkspaceUtilities
{
    public static async Task<string?> TryResolveRepoRootAsync(
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

    public static async Task<string?> TryResolveRepoRelativePathAsync(
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

    public static async Task<bool> HasUncommittedChangesAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var result = await RunGitAsync(
            workingDirectory,
            ["status", "--porcelain", "--untracked-files=all"],
            cancellationToken);
        return result.ExitCode == 0 &&
               !string.IsNullOrWhiteSpace(result.Stdout);
    }

    public static async Task<GitCommandResult> RunGitAsync(
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
}

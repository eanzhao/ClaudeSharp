using System.Diagnostics;
using System.Text;

namespace Aexon.Core.Hooks;

/// <summary>
/// Represents the result of executing a configured hook command.
/// </summary>
public sealed record HookCommandExecutionResult(
    int ExitCode,
    string Stdout,
    string Stderr,
    bool TimedOut = false)
{
    public bool Succeeded => !TimedOut && ExitCode == 0;
}

/// <summary>
/// Defines the contract for running configured hook commands.
/// </summary>
public interface IHookCommandRunner
{
    Task<HookCommandExecutionResult> ExecuteAsync(
        HookCommandDefinition command,
        string payloadJson,
        string fallbackWorkingDirectory,
        IReadOnlyDictionary<string, string> ambientEnvironment,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Executes configured hook commands through the user's shell.
/// </summary>
public sealed class ProcessHookCommandRunner : IHookCommandRunner
{
    public async Task<HookCommandExecutionResult> ExecuteAsync(
        HookCommandDefinition command,
        string payloadJson,
        string fallbackWorkingDirectory,
        IReadOnlyDictionary<string, string> ambientEnvironment,
        CancellationToken cancellationToken = default)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = GetShell(),
                Arguments = GetShellArgs(command.Command),
                WorkingDirectory = command.WorkingDirectory ?? fallbackWorkingDirectory,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardInputEncoding = Encoding.UTF8,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            },
        };

        foreach (var entry in ambientEnvironment)
            process.StartInfo.Environment[entry.Key] = entry.Value;

        foreach (var entry in command.Environment)
            process.StartInfo.Environment[entry.Key] = entry.Value;

        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        await process.StandardInput.WriteAsync(payloadJson);
        await process.StandardInput.FlushAsync(cancellationToken);
        process.StandardInput.Close();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromMilliseconds(command.TimeoutMs));

        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            await process.WaitForExitAsync(CancellationToken.None);
            return new HookCommandExecutionResult(
                -1,
                await stdoutTask,
                await stderrTask,
                TimedOut: true);
        }

        return new HookCommandExecutionResult(
            process.ExitCode,
            await stdoutTask,
            await stderrTask);
    }

    private static string GetShell()
    {
        if (OperatingSystem.IsWindows())
            return "cmd.exe";

        return Environment.GetEnvironmentVariable("SHELL") ?? "/bin/bash";
    }

    private static string GetShellArgs(string command)
    {
        if (OperatingSystem.IsWindows())
            return $"/c {command}";

        return $"-c \"{command.Replace("\"", "\\\"")}\"";
    }
}

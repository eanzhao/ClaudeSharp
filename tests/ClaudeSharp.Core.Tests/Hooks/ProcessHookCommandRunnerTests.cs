using ClaudeSharp.Core.Hooks;
using ClaudeSharp.Core.Tests.Runtime;

namespace ClaudeSharp.Core.Tests.Hooks;

/// <summary>
/// Contains tests for the process-based hook command runner.
/// </summary>
public sealed class ProcessHookCommandRunnerTests
{
    [Fact]
    public async Task ExecuteAsync_WritesPayloadAndAppliesEnvironmentAndWorkingDirectory()
    {
        using var temp = new TempDirectory();
        var workDir = temp.CreateDirectory("work");
        var runner = new ProcessHookCommandRunner();
        var payload = "payload-data";

        var result = await runner.ExecuteAsync(
            new HookCommandDefinition
            {
                EventKind = HookEventKind.SessionStart,
                Command = BuildEchoCommand(),
                WorkingDirectory = workDir,
                TimeoutMs = 2_000,
            },
            payload,
            temp.Root,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["FOO"] = "ambient",
            });

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.ExitCode);
        var parts = result.Stdout.Trim().Split('|', 4);
        Assert.Equal("ambient", parts[0]);
        Assert.Equal("command", parts[1]);
        Assert.True(Path.IsPathRooted(parts[2]));
        Assert.Equal(payload, parts[3].TrimStart('\uFEFF'));
        Assert.Equal(string.Empty, result.Stderr);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsFailureDetailsWhenCommandExitsNonZero()
    {
        using var temp = new TempDirectory();
        var runner = new ProcessHookCommandRunner();

        var result = await runner.ExecuteAsync(
            new HookCommandDefinition
            {
                EventKind = HookEventKind.Stop,
                Command = BuildFailureCommand(),
            },
            "payload",
            temp.Root,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        Assert.False(result.Succeeded);
        Assert.False(result.TimedOut);
        Assert.Equal(7, result.ExitCode);
        Assert.Equal(string.Empty, result.Stdout);
        Assert.Equal("boom", result.Stderr.Trim());
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsTimedOutResultWhenCommandDoesNotFinish()
    {
        using var temp = new TempDirectory();
        var runner = new ProcessHookCommandRunner();

        var result = await runner.ExecuteAsync(
            new HookCommandDefinition
            {
                EventKind = HookEventKind.PreCompact,
                Command = BuildSlowCommand(),
                TimeoutMs = 25,
            },
            "payload",
            temp.Root,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        Assert.False(result.Succeeded);
        Assert.True(result.TimedOut);
        Assert.Equal(-1, result.ExitCode);
    }

    private static string BuildEchoCommand() =>
        OperatingSystem.IsWindows()
            ? "set /p PAYLOAD= & echo %FOO%^|command^|%CD%^|%PAYLOAD%"
            : "printf '%s|%s|%s|%s' \"$FOO\" \"command\" \"$(pwd)\" \"$(cat)\"";

    private static string BuildFailureCommand() =>
        OperatingSystem.IsWindows()
            ? "set /p DUMMY= & echo boom 1>&2 & exit /b 7"
            : "cat >/dev/null; printf 'boom' 1>&2; exit 7";

    private static string BuildSlowCommand() =>
        OperatingSystem.IsWindows()
            ? "ping 127.0.0.1 -n 5 >nul"
            : "sleep 5";
}

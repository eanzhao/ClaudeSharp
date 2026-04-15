namespace Aexon.Core.Hooks;

/// <summary>
/// Represents the result of building a configured hook runtime.
/// </summary>
public sealed record HookRuntimeBuildResult(
    HookRuntime Runtime,
    IReadOnlyList<string> StartupMessages,
    IReadOnlyList<string> SourcePaths,
    int CommandCount)
{
    public string? StartupSummary =>
        StartupMessages.Count == 0
            ? null
            : string.Join(Environment.NewLine, StartupMessages);
}

/// <summary>
/// Builds a hook runtime from settings.json files.
/// </summary>
public static class HookRuntimeBuilder
{
    public static HookRuntimeBuildResult Build(
        string workingDirectory,
        string? explicitConfigPath = null,
        IHookCommandRunner? runner = null)
    {
        var loadResult = HookSettingsLoader.Load(workingDirectory, explicitConfigPath);
        var runtime = new HookRuntime();
        var startupMessages = new List<string>(loadResult.Diagnostics);

        if (loadResult.Commands.Count > 0)
        {
            runtime.Register(new CommandHookObserver(
                loadResult.Commands,
                runner));

            var eventCount = loadResult.Commands
                .Select(command => command.EventKind)
                .Distinct()
                .Count();
            startupMessages.Add(
                $"Hooks: loaded {loadResult.Commands.Count} command(s) across {eventCount} event(s).");
        }

        return new HookRuntimeBuildResult(
            runtime,
            startupMessages,
            loadResult.SourcePaths,
            loadResult.Commands.Count);
    }
}

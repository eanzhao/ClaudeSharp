namespace ClaudeSharp.Core.Hooks;

/// <summary>
/// Represents a shell command bound to a hook event.
/// </summary>
public sealed record HookCommandDefinition
{
    public required HookEventKind EventKind { get; init; }
    public required string Command { get; init; }
    public int TimeoutMs { get; init; } = 5_000;
    public string? WorkingDirectory { get; init; }
    public IReadOnlyDictionary<string, string> Environment { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public bool FailOpen { get; init; } = true;
    public string? SourcePath { get; init; }
}

/// <summary>
/// Represents the result of loading hook settings.
/// </summary>
public sealed record HookSettingsLoadResult(
    IReadOnlyList<HookCommandDefinition> Commands,
    IReadOnlyList<string> Diagnostics,
    IReadOnlyList<string> SourcePaths);

namespace ClaudeSharp.Core.Agents;

/// <summary>
/// Represents configurable subagent runtime settings.
/// </summary>
public sealed record AgentSettings
{
    public int BackgroundRunConcurrency { get; init; } = 1;
}

/// <summary>
/// Represents the result of loading subagent settings.
/// </summary>
public sealed record AgentSettingsLoadResult(
    AgentSettings Settings,
    IReadOnlyList<string> Diagnostics,
    IReadOnlyList<string> SourcePaths)
{
    public string? StartupSummary
    {
        get
        {
            var messages = new List<string>(Diagnostics);
            if (SourcePaths.Count > 0 || Settings.BackgroundRunConcurrency != 1)
            {
                messages.Add(
                    $"Agents: background concurrency set to {Settings.BackgroundRunConcurrency}.");
            }

            return messages.Count == 0
                ? null
                : string.Join(Environment.NewLine, messages);
        }
    }
}

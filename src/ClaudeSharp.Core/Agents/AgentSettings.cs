namespace ClaudeSharp.Core.Agents;

/// <summary>
/// Represents configurable subagent runtime settings.
/// </summary>
public sealed record AgentSettings
{
    public int BackgroundRunConcurrency { get; init; } = 1;
    public int RetainCompletedBackgroundRuns { get; init; } = 100;
    public int RetainCompletedWorkItems { get; init; } = 100;

    public AgentRetentionPolicy BuildRetentionPolicy() =>
        new()
        {
            RetainTerminalBackgroundRuns = RetainCompletedBackgroundRuns,
            RetainTerminalWorkItems = RetainCompletedWorkItems,
        };
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

            if (SourcePaths.Count > 0 ||
                Settings.RetainCompletedBackgroundRuns != 100 ||
                Settings.RetainCompletedWorkItems != 100)
            {
                messages.Add(
                    $"Agents: retain {Settings.RetainCompletedBackgroundRuns} completed background runs and {Settings.RetainCompletedWorkItems} completed work items.");
            }

            return messages.Count == 0
                ? null
                : string.Join(Environment.NewLine, messages);
        }
    }
}

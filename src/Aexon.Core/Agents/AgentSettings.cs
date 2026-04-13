namespace Aexon.Core.Agents;

/// <summary>
/// Defines how approved work items should be resumed automatically.
/// </summary>
public enum AgentAutoResumeMode
{
    Queue = 0,
    Latest = 1,
    Disabled = 2,
}

/// <summary>
/// Represents configurable subagent runtime settings.
/// </summary>
public sealed record AgentSettings
{
    public int BackgroundRunConcurrency { get; init; } = 1;
    public int RetainCompletedBackgroundRuns { get; init; } = 100;
    public int RetainCompletedWorkItems { get; init; } = 100;
    public AgentAutoResumeMode AutoResumeMode { get; init; } = AgentAutoResumeMode.Queue;

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

            if (SourcePaths.Count > 0 || Settings.AutoResumeMode != AgentAutoResumeMode.Queue)
            {
                messages.Add(
                    $"Agents: auto-resume mode set to {Settings.AutoResumeMode.ToString().ToLowerInvariant()}.");
            }

            return messages.Count == 0
                ? null
                : string.Join(Environment.NewLine, messages);
        }
    }
}

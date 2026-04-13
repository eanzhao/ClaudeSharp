using System.Text.Json;
using ClaudeSharp.Core.Configuration;

namespace ClaudeSharp.Core.Agents;

/// <summary>
/// Loads subagent runtime settings from explicit, project, and user config files.
/// </summary>
public static class AgentSettingsLoader
{
    public static AgentSettingsLoadResult Load(
        string workingDirectory,
        string? explicitConfigPath = null) =>
        LoadFromFiles(
            SettingsFileLocator.GetCandidatePaths(workingDirectory, explicitConfigPath),
            workingDirectory);

    public static AgentSettingsLoadResult LoadFromFiles(
        IEnumerable<string> configPaths,
        string workingDirectory)
    {
        var settings = new AgentSettings();
        var diagnostics = new List<string>();
        var sources = new List<string>();

        foreach (var configPath in configPaths
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Select(Path.GetFullPath)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(configPath))
                continue;

            try
            {
                settings = ParseFile(configPath, workingDirectory, diagnostics, settings);
                sources.Add(configPath);
            }
            catch (Exception ex)
            {
                diagnostics.Add($"Agent settings: failed to load {configPath}: {ex.Message}");
            }
        }

        return new AgentSettingsLoadResult(settings, diagnostics, sources);
    }

    private static AgentSettings ParseFile(
        string configPath,
        string workingDirectory,
        ICollection<string> diagnostics,
        AgentSettings current)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(configPath));
        var root = document.RootElement;

        if (!TryGetProperty(root, "agents", out var agentsElement))
            return current;

        if (agentsElement.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add($"Agent settings: {configPath} has a non-object agents value.");
            return current;
        }

        var backgroundRunConcurrency = current.BackgroundRunConcurrency;
        var retainCompletedBackgroundRuns = current.RetainCompletedBackgroundRuns;
        var retainCompletedWorkItems = current.RetainCompletedWorkItems;
        var autoResumeMode = current.AutoResumeMode;
        if (TryGetProperty(agentsElement, "backgroundRunConcurrency", out var concurrencyElement) ||
            TryGetProperty(agentsElement, "background_run_concurrency", out concurrencyElement) ||
            TryGetProperty(agentsElement, "backgroundConcurrency", out concurrencyElement) ||
            TryGetProperty(agentsElement, "background_concurrency", out concurrencyElement))
        {
            if (concurrencyElement.ValueKind == JsonValueKind.Number &&
                concurrencyElement.TryGetInt32(out var parsed) &&
                parsed > 0)
            {
                backgroundRunConcurrency = parsed;
            }
            else
            {
                diagnostics.Add(
                    $"Agent settings: invalid background concurrency in {configPath}; using {backgroundRunConcurrency}.");
            }
        }

        if (TryGetProperty(agentsElement, "retainCompletedBackgroundRuns", out var retainRunsElement) ||
            TryGetProperty(agentsElement, "retain_completed_background_runs", out retainRunsElement) ||
            TryGetProperty(agentsElement, "completedBackgroundRunRetention", out retainRunsElement) ||
            TryGetProperty(agentsElement, "completed_background_run_retention", out retainRunsElement))
        {
            if (TryParseNonNegativeInt(retainRunsElement, out var parsed))
            {
                retainCompletedBackgroundRuns = parsed;
            }
            else
            {
                diagnostics.Add(
                    $"Agent settings: invalid completed background-run retention in {configPath}; using {retainCompletedBackgroundRuns}.");
            }
        }

        if (TryGetProperty(agentsElement, "retainCompletedWorkItems", out var retainItemsElement) ||
            TryGetProperty(agentsElement, "retain_completed_work_items", out retainItemsElement) ||
            TryGetProperty(agentsElement, "completedWorkItemRetention", out retainItemsElement) ||
            TryGetProperty(agentsElement, "completed_work_item_retention", out retainItemsElement))
        {
            if (TryParseNonNegativeInt(retainItemsElement, out var parsed))
            {
                retainCompletedWorkItems = parsed;
            }
            else
            {
                diagnostics.Add(
                    $"Agent settings: invalid completed work-item retention in {configPath}; using {retainCompletedWorkItems}.");
            }
        }

        if (TryGetProperty(agentsElement, "autoResumeMode", out var autoResumeElement) ||
            TryGetProperty(agentsElement, "auto_resume_mode", out autoResumeElement) ||
            TryGetProperty(agentsElement, "autoResumePolicy", out autoResumeElement) ||
            TryGetProperty(agentsElement, "auto_resume_policy", out autoResumeElement))
        {
            if (TryParseAutoResumeMode(autoResumeElement, out var parsed))
            {
                autoResumeMode = parsed;
            }
            else
            {
                diagnostics.Add(
                    $"Agent settings: invalid auto-resume mode in {configPath}; using {autoResumeMode.ToString().ToLowerInvariant()}.");
            }
        }

        return current with
        {
            BackgroundRunConcurrency = backgroundRunConcurrency,
            RetainCompletedBackgroundRuns = retainCompletedBackgroundRuns,
            RetainCompletedWorkItems = retainCompletedWorkItems,
            AutoResumeMode = autoResumeMode,
        };
    }

    private static bool TryParseAutoResumeMode(
        JsonElement element,
        out AgentAutoResumeMode mode)
    {
        if (element.ValueKind != JsonValueKind.String)
        {
            mode = default;
            return false;
        }

        return AgentAutoResumeModeParser.TryParse(element.GetString(), out mode);
    }

    private static bool TryParseNonNegativeInt(JsonElement element, out int value)
    {
        value = default;
        return element.ValueKind == JsonValueKind.Number &&
               element.TryGetInt32(out value) &&
               value >= 0;
    }

    private static bool TryGetProperty(
        JsonElement element,
        string propertyName,
        out JsonElement property)
    {
        foreach (var candidate in element.EnumerateObject())
        {
            if (string.Equals(candidate.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                property = candidate.Value;
                return true;
            }
        }

        property = default;
        return false;
    }
}

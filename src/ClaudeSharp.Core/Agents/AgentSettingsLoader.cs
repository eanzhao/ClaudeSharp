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

        return current with
        {
            BackgroundRunConcurrency = backgroundRunConcurrency,
        };
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

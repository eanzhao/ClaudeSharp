using System.Text.Json;

namespace ClaudeSharp.Core.Configuration;

/// <summary>
/// Represents resolved Anthropic client settings.
/// </summary>
public sealed record AnthropicClientSettings(
    string? ApiKey,
    string? BaseUrl,
    bool ApiKeyFromEnvironment,
    bool ApiKeyFromAppSettings,
    string? SourcePath,
    IReadOnlyList<string> Diagnostics)
{
    public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey);

    public string? StartupSummary
    {
        get
        {
            var messages = new List<string>(Diagnostics);
            var sourceName = string.IsNullOrWhiteSpace(SourcePath)
                ? "appsettings.json"
                : Path.GetFileName(SourcePath);

            if (ApiKeyFromEnvironment)
                messages.Add($"Anthropic config: using API key from ANTHROPIC_API_KEY.");
            else if (ApiKeyFromAppSettings)
                messages.Add($"Anthropic config: using API key from {sourceName}.");

            if (!string.IsNullOrWhiteSpace(BaseUrl))
                messages.Add($"Anthropic config: using base URL from {sourceName}.");

            return messages.Count == 0
                ? null
                : string.Join(Environment.NewLine, messages);
        }
    }
}

/// <summary>
/// Loads Anthropic client settings from environment variables and appsettings.json.
/// </summary>
public static class AnthropicClientSettingsLoader
{
    public static AnthropicClientSettings Load(
        string workingDirectory,
        Func<string, string?>? getEnvironmentVariable = null,
        string? appBaseDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        getEnvironmentVariable ??= Environment.GetEnvironmentVariable;
        string? appSettingsApiKey = null;
        string? baseUrl = null;
        string? sourcePath = null;
        var diagnostics = new List<string>();

        foreach (var appSettingsPath in GetCandidatePaths(workingDirectory, appBaseDirectory))
        {
            if (!File.Exists(appSettingsPath))
                continue;

            sourcePath = Path.GetFullPath(appSettingsPath);

            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(appSettingsPath));
                var section = FindAnthropicSection(document.RootElement, diagnostics);
                if (!section.HasValue)
                    continue;

                appSettingsApiKey = TryReadString(section.Value, "apiKey", diagnostics, "Anthropic.apiKey");
                baseUrl = TryReadBaseUrl(section.Value, diagnostics);
                break;
            }
            catch (JsonException ex)
            {
                diagnostics.Add($"Anthropic config: failed to parse {Path.GetFileName(appSettingsPath)}: {ex.Message}");
            }
            catch (IOException ex)
            {
                diagnostics.Add($"Anthropic config: failed to read {Path.GetFileName(appSettingsPath)}: {ex.Message}");
            }
        }

        var environmentApiKey = getEnvironmentVariable("ANTHROPIC_API_KEY")?.Trim();
        if (string.IsNullOrWhiteSpace(environmentApiKey))
            environmentApiKey = null;

        return new AnthropicClientSettings(
            ApiKey: environmentApiKey ?? appSettingsApiKey,
            BaseUrl: baseUrl,
            ApiKeyFromEnvironment: environmentApiKey != null,
            ApiKeyFromAppSettings: environmentApiKey == null && !string.IsNullOrWhiteSpace(appSettingsApiKey),
            SourcePath: sourcePath,
            Diagnostics: diagnostics);
    }

    private static IReadOnlyList<string> GetCandidatePaths(
        string workingDirectory,
        string? appBaseDirectory)
    {
        var candidates = new List<string>
        {
            Path.Combine(workingDirectory, "appsettings.json"),
        };

        if (!string.IsNullOrWhiteSpace(appBaseDirectory))
            candidates.Add(Path.Combine(appBaseDirectory, "appsettings.json"));

        return candidates
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static JsonElement? FindAnthropicSection(
        JsonElement root,
        List<string> diagnostics)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add("Anthropic config: appsettings.json root must be a JSON object.");
            return null;
        }

        if (TryGetPropertyCaseInsensitive(root, "ClaudeSharp", out var claudeSharp))
        {
            if (claudeSharp.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add("Anthropic config: ClaudeSharp section must be a JSON object.");
            }
            else if (TryGetPropertyCaseInsensitive(claudeSharp, "Anthropic", out var nestedAnthropic))
            {
                if (nestedAnthropic.ValueKind != JsonValueKind.Object)
                {
                    diagnostics.Add("Anthropic config: ClaudeSharp.Anthropic must be a JSON object.");
                    return null;
                }

                return nestedAnthropic;
            }
        }

        if (TryGetPropertyCaseInsensitive(root, "Anthropic", out var anthropic))
        {
            if (anthropic.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add("Anthropic config: Anthropic section must be a JSON object.");
                return null;
            }

            return anthropic;
        }

        return null;
    }

    private static string? TryReadBaseUrl(
        JsonElement section,
        List<string> diagnostics)
    {
        var value = TryReadString(section, "baseUrl", diagnostics, "Anthropic.baseUrl");
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (Uri.TryCreate(value, UriKind.Absolute, out _))
            return value;

        diagnostics.Add("Anthropic config: Anthropic.baseUrl must be an absolute URL.");
        return null;
    }

    private static string? TryReadString(
        JsonElement section,
        string propertyName,
        List<string> diagnostics,
        string label)
    {
        if (!TryGetPropertyCaseInsensitive(section, propertyName, out var property))
            return null;

        if (property.ValueKind != JsonValueKind.String)
        {
            diagnostics.Add($"Anthropic config: {label} must be a string.");
            return null;
        }

        var value = property.GetString()?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static bool TryGetPropertyCaseInsensitive(
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

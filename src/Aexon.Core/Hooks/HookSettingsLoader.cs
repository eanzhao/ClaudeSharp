using System.Text.Json;
using Aexon.Core.Configuration;

namespace Aexon.Core.Hooks;

/// <summary>
/// Loads hook command definitions from settings.json files.
/// </summary>
public static class HookSettingsLoader
{
    public static HookSettingsLoadResult Load(
        string workingDirectory,
        string? explicitConfigPath = null) =>
        LoadFromFiles(
            SettingsFileLocator.GetCandidatePaths(workingDirectory, explicitConfigPath),
            workingDirectory);

    public static HookSettingsLoadResult LoadFromFiles(
        IEnumerable<string> configPaths,
        string workingDirectory)
    {
        var commands = new List<HookCommandDefinition>();
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
                var parsed = ParseFile(configPath, workingDirectory, diagnostics);
                commands.AddRange(parsed);
                sources.Add(configPath);
            }
            catch (Exception ex)
            {
                diagnostics.Add($"Hook settings: failed to load {configPath}: {ex.Message}");
            }
        }

        return new HookSettingsLoadResult(commands, diagnostics, sources);
    }

    private static IReadOnlyList<HookCommandDefinition> ParseFile(
        string configPath,
        string workingDirectory,
        ICollection<string> diagnostics)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(configPath));
        var root = document.RootElement;

        if (!TryGetProperty(root, "hooks", out var hooksElement))
            return [];

        if (hooksElement.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add($"Hook settings: {configPath} has a non-object hooks value.");
            return [];
        }

        var configDirectory = Path.GetDirectoryName(configPath) ?? workingDirectory;
        var commands = new List<HookCommandDefinition>();

        foreach (var property in hooksElement.EnumerateObject())
        {
            if (!TryParseEventKind(property.Name, out var eventKind))
            {
                diagnostics.Add($"Hook settings: unknown hook event {property.Name} in {configPath}.");
                continue;
            }

            foreach (var command in ParseEventCommands(
                         eventKind,
                         property.Value,
                         configDirectory,
                         configPath,
                         diagnostics))
            {
                commands.Add(command);
            }
        }

        return commands;
    }

    private static IReadOnlyList<HookCommandDefinition> ParseEventCommands(
        HookEventKind eventKind,
        JsonElement element,
        string configDirectory,
        string sourcePath,
        ICollection<string> diagnostics)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Array => element.EnumerateArray()
                .Select((item, index) => ParseCommand(eventKind, item, configDirectory, sourcePath, diagnostics, index))
                .Where(command => command != null)
                .Select(command => command!)
                .ToArray(),
            JsonValueKind.String or JsonValueKind.Object =>
                ParseSingleCommand(eventKind, element, configDirectory, sourcePath, diagnostics) is { } command
                    ? [command]
                    : [],
            _ => AddInvalidEventDiagnostic(eventKind, sourcePath, diagnostics),
        };
    }

    private static IReadOnlyList<HookCommandDefinition> AddInvalidEventDiagnostic(
        HookEventKind eventKind,
        string sourcePath,
        ICollection<string> diagnostics)
    {
        diagnostics.Add(
            $"Hook settings: {eventKind} in {sourcePath} must be a command string, object, or array.");
        return [];
    }

    private static HookCommandDefinition? ParseCommand(
        HookEventKind eventKind,
        JsonElement element,
        string configDirectory,
        string sourcePath,
        ICollection<string> diagnostics,
        int index)
    {
        var command = ParseSingleCommand(eventKind, element, configDirectory, sourcePath, diagnostics);
        if (command != null)
            return command;

        diagnostics.Add(
            $"Hook settings: ignored invalid entry #{index + 1} for {eventKind} in {sourcePath}.");
        return null;
    }

    private static HookCommandDefinition? ParseSingleCommand(
        HookEventKind eventKind,
        JsonElement element,
        string configDirectory,
        string sourcePath,
        ICollection<string> diagnostics)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            var inlineCommand = element.GetString();
            if (string.IsNullOrWhiteSpace(inlineCommand))
            {
                diagnostics.Add($"Hook settings: {eventKind} in {sourcePath} contains an empty command.");
                return null;
            }

            return new HookCommandDefinition
            {
                EventKind = eventKind,
                Command = inlineCommand,
                SourcePath = sourcePath,
            };
        }

        if (element.ValueKind != JsonValueKind.Object)
            return null;

        var command = TryGetString(element, "command");
        if (string.IsNullOrWhiteSpace(command))
        {
            diagnostics.Add($"Hook settings: {eventKind} in {sourcePath} is missing command.");
            return null;
        }

        var enabled = !TryGetBool(element, "disabled");
        if (TryGetProperty(element, "enabled", out var enabledProperty) &&
            enabledProperty.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            enabled = enabledProperty.GetBoolean();
        }

        if (!enabled)
            return null;

        return new HookCommandDefinition
        {
            EventKind = eventKind,
            Command = command,
            TimeoutMs = ParseTimeout(element, sourcePath, diagnostics),
            WorkingDirectory = ResolveWorkingDirectory(
                TryGetString(element, "cwd") ?? TryGetString(element, "workingDirectory"),
                configDirectory),
            Environment = ParseEnvironment(element),
            FailOpen = !TryGetProperty(element, "failOpen", out var failOpenProperty) ||
                       failOpenProperty.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                       failOpenProperty.GetBoolean(),
            SourcePath = sourcePath,
        };
    }

    private static int ParseTimeout(
        JsonElement element,
        string sourcePath,
        ICollection<string> diagnostics)
    {
        if (!TryGetProperty(element, "timeout", out var timeoutElement) &&
            !TryGetProperty(element, "timeoutMs", out timeoutElement) &&
            !TryGetProperty(element, "timeout_ms", out timeoutElement))
        {
            return 5_000;
        }

        if (timeoutElement.ValueKind == JsonValueKind.Number &&
            timeoutElement.TryGetInt32(out var parsed) &&
            parsed > 0)
        {
            return parsed;
        }

        diagnostics.Add($"Hook settings: invalid timeout in {sourcePath}; using 5000ms.");
        return 5_000;
    }

    private static IReadOnlyDictionary<string, string> ParseEnvironment(JsonElement element)
    {
        if (!TryGetProperty(element, "env", out var envElement) ||
            envElement.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in envElement.EnumerateObject())
        {
            environment[property.Name] = property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString() ?? string.Empty
                : property.Value.GetRawText();
        }

        return environment;
    }

    private static bool TryParseEventKind(string value, out HookEventKind eventKind)
    {
        var normalized = Normalize(value);
        eventKind = normalized switch
        {
            "pretooluse" => HookEventKind.PreToolUse,
            "posttooluse" => HookEventKind.PostToolUse,
            "posttoolusefailure" => HookEventKind.PostToolUseFailure,
            "permissionrequest" => HookEventKind.PermissionRequest,
            "sessionstart" => HookEventKind.SessionStart,
            "sessionend" => HookEventKind.SessionEnd,
            "stop" => HookEventKind.Stop,
            "stopfailure" => HookEventKind.StopFailure,
            "precompact" => HookEventKind.PreCompact,
            "postcompact" => HookEventKind.PostCompact,
            _ => default,
        };

        return normalized is
            "pretooluse" or
            "posttooluse" or
            "posttoolusefailure" or
            "permissionrequest" or
            "sessionstart" or
            "sessionend" or
            "stop" or
            "stopfailure" or
            "precompact" or
            "postcompact";
    }

    private static string Normalize(string value)
    {
        var buffer = new List<char>(value.Length);
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
                buffer.Add(char.ToLowerInvariant(ch));
        }

        return new string([.. buffer]);
    }

    private static string? ResolveWorkingDirectory(string? path, string configDirectory)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(configDirectory, path));
    }

    private static bool TryGetProperty(
        JsonElement element,
        string propertyName,
        out JsonElement property)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            property = default;
            return false;
        }

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

    private static string? TryGetString(JsonElement element, string propertyName) =>
        TryGetProperty(element, propertyName, out var property) &&
        property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static bool TryGetBool(JsonElement element, string propertyName) =>
        TryGetProperty(element, propertyName, out var property) &&
        property.ValueKind is JsonValueKind.True or JsonValueKind.False &&
        property.GetBoolean();
}

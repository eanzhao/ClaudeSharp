using System.Text.Json;
using ClaudeSharp.Core.Configuration;

namespace ClaudeSharp.Core.Mcp;

/// <summary>
/// Loads MCP server settings from explicit, project, and user config files.
/// </summary>
public static class McpSettingsLoader
{
    public static McpSettingsLoadResult Load(
        string workingDirectory,
        string? explicitConfigPath = null) =>
        LoadFromFiles(
            SettingsFileLocator.GetCandidatePaths(workingDirectory, explicitConfigPath),
            workingDirectory);

    public static McpSettingsLoadResult LoadFromFiles(
        IEnumerable<string> configPaths,
        string workingDirectory)
    {
        var merged = new Dictionary<string, McpServerConfig>(StringComparer.OrdinalIgnoreCase);
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
                foreach (var server in parsed)
                    merged[server.ServerId] = server;

                sources.Add(configPath);
            }
            catch (Exception ex)
            {
                diagnostics.Add($"MCP settings: failed to load {configPath}: {ex.Message}");
            }
        }

        return new McpSettingsLoadResult(
            merged.Values
                .OrderBy(server => server.ServerId, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            diagnostics,
            sources);
    }

    private static IReadOnlyList<McpServerConfig> ParseFile(
        string configPath,
        string workingDirectory,
        ICollection<string> diagnostics)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(configPath));
        var root = document.RootElement;

        if (!TryGetProperty(root, "mcpServers", out var serversElement) &&
            !TryGetProperty(root, "mcp_servers", out serversElement))
        {
            return [];
        }

        if (serversElement.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add($"MCP settings: {configPath} has a non-object mcpServers value.");
            return [];
        }

        var configDirectory = Path.GetDirectoryName(configPath) ?? workingDirectory;
        var servers = new List<McpServerConfig>();

        foreach (var property in serversElement.EnumerateObject())
        {
            var serverId = property.Name.Trim();
            if (string.IsNullOrWhiteSpace(serverId))
            {
                diagnostics.Add($"MCP settings: {configPath} contains an empty server id.");
                continue;
            }

            if (property.Value.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add($"MCP settings: server {serverId} in {configPath} is not an object.");
                continue;
            }

            var server = ParseServer(
                serverId,
                property.Value,
                configDirectory,
                configPath,
                diagnostics);

            if (server != null)
                servers.Add(server);
        }

        return servers;
    }

    private static McpServerConfig? ParseServer(
        string serverId,
        JsonElement element,
        string configDirectory,
        string sourcePath,
        ICollection<string> diagnostics)
    {
        var command = TryGetString(element, "command");
        var transportUrl = TryGetString(element, "url");
        if (string.IsNullOrWhiteSpace(command))
        {
            if (!string.IsNullOrWhiteSpace(transportUrl))
            {
                diagnostics.Add(
                    $"MCP settings: server {serverId} uses url transport in {sourcePath}, but only stdio is implemented.");
            }
            else
            {
                diagnostics.Add($"MCP settings: server {serverId} in {sourcePath} is missing command.");
            }

            return null;
        }

        var args = ParseArgs(element);
        var workingDirectory = ResolveWorkingDirectory(
            TryGetString(element, "cwd") ?? TryGetString(element, "workingDirectory"),
            configDirectory);
        var environment = ParseEnvironment(element);
        var disabled = TryGetBool(element, "disabled");

        return new McpServerConfig
        {
            ServerId = serverId,
            Command = command,
            Args = args,
            WorkingDirectory = workingDirectory,
            Environment = environment,
            Disabled = disabled,
            SourcePath = sourcePath,
        };
    }

    private static IReadOnlyList<string> ParseArgs(JsonElement element)
    {
        if (!TryGetProperty(element, "args", out var argsElement) ||
            argsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var args = new List<string>();
        foreach (var value in argsElement.EnumerateArray())
        {
            if (value.ValueKind == JsonValueKind.String)
                args.Add(value.GetString() ?? string.Empty);
        }

        return args;
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
            if (property.Value.ValueKind == JsonValueKind.String)
            {
                environment[property.Name] = property.Value.GetString() ?? string.Empty;
            }
            else
            {
                environment[property.Name] = property.Value.GetRawText();
            }
        }

        return environment;
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

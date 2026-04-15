namespace Aexon.Core.Mcp;

/// <summary>
/// Represents an MCP server configuration entry.
/// </summary>
public sealed record McpServerConfig
{
    public required string ServerId { get; init; }
    public required string Command { get; init; }
    public IReadOnlyList<string> Args { get; init; } = [];
    public string? WorkingDirectory { get; init; }
    public IReadOnlyDictionary<string, string> Environment { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public bool Disabled { get; init; }
    public string? SourcePath { get; init; }
}

/// <summary>
/// Represents the result of loading MCP settings.
/// </summary>
public sealed record McpSettingsLoadResult(
    IReadOnlyList<McpServerConfig> Servers,
    IReadOnlyList<string> Diagnostics,
    IReadOnlyList<string> SourcePaths);

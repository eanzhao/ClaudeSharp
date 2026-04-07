using System.Text.Json;

namespace ClaudeSharp.Core.Mcp;

/// <summary>
/// Represents mcp tool descriptor.
/// </summary>
public sealed record McpToolDescriptor
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required JsonElement InputSchema { get; init; }
    public bool ReadOnlyHint { get; init; }
    public bool OpenWorldHint { get; init; }
    public bool AlwaysLoad { get; init; }
    public string? SearchHint { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

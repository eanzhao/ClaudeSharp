using ClaudeSharp.Core.Agents;
using ClaudeSharp.Core.Mcp;
using ClaudeSharp.Core.Permissions;

namespace ClaudeSharp.Core.AppState;

/// <summary>
/// Represents app state snapshot.
/// </summary>
public sealed record AppStateSnapshot
{
    public string? SessionId { get; init; }
    public string WorkingDirectory { get; init; } = Environment.CurrentDirectory;
    public PermissionMode PermissionMode { get; init; } = PermissionMode.Default;
    public string? ActiveTaskId { get; init; }
    public string? MemoryRootDirectory { get; init; }
    public IReadOnlyDictionary<string, McpConnectionState> McpConnections { get; init; } =
        new Dictionary<string, McpConnectionState>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, AgentWorkItemStatus> WorkItems { get; init; } =
        new Dictionary<string, AgentWorkItemStatus>(StringComparer.OrdinalIgnoreCase);
}

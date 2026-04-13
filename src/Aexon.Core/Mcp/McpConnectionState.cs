namespace Aexon.Core.Mcp;

/// <summary>
/// Defines the lifecycle states for an MCP connection.
/// </summary>
public enum McpConnectionState
{
    Pending,
    Connected,
    NeedsAuth,
    Failed,
    Disabled,
}

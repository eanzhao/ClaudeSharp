namespace Aexon.Core.Mcp;

/// <summary>
/// Represents mcp connection snapshot.
/// </summary>
public sealed record McpConnectionSnapshot(
    string ServerId,
    Uri? Endpoint,
    McpConnectionState State,
    int ToolCount,
    DateTimeOffset LastUpdatedAt);

/// <summary>
/// Provides mcp connection manager.
/// </summary>
public sealed class McpConnectionManager
{
    private readonly Dictionary<string, McpConnection> _connections =
        new(StringComparer.OrdinalIgnoreCase);

    public void Register(McpConnection connection)
    {
        _connections[connection.ServerId] = connection;
    }

    public bool Remove(string serverId) =>
        _connections.Remove(serverId);

    public bool TryGet(string serverId, out McpConnection? connection) =>
        _connections.TryGetValue(serverId, out connection);

    public bool UpdateState(string serverId, McpConnectionState state)
    {
        if (!_connections.TryGetValue(serverId, out var connection))
            return false;

        connection.UpdateState(state);
        return true;
    }

    public bool UpdateTools(string serverId, IEnumerable<McpToolDescriptor> tools)
    {
        if (!_connections.TryGetValue(serverId, out var connection))
            return false;

        connection.ReplaceTools(tools);
        return true;
    }

    public IReadOnlyList<McpConnectionSnapshot> Snapshot() =>
        _connections.Values
            .OrderBy(connection => connection.ServerId, StringComparer.OrdinalIgnoreCase)
            .Select(connection => new McpConnectionSnapshot(
                connection.ServerId,
                connection.Endpoint,
                connection.State,
                connection.Tools.Count,
                connection.LastUpdatedAt))
            .ToArray();
}

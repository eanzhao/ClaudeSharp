namespace ClaudeSharp.Core.Mcp;

/// <summary>
/// Defines the contract for mcp connection.
/// </summary>
public interface IMcpConnection
{
    string ServerId { get; }
    Uri? Endpoint { get; }
    McpConnectionState State { get; }
    IReadOnlyList<McpToolDescriptor> Tools { get; }
}

/// <summary>
/// Represents mcp connection.
/// </summary>
public sealed class McpConnection : IMcpConnection
{
    private readonly List<McpToolDescriptor> _tools = [];

    public McpConnection(
        string serverId,
        Uri? endpoint = null,
        McpConnectionState state = McpConnectionState.Pending,
        IEnumerable<McpToolDescriptor>? tools = null)
    {
        ServerId = serverId;
        Endpoint = endpoint;
        State = state;

        if (tools != null)
            _tools.AddRange(tools);
    }

    public string ServerId { get; }
    public Uri? Endpoint { get; }
    public McpConnectionState State { get; private set; }
    public IReadOnlyList<McpToolDescriptor> Tools => _tools;
    public DateTimeOffset LastUpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public void UpdateState(McpConnectionState state)
    {
        State = state;
        LastUpdatedAt = DateTimeOffset.UtcNow;
    }

    public void ReplaceTools(IEnumerable<McpToolDescriptor> tools)
    {
        _tools.Clear();
        _tools.AddRange(tools);
        LastUpdatedAt = DateTimeOffset.UtcNow;
    }
}

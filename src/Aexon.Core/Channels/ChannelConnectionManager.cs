using Aexon.Core.Messages;

namespace Aexon.Core.Channels;

/// <summary>
/// Represents a channel connection snapshot for observability.
/// </summary>
public sealed record ChannelConnectionSnapshot(
    string ChannelId,
    ChannelKind Kind,
    string Target,
    ChannelConnectionState State,
    string? ErrorMessage,
    DateTimeOffset LastUpdatedAt);

/// <summary>
/// Manages channel connections for bridge and UDS transports.
/// </summary>
public sealed class ChannelConnectionManager
{
    private readonly Dictionary<string, ChannelConnection> _connections =
        new(StringComparer.OrdinalIgnoreCase);
    private Func<ConversationMessage, CancellationToken, Task>? _messageSink;

    public void SetMessageSink(Func<ConversationMessage, CancellationToken, Task>? messageSink) =>
        _messageSink = messageSink;

    public ChannelConnection Register(string channelId, ChannelKind kind, string target)
    {
        var connection = new ChannelConnection(channelId, kind, target);
        _connections[channelId] = connection;
        return connection;
    }

    public bool Remove(string channelId) =>
        _connections.Remove(channelId);

    public bool TryGet(string channelId, out ChannelConnection? connection) =>
        _connections.TryGetValue(channelId, out connection);

    public bool UpdateState(string channelId, ChannelConnectionState state, string? errorMessage = null)
    {
        if (!_connections.TryGetValue(channelId, out var connection))
            return false;

        connection.UpdateState(state, errorMessage);
        EmitStatusMessage(connection);
        return true;
    }

    public IReadOnlyList<ChannelConnectionSnapshot> Snapshot() =>
        _connections.Values
            .OrderBy(connection => connection.ChannelId, StringComparer.OrdinalIgnoreCase)
            .Select(connection => new ChannelConnectionSnapshot(
                connection.ChannelId,
                connection.Kind,
                connection.Target,
                connection.State,
                connection.ErrorMessage,
                connection.LastUpdatedAt))
            .ToArray();

    private void EmitStatusMessage(ChannelConnection connection)
    {
        if (_messageSink == null)
            return;

        var message = new SystemBridgeStatusMessage
        {
            Content = $"{connection.Kind.ToString().ToLowerInvariant()} channel '{connection.Target}' is {connection.State.ToString().ToLowerInvariant()}.",
            BridgeName = connection.Target,
            Status = connection.State.ToString().ToLowerInvariant(),
            Detail = connection.ErrorMessage,
        };

        try
        {
            _messageSink(message, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch
        {
            // Connection-state messages are best-effort and must not break the transport.
        }
    }
}

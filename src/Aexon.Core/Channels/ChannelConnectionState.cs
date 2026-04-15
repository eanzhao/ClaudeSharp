namespace Aexon.Core.Channels;

/// <summary>
/// Defines lifecycle states for a bridge or UDS channel connection.
/// </summary>
public enum ChannelConnectionState
{
    Pending,
    Connecting,
    Connected,
    Disconnected,
    Failed,
}

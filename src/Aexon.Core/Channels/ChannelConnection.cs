namespace Aexon.Core.Channels;

/// <summary>
/// Defines the contract for a channel connection.
/// </summary>
public interface IChannelConnection
{
    string ChannelId { get; }
    ChannelKind Kind { get; }
    string Target { get; }
    ChannelConnectionState State { get; }
    string? ErrorMessage { get; }
}

/// <summary>
/// Represents a bridge or UDS channel connection with lifecycle tracking.
/// </summary>
public sealed class ChannelConnection : IChannelConnection
{
    public ChannelConnection(
        string channelId,
        ChannelKind kind,
        string target,
        ChannelConnectionState state = ChannelConnectionState.Pending)
    {
        ChannelId = channelId;
        Kind = kind;
        Target = target;
        State = state;
    }

    public string ChannelId { get; }
    public ChannelKind Kind { get; }
    public string Target { get; }
    public ChannelConnectionState State { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastUpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public void UpdateState(ChannelConnectionState state, string? errorMessage = null)
    {
        State = state;
        ErrorMessage = state == ChannelConnectionState.Failed ? errorMessage : null;
        LastUpdatedAt = DateTimeOffset.UtcNow;
    }
}

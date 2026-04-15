using Aexon.Core.Agents;

namespace Aexon.Core.Channels;

/// <summary>
/// Defines the result of sending a message through a channel transport.
/// </summary>
public sealed record ChannelSendResult(
    bool Success,
    string ChannelId,
    string? ErrorMessage = null)
{
    public static ChannelSendResult Sent(string channelId) => new(true, channelId);
    public static ChannelSendResult Failed(string channelId, string error) => new(false, channelId, error);
}

/// <summary>
/// Defines the contract for a channel transport that delivers messages
/// to bridge: or uds: targets.
/// </summary>
public interface IChannelTransport
{
    ChannelKind Kind { get; }

    Task<ChannelSendResult> SendAsync(
        string channelTarget,
        string sender,
        string body,
        AgentMessageKind kind = AgentMessageKind.Note,
        string? subject = null,
        AgentMessageProtocol? protocol = null,
        CancellationToken cancellationToken = default);

    Task<ChannelConnectionSnapshot?> ConnectAsync(
        string channelTarget,
        CancellationToken cancellationToken = default);

    Task DisconnectAsync(
        string channelId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Provides the bridge channel transport implementation.
/// </summary>
public sealed class BridgeChannelTransport : IChannelTransport
{
    private readonly ChannelConnectionManager _connectionManager;
    private readonly IAgentMessageRuntime _messageRuntime;

    public BridgeChannelTransport(
        ChannelConnectionManager connectionManager,
        IAgentMessageRuntime messageRuntime)
    {
        _connectionManager = connectionManager;
        _messageRuntime = messageRuntime;
    }

    public ChannelKind Kind => ChannelKind.Bridge;

    public Task<ChannelSendResult> SendAsync(
        string channelTarget,
        string sender,
        string body,
        AgentMessageKind kind = AgentMessageKind.Note,
        string? subject = null,
        AgentMessageProtocol? protocol = null,
        CancellationToken cancellationToken = default)
    {
        var channelId = NormalizeChannelId(channelTarget);

        if (!_connectionManager.TryGet(channelId, out var connection) ||
            connection == null)
        {
            connection = _connectionManager.Register(channelId, ChannelKind.Bridge, channelTarget);
        }

        if (connection.State is ChannelConnectionState.Failed)
            return Task.FromResult(ChannelSendResult.Failed(channelId, connection.ErrorMessage ?? "Channel is in a failed state."));

        if (connection.State is ChannelConnectionState.Pending or ChannelConnectionState.Connecting)
            _connectionManager.UpdateState(channelId, ChannelConnectionState.Connected);

        _messageRuntime.SendMessage(
            sender,
            channelTarget,
            body,
            kind,
            subject,
            protocol: protocol);

        return Task.FromResult(ChannelSendResult.Sent(channelId));
    }

    public Task<ChannelConnectionSnapshot?> ConnectAsync(
        string channelTarget,
        CancellationToken cancellationToken = default)
    {
        var channelId = NormalizeChannelId(channelTarget);
        var connection = _connectionManager.Register(channelId, ChannelKind.Bridge, channelTarget);
        connection.UpdateState(ChannelConnectionState.Connected);

        return Task.FromResult<ChannelConnectionSnapshot?>(new ChannelConnectionSnapshot(
            connection.ChannelId,
            connection.Kind,
            connection.Target,
            connection.State,
            connection.ErrorMessage,
            connection.LastUpdatedAt));
    }

    public Task DisconnectAsync(string channelId, CancellationToken cancellationToken = default)
    {
        if (_connectionManager.TryGet(channelId, out _))
            _connectionManager.UpdateState(channelId, ChannelConnectionState.Disconnected);

        return Task.CompletedTask;
    }

    internal static string NormalizeChannelId(string target) =>
        target.StartsWith("bridge:", StringComparison.OrdinalIgnoreCase)
            ? target[7..].Trim()
            : target.Trim();
}

/// <summary>
/// Provides the UDS (Unix Domain Socket) channel transport implementation.
/// </summary>
public sealed class UdsChannelTransport : IChannelTransport
{
    private readonly ChannelConnectionManager _connectionManager;
    private readonly IAgentMessageRuntime _messageRuntime;

    public UdsChannelTransport(
        ChannelConnectionManager connectionManager,
        IAgentMessageRuntime messageRuntime)
    {
        _connectionManager = connectionManager;
        _messageRuntime = messageRuntime;
    }

    public ChannelKind Kind => ChannelKind.Uds;

    public Task<ChannelSendResult> SendAsync(
        string channelTarget,
        string sender,
        string body,
        AgentMessageKind kind = AgentMessageKind.Note,
        string? subject = null,
        AgentMessageProtocol? protocol = null,
        CancellationToken cancellationToken = default)
    {
        var channelId = NormalizeChannelId(channelTarget);

        if (!_connectionManager.TryGet(channelId, out var connection) ||
            connection == null)
        {
            connection = _connectionManager.Register(channelId, ChannelKind.Uds, channelTarget);
        }

        if (connection.State is ChannelConnectionState.Failed)
            return Task.FromResult(ChannelSendResult.Failed(channelId, connection.ErrorMessage ?? "Channel is in a failed state."));

        if (connection.State is ChannelConnectionState.Pending or ChannelConnectionState.Connecting)
            _connectionManager.UpdateState(channelId, ChannelConnectionState.Connected);

        _messageRuntime.SendMessage(
            sender,
            channelTarget,
            body,
            kind,
            subject,
            protocol: protocol);

        return Task.FromResult(ChannelSendResult.Sent(channelId));
    }

    public Task<ChannelConnectionSnapshot?> ConnectAsync(
        string channelTarget,
        CancellationToken cancellationToken = default)
    {
        var channelId = NormalizeChannelId(channelTarget);
        var connection = _connectionManager.Register(channelId, ChannelKind.Uds, channelTarget);
        connection.UpdateState(ChannelConnectionState.Connected);

        return Task.FromResult<ChannelConnectionSnapshot?>(new ChannelConnectionSnapshot(
            connection.ChannelId,
            connection.Kind,
            connection.Target,
            connection.State,
            connection.ErrorMessage,
            connection.LastUpdatedAt));
    }

    public Task DisconnectAsync(string channelId, CancellationToken cancellationToken = default)
    {
        if (_connectionManager.TryGet(channelId, out _))
            _connectionManager.UpdateState(channelId, ChannelConnectionState.Disconnected);

        return Task.CompletedTask;
    }

    internal static string NormalizeChannelId(string target) =>
        target.StartsWith("uds:", StringComparison.OrdinalIgnoreCase)
            ? target[4..].Trim()
            : target.Trim();
}

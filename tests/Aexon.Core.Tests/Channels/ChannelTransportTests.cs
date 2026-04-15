using Aexon.Core.Agents;
using Aexon.Core.Channels;

namespace Aexon.Core.Tests.Channels;

public sealed class ChannelTransportTests
{
    [Fact]
    public async Task BridgeTransport_SendAsync_DeliversMessageAndConnects()
    {
        var connectionManager = new ChannelConnectionManager();
        var messages = new InMemoryAgentMessageRuntime();
        var transport = new BridgeChannelTransport(connectionManager, messages);

        var result = await transport.SendAsync("bridge:remote-1", "main", "Hello bridge");

        Assert.True(result.Success);
        Assert.Equal("remote-1", result.ChannelId);

        Assert.True(connectionManager.TryGet("remote-1", out var connection));
        Assert.Equal(ChannelConnectionState.Connected, connection!.State);

        var delivered = messages.ListMessages();
        Assert.Single(delivered);
        Assert.Equal("main", delivered[0].From);
        Assert.Equal("bridge:remote-1", delivered[0].To);
        Assert.Equal("Hello bridge", delivered[0].Body);
    }

    [Fact]
    public async Task BridgeTransport_SendAsync_ReturnsFailureWhenChannelFailed()
    {
        var connectionManager = new ChannelConnectionManager();
        var messages = new InMemoryAgentMessageRuntime();
        var transport = new BridgeChannelTransport(connectionManager, messages);

        connectionManager.Register("remote-1", ChannelKind.Bridge, "bridge:remote-1");
        connectionManager.UpdateState("remote-1", ChannelConnectionState.Failed, "Connection refused");

        var result = await transport.SendAsync("bridge:remote-1", "main", "Hello");

        Assert.False(result.Success);
        Assert.Contains("Connection refused", result.ErrorMessage);
        Assert.Empty(messages.ListMessages());
    }

    [Fact]
    public async Task BridgeTransport_ConnectAsync_ReturnsConnectedSnapshot()
    {
        var connectionManager = new ChannelConnectionManager();
        var messages = new InMemoryAgentMessageRuntime();
        var transport = new BridgeChannelTransport(connectionManager, messages);

        var snapshot = await transport.ConnectAsync("bridge:remote-1");

        Assert.NotNull(snapshot);
        Assert.Equal("remote-1", snapshot!.ChannelId);
        Assert.Equal(ChannelKind.Bridge, snapshot.Kind);
        Assert.Equal(ChannelConnectionState.Connected, snapshot.State);
    }

    [Fact]
    public async Task BridgeTransport_DisconnectAsync_SetsDisconnectedState()
    {
        var connectionManager = new ChannelConnectionManager();
        var messages = new InMemoryAgentMessageRuntime();
        var transport = new BridgeChannelTransport(connectionManager, messages);
        await transport.ConnectAsync("bridge:remote-1");

        await transport.DisconnectAsync("remote-1");

        Assert.True(connectionManager.TryGet("remote-1", out var connection));
        Assert.Equal(ChannelConnectionState.Disconnected, connection!.State);
    }

    [Fact]
    public async Task UdsTransport_SendAsync_DeliversMessageAndConnects()
    {
        var connectionManager = new ChannelConnectionManager();
        var messages = new InMemoryAgentMessageRuntime();
        var transport = new UdsChannelTransport(connectionManager, messages);

        var result = await transport.SendAsync("uds:/tmp/agent.sock", "worker", "Hello uds");

        Assert.True(result.Success);
        Assert.Equal("/tmp/agent.sock", result.ChannelId);

        Assert.True(connectionManager.TryGet("/tmp/agent.sock", out var connection));
        Assert.Equal(ChannelConnectionState.Connected, connection!.State);
        Assert.Equal(ChannelKind.Uds, connection.Kind);

        var delivered = messages.ListMessages();
        Assert.Single(delivered);
        Assert.Equal("worker", delivered[0].From);
        Assert.Equal("uds:/tmp/agent.sock", delivered[0].To);
    }

    [Fact]
    public async Task UdsTransport_SendAsync_PreservesKindAndProtocol()
    {
        var connectionManager = new ChannelConnectionManager();
        var messages = new InMemoryAgentMessageRuntime();
        var transport = new UdsChannelTransport(connectionManager, messages);

        var protocol = new AgentMessageProtocol { ActionName = "check-status", RequiresResponse = true };
        var result = await transport.SendAsync(
            "uds:/tmp/test.sock",
            "main",
            "Status check",
            AgentMessageKind.SystemBridgeStatus,
            subject: "status",
            protocol: protocol);

        Assert.True(result.Success);

        var delivered = messages.ListMessages();
        Assert.Single(delivered);
        Assert.Equal(AgentMessageKind.SystemBridgeStatus, delivered[0].Kind);
        Assert.Equal("status", delivered[0].Subject);
        Assert.Equal("check-status", delivered[0].Protocol?.ActionName);
        Assert.True(delivered[0].Protocol?.RequiresResponse);
    }

    [Fact]
    public async Task UdsTransport_ConnectAsync_ReturnsConnectedSnapshot()
    {
        var connectionManager = new ChannelConnectionManager();
        var messages = new InMemoryAgentMessageRuntime();
        var transport = new UdsChannelTransport(connectionManager, messages);

        var snapshot = await transport.ConnectAsync("uds:/tmp/test.sock");

        Assert.NotNull(snapshot);
        Assert.Equal("/tmp/test.sock", snapshot!.ChannelId);
        Assert.Equal(ChannelKind.Uds, snapshot.Kind);
        Assert.Equal(ChannelConnectionState.Connected, snapshot.State);
    }
}

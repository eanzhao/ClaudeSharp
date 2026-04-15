using System.Text.Json;
using Aexon.Core.Agents;
using Aexon.Core.Channels;
using Aexon.Core.Permissions;
using Aexon.Core.Tools;
using Aexon.Tools;

namespace Aexon.Core.Tests.Channels;

public sealed class MailboxBridgeRoutingTests
{
    [Fact]
    public async Task SendMessageTool_RoutesBridgeTargetThroughChannelTransport()
    {
        var messages = new InMemoryAgentMessageRuntime();
        var connectionManager = new ChannelConnectionManager();
        var bridge = new BridgeChannelTransport(connectionManager, messages);
        var uds = new UdsChannelTransport(connectionManager, messages);
        var router = new ChannelRouter(bridge, uds);

        var tool = new SendMessageTool(messages, channelRouter: router);

        var result = await tool.ExecuteAsync(
            Json(new
            {
                request = new
                {
                    to = "bridge:remote-agent",
                    from = "main",
                    message = "Hello via bridge",
                },
            }),
            CreateContext());

        Assert.False(result.IsError);
        Assert.Contains("Delivered 1 message(s) via bridge channel", result.Data);
        Assert.Contains("remote-agent", result.Data);

        var delivered = messages.ListMessages();
        Assert.Single(delivered);
        Assert.Equal("bridge:remote-agent", delivered[0].To);
    }

    [Fact]
    public async Task SendMessageTool_RoutesUdsTargetThroughChannelTransport()
    {
        var messages = new InMemoryAgentMessageRuntime();
        var connectionManager = new ChannelConnectionManager();
        var bridge = new BridgeChannelTransport(connectionManager, messages);
        var uds = new UdsChannelTransport(connectionManager, messages);
        var router = new ChannelRouter(bridge, uds);

        var tool = new SendMessageTool(messages, channelRouter: router);

        var result = await tool.ExecuteAsync(
            Json(new
            {
                request = new
                {
                    to = "uds:/tmp/agent.sock",
                    from = "worker",
                    message = "Hello via UDS",
                },
            }),
            CreateContext());

        Assert.False(result.IsError);
        Assert.Contains("Delivered 1 message(s) via uds channel", result.Data);
    }

    [Fact]
    public async Task SendMessageTool_ReturnsErrorWhenChannelRouterNotConfigured()
    {
        var messages = new InMemoryAgentMessageRuntime();
        var tool = new SendMessageTool(messages);

        var result = await tool.ExecuteAsync(
            Json(new
            {
                request = new
                {
                    to = "bridge:remote",
                    from = "main",
                    message = "Should fail",
                },
            }),
            CreateContext());

        Assert.True(result.IsError);
        Assert.Contains("Channel routing is not configured", result.Data);
    }

    [Fact]
    public async Task SendMessageTool_ReturnsErrorWhenChannelInFailedState()
    {
        var messages = new InMemoryAgentMessageRuntime();
        var connectionManager = new ChannelConnectionManager();
        connectionManager.Register("remote", ChannelKind.Bridge, "bridge:remote");
        connectionManager.UpdateState("remote", ChannelConnectionState.Failed, "Connection refused");

        var bridge = new BridgeChannelTransport(connectionManager, messages);
        var uds = new UdsChannelTransport(connectionManager, messages);
        var router = new ChannelRouter(bridge, uds);

        var tool = new SendMessageTool(messages, channelRouter: router);

        var result = await tool.ExecuteAsync(
            Json(new
            {
                request = new
                {
                    to = "bridge:remote",
                    from = "main",
                    message = "Should fail",
                },
            }),
            CreateContext());

        Assert.True(result.IsError);
        Assert.Contains("Connection refused", result.Data);
    }

    [Fact]
    public async Task SendMessageTool_LocalTargetStillWorksWithChannelRouter()
    {
        var messages = new InMemoryAgentMessageRuntime();
        var connectionManager = new ChannelConnectionManager();
        var bridge = new BridgeChannelTransport(connectionManager, messages);
        var uds = new UdsChannelTransport(connectionManager, messages);
        var router = new ChannelRouter(bridge, uds);

        var tool = new SendMessageTool(messages, channelRouter: router);

        var result = await tool.ExecuteAsync(
            Json(new
            {
                request = new
                {
                    to = "subagent",
                    from = "main",
                    message = "Local message",
                },
            }),
            CreateContext());

        Assert.False(result.IsError);
        Assert.Contains("Delivered 1 message(s).", result.Data);

        var delivered = messages.ListMessages();
        Assert.Single(delivered);
        Assert.Equal("subagent", delivered[0].To);
    }

    private static JsonElement Json(object value) =>
        JsonSerializer.SerializeToElement(value);

    private static ToolExecutionContext CreateContext() =>
        new()
        {
            WorkingDirectory = "/tmp",
            PermissionContext = new PermissionContext(),
            Tools = [],
            Messages = [],
            CancellationToken = CancellationToken.None,
        };
}

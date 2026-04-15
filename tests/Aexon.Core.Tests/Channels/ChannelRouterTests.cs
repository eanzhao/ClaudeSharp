using Aexon.Core.Agents;
using Aexon.Core.Channels;

namespace Aexon.Core.Tests.Channels;

public sealed class ChannelRouterTests
{
    [Theory]
    [InlineData("bridge:remote-1", true)]
    [InlineData("Bridge:Remote-1", true)]
    [InlineData("uds:/tmp/test.sock", true)]
    [InlineData("UDS:/tmp/test.sock", true)]
    [InlineData("main", false)]
    [InlineData("Platform/Ada", false)]
    public void IsChannelTarget_IdentifiesPrefixCorrectly(string target, bool expected)
    {
        Assert.Equal(expected, ChannelRouter.IsChannelTarget(target));
    }

    [Theory]
    [InlineData("bridge:ch1", ChannelKind.Bridge)]
    [InlineData("uds:/tmp/s.sock", ChannelKind.Uds)]
    public void ParseChannelKind_ReturnsCorrectKind(string target, ChannelKind expected)
    {
        Assert.Equal(expected, ChannelRouter.ParseChannelKind(target));
    }

    [Fact]
    public void ParseChannelKind_ReturnsNullForLocalTarget()
    {
        Assert.Null(ChannelRouter.ParseChannelKind("main"));
    }

    [Fact]
    public void ResolveTransport_ReturnsBridgeTransportForBridgeTarget()
    {
        var connectionManager = new ChannelConnectionManager();
        var messages = new InMemoryAgentMessageRuntime();
        var bridge = new BridgeChannelTransport(connectionManager, messages);
        var uds = new UdsChannelTransport(connectionManager, messages);
        var router = new ChannelRouter(bridge, uds);

        var transport = router.ResolveTransport("bridge:remote");

        Assert.NotNull(transport);
        Assert.Equal(ChannelKind.Bridge, transport!.Kind);
    }

    [Fact]
    public void ResolveTransport_ReturnsUdsTransportForUdsTarget()
    {
        var connectionManager = new ChannelConnectionManager();
        var messages = new InMemoryAgentMessageRuntime();
        var bridge = new BridgeChannelTransport(connectionManager, messages);
        var uds = new UdsChannelTransport(connectionManager, messages);
        var router = new ChannelRouter(bridge, uds);

        var transport = router.ResolveTransport("uds:/tmp/test.sock");

        Assert.NotNull(transport);
        Assert.Equal(ChannelKind.Uds, transport!.Kind);
    }

    [Fact]
    public void ResolveTransport_ReturnsNullForLocalTarget()
    {
        var connectionManager = new ChannelConnectionManager();
        var messages = new InMemoryAgentMessageRuntime();
        var bridge = new BridgeChannelTransport(connectionManager, messages);
        var uds = new UdsChannelTransport(connectionManager, messages);
        var router = new ChannelRouter(bridge, uds);

        Assert.Null(router.ResolveTransport("main"));
    }
}

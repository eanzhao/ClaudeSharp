using Aexon.Core.Channels;

namespace Aexon.Core.Tests.Channels;

public sealed class ChannelConnectionManagerTests
{
    [Fact]
    public void Register_CreatesConnectionInPendingState()
    {
        var manager = new ChannelConnectionManager();

        var connection = manager.Register("test-channel", ChannelKind.Bridge, "bridge:test-channel");

        Assert.Equal("test-channel", connection.ChannelId);
        Assert.Equal(ChannelKind.Bridge, connection.Kind);
        Assert.Equal("bridge:test-channel", connection.Target);
        Assert.Equal(ChannelConnectionState.Pending, connection.State);
        Assert.Null(connection.ErrorMessage);
    }

    [Fact]
    public void TryGet_ReturnsTrueForRegisteredConnection()
    {
        var manager = new ChannelConnectionManager();
        manager.Register("ch1", ChannelKind.Uds, "uds:/tmp/test.sock");

        Assert.True(manager.TryGet("ch1", out var connection));
        Assert.NotNull(connection);
        Assert.Equal(ChannelKind.Uds, connection!.Kind);
    }

    [Fact]
    public void TryGet_ReturnsFalseForUnknownChannel()
    {
        var manager = new ChannelConnectionManager();

        Assert.False(manager.TryGet("unknown", out _));
    }

    [Fact]
    public void UpdateState_TransitionsAndTracksError()
    {
        var manager = new ChannelConnectionManager();
        manager.Register("ch1", ChannelKind.Bridge, "bridge:ch1");

        Assert.True(manager.UpdateState("ch1", ChannelConnectionState.Connected));
        Assert.True(manager.TryGet("ch1", out var connection));
        Assert.Equal(ChannelConnectionState.Connected, connection!.State);
        Assert.Null(connection.ErrorMessage);

        Assert.True(manager.UpdateState("ch1", ChannelConnectionState.Failed, "Connection refused"));
        Assert.True(manager.TryGet("ch1", out connection));
        Assert.Equal(ChannelConnectionState.Failed, connection!.State);
        Assert.Equal("Connection refused", connection.ErrorMessage);
    }

    [Fact]
    public void UpdateState_ReturnsFalseForUnknownChannel()
    {
        var manager = new ChannelConnectionManager();

        Assert.False(manager.UpdateState("unknown", ChannelConnectionState.Connected));
    }

    [Fact]
    public void Remove_RemovesRegisteredConnection()
    {
        var manager = new ChannelConnectionManager();
        manager.Register("ch1", ChannelKind.Bridge, "bridge:ch1");

        Assert.True(manager.Remove("ch1"));
        Assert.False(manager.TryGet("ch1", out _));
    }

    [Fact]
    public void Snapshot_ReturnsOrderedConnectionSnapshots()
    {
        var manager = new ChannelConnectionManager();
        manager.Register("beta", ChannelKind.Uds, "uds:/tmp/beta.sock");
        manager.Register("alpha", ChannelKind.Bridge, "bridge:alpha");
        manager.UpdateState("alpha", ChannelConnectionState.Connected);

        var snapshots = manager.Snapshot();

        Assert.Equal(2, snapshots.Count);
        Assert.Equal("alpha", snapshots[0].ChannelId);
        Assert.Equal(ChannelConnectionState.Connected, snapshots[0].State);
        Assert.Equal("beta", snapshots[1].ChannelId);
        Assert.Equal(ChannelConnectionState.Pending, snapshots[1].State);
    }

    [Fact]
    public void UpdateState_ClearsErrorMessageOnNonFailedState()
    {
        var manager = new ChannelConnectionManager();
        manager.Register("ch1", ChannelKind.Bridge, "bridge:ch1");
        manager.UpdateState("ch1", ChannelConnectionState.Failed, "Timeout");

        manager.UpdateState("ch1", ChannelConnectionState.Connected);

        Assert.True(manager.TryGet("ch1", out var connection));
        Assert.Equal(ChannelConnectionState.Connected, connection!.State);
        Assert.Null(connection.ErrorMessage);
    }
}

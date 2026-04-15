using System.Text.Json;
using Aexon.Core.Mcp;

namespace Aexon.Core.Tests.Mcp;

/// <summary>
/// Contains tests for mcp Connection Manager.
/// </summary>
public sealed class McpConnectionManagerTests
{
    [Fact]
    public void RegisterUpdateAndRemoveConnectionsWorkAsExpected()
    {
        var manager = new McpConnectionManager();
        var connection = new McpConnection(
            "server-a",
            new Uri("http://localhost:3000"),
            McpConnectionState.Pending,
            [BuildTool("alpha")]);

        manager.Register(connection);

        var snapshot = Assert.Single(manager.Snapshot());
        Assert.Equal("server-a", snapshot.ServerId);
        Assert.Equal(McpConnectionState.Pending, snapshot.State);
        Assert.Equal(1, snapshot.ToolCount);
        Assert.Equal(new Uri("http://localhost:3000"), snapshot.Endpoint);

        Assert.True(manager.UpdateState("server-a", McpConnectionState.Connected));
        Assert.True(manager.UpdateTools("server-a", [BuildTool("beta"), BuildTool("gamma")]));

        Assert.True(manager.TryGet("server-a", out var fetched));
        Assert.NotNull(fetched);
        Assert.Equal(McpConnectionState.Connected, fetched!.State);
        Assert.Equal(2, fetched.Tools.Count);

        snapshot = Assert.Single(manager.Snapshot());
        Assert.Equal(McpConnectionState.Connected, snapshot.State);
        Assert.Equal(2, snapshot.ToolCount);
        Assert.Equal(new Uri("http://localhost:3000"), snapshot.Endpoint);

        Assert.False(manager.UpdateState("missing", McpConnectionState.Connected));
        Assert.False(manager.UpdateTools("missing", [BuildTool("delta")]));

        Assert.True(manager.Remove("server-a"));
        Assert.Empty(manager.Snapshot());
    }

    private static McpToolDescriptor BuildTool(string name) =>
        new()
        {
            Name = name,
            Description = $"{name} tool",
            InputSchema = JsonSerializer.SerializeToElement(new { type = "object" }),
        };
}

using System.Text.Json;
using Aexon.Core.Mcp;

namespace Aexon.Core.Tests.Mcp;

/// <summary>
/// Contains tests for the MCP JSON-RPC client.
/// </summary>
public sealed class McpJsonRpcClientTests
{
    [Fact]
    public async Task SendRequestAsync_IgnoresNotificationsAndReturnsMatchingNumericResponse()
    {
        await using var transport = new FakeLineTransport(
        [
            """{"jsonrpc":"2.0","method":"notifications/message","params":{"text":"hello"}}""",
            """{"jsonrpc":"2.0","id":1}""",
        ]);
        await using var client = new McpJsonRpcClient(transport);

        var result = await client.SendRequestAsync("tools/list");

        Assert.Equal(JsonValueKind.Object, result.ValueKind);
        Assert.Empty(result.EnumerateObject());
        Assert.Single(transport.Writes);
        Assert.Contains(@"""method"":""tools/list""", transport.Writes[0], StringComparison.Ordinal);
        Assert.Contains(@"""id"":""1""", transport.Writes[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendRequestAsync_RejectsServerInitiatedRequestsBeforeContinuing()
    {
        await using var transport = new FakeLineTransport(
        [
            """{"jsonrpc":"2.0","id":"server-1","method":"ping","params":{}}""",
            """{"jsonrpc":"2.0","id":"1","result":{"status":"done"}}""",
        ]);
        await using var client = new McpJsonRpcClient(transport);

        var result = await client.SendRequestAsync("initialize", new { protocolVersion = "test" });

        Assert.Equal("done", result.GetProperty("status").GetString());
        Assert.Equal(2, transport.Writes.Count);
        Assert.Contains(@"""method"":""initialize""", transport.Writes[0], StringComparison.Ordinal);
        Assert.Contains(@"""id"":""server-1""", transport.Writes[1], StringComparison.Ordinal);
        Assert.Contains(@"""code"":-32601", transport.Writes[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendRequestAsync_ThrowsProtocolExceptionForErrorResponses()
    {
        await using var transport = new FakeLineTransport(
        [
            """{"jsonrpc":"2.0","id":"1","error":{"code":-32602,"message":"bad request","data":{"field":"name"}}}""",
        ]);
        await using var client = new McpJsonRpcClient(transport);

        var ex = await Assert.ThrowsAsync<McpProtocolException>(() =>
            client.SendRequestAsync("tools/call"));

        Assert.Equal("tools/call", ex.Method);
        Assert.Equal(-32602, ex.Code);
        Assert.Contains("bad request", ex.Message, StringComparison.Ordinal);
        Assert.NotNull(ex.Data);
        Assert.Equal("name", ex.Data.Value.GetProperty("field").GetString());
    }

    [Fact]
    public async Task SendRequestAsync_ThrowsWhenTransportReturnsInvalidJson()
    {
        await using var transport = new FakeLineTransport(
        [
            "not-json",
        ]);
        await using var client = new McpJsonRpcClient(transport);

        await Assert.ThrowsAsync<IOException>(() => client.SendRequestAsync("tools/list"));
    }

    [Fact]
    public async Task SendNotificationAsync_WritesNotificationWithoutRequestId()
    {
        await using var transport = new FakeLineTransport([]);
        await using var client = new McpJsonRpcClient(transport);

        await client.SendNotificationAsync("notifications/initialized", new { serverId = "server-a" });

        var payload = Assert.Single(transport.Writes);
        Assert.Contains(@"""method"":""notifications/initialized""", payload, StringComparison.Ordinal);
        Assert.Contains(@"""params"":{""serverId"":""server-a""}", payload, StringComparison.Ordinal);
        Assert.DoesNotContain(@"""id""", payload, StringComparison.Ordinal);
    }

    private sealed class FakeLineTransport : IMcpLineTransport
    {
        private readonly Queue<string?> _reads;

        public FakeLineTransport(IEnumerable<string?> reads)
        {
            _reads = new Queue<string?>(reads);
        }

        public List<string> Writes { get; } = [];

        public Task WriteLineAsync(string line, CancellationToken cancellationToken = default)
        {
            Writes.Add(line);
            return Task.CompletedTask;
        }

        public Task<string?> ReadLineAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_reads.Count > 0 ? _reads.Dequeue() : null);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

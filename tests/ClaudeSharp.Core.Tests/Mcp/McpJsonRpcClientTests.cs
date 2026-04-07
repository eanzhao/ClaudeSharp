using System.Text.Json;
using ClaudeSharp.Core.Mcp;

namespace ClaudeSharp.Core.Tests.Mcp;

/// <summary>
/// Contains tests for the MCP JSON-RPC client.
/// </summary>
public sealed class McpJsonRpcClientTests
{
    [Fact]
    public async Task SendRequestAsync_IgnoresNotificationsAndReturnsMatchingResponse()
    {
        await using var transport = new FakeLineTransport(
        [
            """{"jsonrpc":"2.0","method":"notifications/message","params":{"text":"hello"}}""",
            """{"jsonrpc":"2.0","id":"1","result":{"ok":true}}""",
        ]);
        await using var client = new McpJsonRpcClient(transport);

        var result = await client.SendRequestAsync("tools/list");

        Assert.True(result.GetProperty("ok").GetBoolean());
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

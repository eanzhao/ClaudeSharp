using System.Collections.Concurrent;
using Aexon.Core.Mcp;
using Aexon.Core.Tests.Runtime;

namespace Aexon.Core.Tests.Mcp;

/// <summary>
/// Contains tests for the MCP stdio transport.
/// </summary>
public sealed class McpStdioTransportTests
{
    [Fact]
    public async Task StartAsync_WritesReadsAndPumpsStderr()
    {
        if (OperatingSystem.IsWindows()) return;
        using var temp = new TempDirectory("mcp-stdio");
        var script = McpTestScripts.WriteShellScript(
            temp,
            "server.sh",
            """
            #!/bin/sh
            printf '%s\n' 'booted' >&2

            while IFS= read -r line; do
              case "$line" in
                *'"method":"ping"'*)
                  printf '%s\n' 'ping-received' >&2
                  printf '%s\n' '{"jsonrpc":"2.0","id":1,"result":{"pong":true}}'
                  ;;
              esac
            done
            """);

        var stderrLines = new ConcurrentQueue<string>();
        var config = new McpServerConfig
        {
            ServerId = "server-a",
            Command = "/bin/sh",
            Args = [script],
            WorkingDirectory = temp.Root,
        };

        await using var transport = await McpStdioTransport.StartAsync(
            config,
            temp.Root,
            stderrLines.Enqueue);

        await transport.WriteLineAsync("""{"jsonrpc":"2.0","id":1,"method":"ping"}""");
        var response = await transport.ReadLineAsync();

        Assert.Equal("""{"jsonrpc":"2.0","id":1,"result":{"pong":true}}""", response);
        await WaitForConditionAsync(() => stderrLines.Count >= 2);
        Assert.Contains("booted", stderrLines.ToArray());
        Assert.Contains("ping-received", stderrLines.ToArray());
    }

    [Fact]
    public async Task StartAsync_ReturnsExitableProcessThatDisposesCleanly()
    {
        if (OperatingSystem.IsWindows()) return;
        using var temp = new TempDirectory("mcp-stdio-exit");
        var script = McpTestScripts.WriteShellScript(
            temp,
            "server.sh",
            """
            #!/bin/sh
            while IFS= read -r line; do
              case "$line" in
                *'"method":"ping"'*)
                  printf '%s\n' '{"jsonrpc":"2.0","id":1,"result":{"ok":true}}'
                  exit 0
                  ;;
              esac
            done
            """);

        var config = new McpServerConfig
        {
            ServerId = "server-a",
            Command = "/bin/sh",
            Args = [script],
            WorkingDirectory = temp.Root,
        };

        await using var transport = await McpStdioTransport.StartAsync(
            config,
            temp.Root);

        await transport.WriteLineAsync("""{"jsonrpc":"2.0","id":1,"method":"ping"}""");
        var response = await transport.ReadLineAsync();

        Assert.Equal("""{"jsonrpc":"2.0","id":1,"result":{"ok":true}}""", response);
        await Task.Delay(50);
    }

    [Fact]
    public async Task StartAsync_ThrowsWhenCancellationIsAlreadyRequested()
    {
        using var temp = new TempDirectory("mcp-stdio-cancel");
        var config = new McpServerConfig
        {
            ServerId = "server-a",
            Command = "/bin/sh",
            Args = ["-c", "exit 0"],
            WorkingDirectory = temp.Root,
        };

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            McpStdioTransport.StartAsync(
                config,
                temp.Root,
                cancellationToken: cts.Token));
    }

    private static async Task WaitForConditionAsync(
        Func<bool> condition,
        int timeoutMilliseconds = 2000)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(timeoutMilliseconds);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;

            await Task.Delay(20);
        }

        Assert.True(condition());
    }
}

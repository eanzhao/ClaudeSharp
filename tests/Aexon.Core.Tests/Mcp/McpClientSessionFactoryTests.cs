using System.Collections.Concurrent;
using Aexon.Core.Mcp;
using Aexon.Core.Tests.Runtime;

namespace Aexon.Core.Tests.Mcp;

/// <summary>
/// Contains tests for the MCP client session factory.
/// </summary>
public sealed class McpClientSessionFactoryTests
{
    [Fact]
    public async Task ConnectAsync_ForwardsStderrSinkAndReturnsConnectedSession()
    {
        if (OperatingSystem.IsWindows()) return;
        using var temp = new TempDirectory("mcp-factory");
        var script = McpTestScripts.WriteShellScript(
            temp,
            "server.sh",
            """
            #!/bin/sh
            printf '%s\n' 'factory booted' >&2

            while IFS= read -r line; do
              case "$line" in
                *'"method":"initialize"'*)
                  printf '%s\n' '{"jsonrpc":"2.0","id":1,"result":{"capabilities":{}}}'
                  ;;
                *'"method":"tools/list"'*)
                  printf '%s\n' '{"jsonrpc":"2.0","id":2,"result":{"tools":[]}}'
                  ;;
              esac
            done
            """);

        var stderrLines = new ConcurrentQueue<string>();
        var factory = new McpClientSessionFactory(stderrLines.Enqueue);
        var config = new McpServerConfig
        {
            ServerId = "server-a",
            Command = "/bin/sh",
            Args = [script],
            WorkingDirectory = temp.Root,
        };

        await using var session = await factory.ConnectAsync(config, temp.Root);
        var tools = await session.ListToolsAsync();

        await WaitForConditionAsync(() => stderrLines.Count >= 1);
        Assert.Equal("server-a", session.ServerId);
        Assert.Empty(tools);
        Assert.Contains("factory booted", stderrLines.ToArray());
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

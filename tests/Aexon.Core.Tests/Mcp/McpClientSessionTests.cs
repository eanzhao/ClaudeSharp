using System.Collections.Concurrent;
using System.Text.Json;
using Aexon.Core.Mcp;
using Aexon.Core.Tests.Runtime;

namespace Aexon.Core.Tests.Mcp;

/// <summary>
/// Contains tests for the MCP client session.
/// </summary>
public sealed class McpClientSessionTests
{
    [Fact]
    public async Task ConnectAsync_InitializesListsToolsAndCallsToolsThroughTheStdioTransport()
    {
        if (OperatingSystem.IsWindows()) return;
        using var temp = new TempDirectory("mcp-session");
        var script = McpTestScripts.WriteShellScript(
            temp,
            "server.sh",
            """
            #!/bin/sh
            list_count=0
            call_count=0
            printf '%s\n' 'server booted' >&2

            while IFS= read -r line; do
              case "$line" in
                *'"method":"initialize"'*)
                  printf '%s\n' '{"jsonrpc":"2.0","id":1,"result":{"capabilities":{}}}'
                  ;;
                *'"method":"notifications/initialized"'*)
                  printf '%s\n' 'initialized' >&2
                  ;;
                *'"method":"tools/list"'*)
                  if [ "$list_count" -eq 0 ]; then
                    list_count=1
                    printf '%s\n' '{"jsonrpc":"2.0","id":2,"result":{"tools":[{"name":"alpha","description":"Alpha tool","inputSchema":{"type":"object"},"annotations":{"readOnlyHint":true,"title":"Alpha Title"}}],"nextCursor":"more"}}'
                  else
                    printf '%s\n' '{"jsonrpc":"2.0","id":3,"result":{"tools":[{"name":"beta","description":"Beta tool","input_schema":{"type":"object"},"title":"Beta Title","annotations":{"openWorldHint":true}}]}}'
                  fi
                  ;;
                *'"method":"tools/call"'*)
                  if [ "$call_count" -eq 0 ]; then
                    call_count=1
                    printf '%s\n' '{"jsonrpc":"2.0","id":4,"result":{"content":[{"type":"text","text":"tool ok"},{"type":"image","mimeType":"image/png"},{"type":"audio","mimeType":"audio/wav"},{"type":"unknown","text":"fallback"}],"structuredContent":{"answer":42},"isError":false}}'
                  else
                    printf '%s\n' '{"jsonrpc":"2.0","id":5,"result":{"foo":"bar","isError":true}}'
                  fi
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

        await using var session = await McpClientSession.ConnectAsync(
            config,
            temp.Root,
            stderrLines.Enqueue);

        await WaitForConditionAsync(() => stderrLines.Count >= 2);
        Assert.Contains("server booted", stderrLines.ToArray());
        Assert.Contains("initialized", stderrLines.ToArray());
        Assert.Equal("server-a", session.ServerId);

        var tools = await session.ListToolsAsync();
        Assert.Equal(2, tools.Count);

        var alpha = Assert.Single(tools, tool => tool.Name == "alpha");
        Assert.Equal("Alpha tool", alpha.Description);
        Assert.True(alpha.ReadOnlyHint);
        Assert.Equal("Alpha Title", alpha.SearchHint);
        Assert.Equal("object", alpha.InputSchema.GetProperty("type").GetString());

        var beta = Assert.Single(tools, tool => tool.Name == "beta");
        Assert.Equal("Beta tool", beta.Description);
        Assert.True(beta.OpenWorldHint);
        Assert.Equal("Beta Title", beta.SearchHint);

        var complexResult = await session.CallToolAsync(
            "alpha",
            JsonSerializer.SerializeToElement(new { query = "hello" }));

        Assert.False(complexResult.IsError);
        Assert.Contains("tool ok", complexResult.Text, StringComparison.Ordinal);
        Assert.Contains("image/png", complexResult.Text, StringComparison.Ordinal);
        Assert.Contains("audio/wav", complexResult.Text, StringComparison.Ordinal);
        Assert.Contains("Structured content:", complexResult.Text, StringComparison.Ordinal);
        Assert.Contains(@"""answer"":42", complexResult.Text, StringComparison.Ordinal);
        Assert.Contains("fallback", complexResult.Text, StringComparison.Ordinal);

        var rawResult = await session.CallToolAsync(
            "beta",
            JsonSerializer.SerializeToElement(new { query = "raw" }));

        Assert.True(rawResult.IsError);
        Assert.Contains(@"""foo"":""bar""", rawResult.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConnectAsync_ThrowsWhenInitializeReturnsProtocolError()
    {
        if (OperatingSystem.IsWindows()) return;
        using var temp = new TempDirectory("mcp-session-fail");
        var script = McpTestScripts.WriteShellScript(
            temp,
            "server.sh",
            """
            #!/bin/sh
            while IFS= read -r line; do
              case "$line" in
                *'"method":"initialize"'*)
                  printf '%s\n' '{"jsonrpc":"2.0","id":1,"error":{"code":-32000,"message":"bad init","data":{"reason":"simulated"}}}'
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

        var ex = await Assert.ThrowsAsync<McpProtocolException>(() =>
            McpClientSession.ConnectAsync(config, temp.Root));

        Assert.Equal("initialize", ex.Method);
        Assert.Equal(-32000, ex.Code);
        Assert.Contains("bad init", ex.Message, StringComparison.Ordinal);
        Assert.NotNull(ex.Data);
        Assert.Equal("simulated", ex.Data.Value.GetProperty("reason").GetString());
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

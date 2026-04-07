using ClaudeSharp.Core.Mcp;
using ClaudeSharp.Core.Tests.Runtime;

namespace ClaudeSharp.Core.Tests.Mcp;

/// <summary>
/// Contains tests for MCP settings loading.
/// </summary>
public sealed class McpSettingsLoaderTests
{
    [Fact]
    public void LoadFromFiles_MergesConfigsAndResolvesRelativePaths()
    {
        using var temp = new TempDirectory();
        var globalConfig = temp.WriteFile("global/settings.json", """
{
  "mcpServers": {
    "server-a": {
      "command": "node",
      "args": ["global.js"]
    },
    "server-sse": {
      "url": "http://localhost:3000/sse"
    }
  }
}
""");

        var projectConfig = temp.WriteFile("project/.claude/settings.json", """
{
  "mcpServers": {
    "server-a": {
      "command": "uvx",
      "args": ["server-a"],
      "cwd": "./servers/a",
      "env": {
        "DEBUG": "1"
      }
    },
    "server-b": {
      "command": "python",
      "disabled": true
    }
  }
}
""");

        var result = McpSettingsLoader.LoadFromFiles(
            [globalConfig, projectConfig],
            temp.FullPath("project"));

        Assert.Equal(2, result.Servers.Count);
        Assert.Equal(
            [Path.GetFullPath(globalConfig), Path.GetFullPath(projectConfig)],
            result.SourcePaths);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Contains("uses url transport", StringComparison.Ordinal));

        var serverA = Assert.Single(result.Servers, server => server.ServerId == "server-a");
        Assert.Equal("uvx", serverA.Command);
        Assert.Equal(["server-a"], serverA.Args);
        Assert.Equal(
            Path.GetFullPath(temp.FullPath("project", ".claude", "servers", "a")),
            serverA.WorkingDirectory);
        Assert.Equal("1", serverA.Environment["DEBUG"]);

        var serverB = Assert.Single(result.Servers, server => server.ServerId == "server-b");
        Assert.True(serverB.Disabled);
        Assert.Equal("python", serverB.Command);
    }
}

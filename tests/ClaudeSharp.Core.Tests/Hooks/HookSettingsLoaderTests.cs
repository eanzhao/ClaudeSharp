using ClaudeSharp.Core.Hooks;
using ClaudeSharp.Core.Tests.Runtime;

namespace ClaudeSharp.Core.Tests.Hooks;

/// <summary>
/// Contains tests for hook settings loading.
/// </summary>
public sealed class HookSettingsLoaderTests
{
    [Fact]
    public void LoadFromFiles_AppendsCommandsAcrossFilesAndResolvesRelativePaths()
    {
        using var temp = new TempDirectory();
        var globalConfig = temp.WriteFile("global/settings.json", """
{
  "hooks": {
    "pre_tool_use": [
      "echo global"
    ],
    "unknown_event": [
      "echo ignored"
    ]
  }
}
""");

        var projectConfig = temp.WriteFile("project/.claude/settings.json", """
{
  "hooks": {
    "PermissionRequest": [
      {
        "command": "echo approve",
        "cwd": "./scripts",
        "env": {
          "DEBUG": "1"
        },
        "timeout": 1200,
        "failOpen": false
      }
    ]
  }
}
""");

        var result = HookSettingsLoader.LoadFromFiles(
            [globalConfig, projectConfig],
            temp.FullPath("project"));

        Assert.Equal(2, result.Commands.Count);
        Assert.Equal(
            [Path.GetFullPath(globalConfig), Path.GetFullPath(projectConfig)],
            result.SourcePaths);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Contains("unknown hook event", StringComparison.OrdinalIgnoreCase));

        var preToolUse = Assert.Single(
            result.Commands,
            command => command.EventKind == HookEventKind.PreToolUse);
        Assert.Equal("echo global", preToolUse.Command);
        Assert.True(preToolUse.FailOpen);

        var permissionRequest = Assert.Single(
            result.Commands,
            command => command.EventKind == HookEventKind.PermissionRequest);
        Assert.Equal("echo approve", permissionRequest.Command);
        Assert.Equal(1200, permissionRequest.TimeoutMs);
        Assert.False(permissionRequest.FailOpen);
        Assert.Equal("1", permissionRequest.Environment["DEBUG"]);
        Assert.Equal(
            Path.GetFullPath(temp.FullPath("project", ".claude", "scripts")),
            permissionRequest.WorkingDirectory);
    }
}

using System.Text.Json;
using ClaudeSharp.Core.Hooks;
using ClaudeSharp.Core.Messages;
using ClaudeSharp.Core.Permissions;
using ClaudeSharp.Core.Storage;
using ClaudeSharp.Core.Tests.Runtime;
using ClaudeSharp.Core.Tools;

namespace ClaudeSharp.Core.Tests.Hooks;

/// <summary>
/// Contains tests for configured command hook observers.
/// </summary>
public sealed class ConfiguredHookObserverTests
{
    [Fact]
    public async Task PreToolUse_CanRewriteInputAndThenBlock()
    {
        var observer = new CommandHookObserver(
        [
            new HookCommandDefinition
            {
                EventKind = HookEventKind.PreToolUse,
                Command = "rewrite-input",
            },
            new HookCommandDefinition
            {
                EventKind = HookEventKind.PreToolUse,
                Command = "block-tool",
                FailOpen = false,
            },
        ],
            new FakeHookCommandRunner(
                ("rewrite-input", new HookCommandExecutionResult(
                    0,
                    """{"updatedInput":{"query":"updated"}}""",
                    "")),
                ("block-tool", new HookCommandExecutionResult(
                    0,
                    """{"action":"block","message":"blocked by configured hook"}""",
                    ""))));

        var tool = new FakeTool { Name = "search" };
        var context = CreateToolContext(tool);
        var invocation = CreateToolUseBlock();

        var result = await observer.OnPreToolUseAsync(
            new PreToolUseHookContext(
                invocation,
                tool,
                context,
                JsonSerializer.SerializeToElement(new { query = "needle" })));

        Assert.Equal(HookAction.Block, result.Action);
        Assert.Equal("blocked by configured hook", result.Message);
        Assert.Equal("updated", result.UpdatedInput?.GetProperty("query").GetString());
    }

    [Fact]
    public async Task HookRuntimeBuilder_LoadsConfiguredPermissionHookAndReturnsDecision()
    {
        using var temp = new TempDirectory();
        var configPath = temp.WriteFile(".claude/settings.json", """
{
  "hooks": {
    "permission_request": [
      {
        "command": "permission-gate",
        "failOpen": false
      }
    ]
  }
}
""");

        var build = HookRuntimeBuilder.Build(
            temp.Root,
            configPath,
            new FakeHookCommandRunner(
                ("permission-gate", new HookCommandExecutionResult(
                    0,
                    """{"decision":"allow","message":"approved by hook"}""",
                    ""))));

        var tool = new FakeTool { Name = "search" };
        var context = CreateToolContext(tool);
        var invocation = CreateToolUseBlock();

        var result = await build.Runtime.RunPermissionRequestAsync(
            new PermissionRequestHookContext(
                invocation,
                tool,
                context,
                JsonSerializer.SerializeToElement(new { query = "needle" }),
                "approve?"));

        Assert.True(result.HasDecision);
        Assert.True(result.Approved);
        Assert.Equal("approved by hook", result.Message);
        Assert.Contains(
            build.StartupMessages,
            message => message.Contains("loaded 1 command", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, build.CommandCount);
    }

    private static ToolExecutionContext CreateToolContext(params ITool[] tools) =>
        new()
        {
            WorkingDirectory = "/tmp",
            PermissionContext = new PermissionContext(),
            Tools = tools,
            Messages = [],
            CancellationToken = CancellationToken.None,
            MainLoopModel = "claude-sonnet-4-6",
        };

    private static ToolUseBlock CreateToolUseBlock() =>
        new()
        {
            ToolUseId = "tool-1",
            Name = "search",
            Input = JsonSerializer.SerializeToElement(new { query = "needle" }),
        };

    private sealed class FakeHookCommandRunner : IHookCommandRunner
    {
        private readonly Dictionary<string, HookCommandExecutionResult> _results;

        public FakeHookCommandRunner(params (string Command, HookCommandExecutionResult Result)[] results)
        {
            _results = results.ToDictionary(pair => pair.Command, pair => pair.Result, StringComparer.Ordinal);
        }

        public Task<HookCommandExecutionResult> ExecuteAsync(
            HookCommandDefinition command,
            string payloadJson,
            string fallbackWorkingDirectory,
            IReadOnlyDictionary<string, string> ambientEnvironment,
            CancellationToken cancellationToken = default)
        {
            Assert.Contains(@"""event"":", payloadJson, StringComparison.Ordinal);
            Assert.Equal(command.EventKind.ToString(), ambientEnvironment["CLAUDESHARP_HOOK_EVENT"]);
            return Task.FromResult(_results[command.Command]);
        }
    }
}

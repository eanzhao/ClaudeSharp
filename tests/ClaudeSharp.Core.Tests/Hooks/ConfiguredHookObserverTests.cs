using System.Text.Json;
using ClaudeSharp.Core.Compaction;
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
        var runner = new FakeHookCommandRunner(
            ("rewrite-input", new HookCommandExecutionResult(
                0,
                """{"updatedInput":{"query":"updated"}}""",
                "")),
            ("block-tool", new HookCommandExecutionResult(
                0,
                """{"action":"block","message":"blocked by configured hook"}""",
                "")));

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
            runner);

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
        Assert.Equal(2, runner.Calls.Count);
        Assert.Equal("PreToolUse", runner.Calls[0].AmbientEnvironment["CLAUDESHARP_HOOK_EVENT"]);
        Assert.Equal("search", runner.Calls[0].AmbientEnvironment["CLAUDESHARP_TOOL_NAME"]);
        Assert.Contains(@"""query"":""needle""", runner.Calls[0].PayloadJson, StringComparison.Ordinal);
        Assert.Contains(@"""query"":""updated""", runner.Calls[1].PayloadJson, StringComparison.Ordinal);
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

    [Fact]
    public async Task PreToolUse_BlocksOnTimeoutAndKeepsOriginalInput()
    {
        var runner = new FakeHookCommandRunner(
            ("slow-hook", new HookCommandExecutionResult(
                -1,
                "",
                "",
                TimedOut: true)));

        var observer = new CommandHookObserver(
        [
            new HookCommandDefinition
            {
                EventKind = HookEventKind.PreToolUse,
                Command = "slow-hook",
                TimeoutMs = 15,
                FailOpen = false,
            },
        ],
            runner);

        var tool = new FakeTool { Name = "search" };
        var input = JsonSerializer.SerializeToElement(new { query = "needle" });

        var result = await observer.OnPreToolUseAsync(
            new PreToolUseHookContext(
                CreateToolUseBlock(),
                tool,
                CreateToolContext(tool),
                input));

        Assert.Equal(HookAction.Block, result.Action);
        Assert.Contains("timed out after 15ms", result.Message, StringComparison.Ordinal);
        Assert.Equal("needle", result.UpdatedInput?.GetProperty("query").GetString());
    }

    [Fact]
    public async Task PreToolUse_BlocksOnInvalidJsonWhenFailClosed()
    {
        var runner = new FakeHookCommandRunner(
            ("invalid-json", new HookCommandExecutionResult(
                0,
                "[]",
                "")));

        var observer = new CommandHookObserver(
        [
            new HookCommandDefinition
            {
                EventKind = HookEventKind.PreToolUse,
                Command = "invalid-json",
                FailOpen = false,
            },
        ],
            runner);

        var tool = new FakeTool { Name = "search" };
        var input = JsonSerializer.SerializeToElement(new { query = "needle" });

        var result = await observer.OnPreToolUseAsync(
            new PreToolUseHookContext(
                CreateToolUseBlock(),
                tool,
                CreateToolContext(tool),
                input));

        Assert.Equal(HookAction.Block, result.Action);
        Assert.Equal("Hook command output must be a JSON object.", result.Message);
    }

    [Fact]
    public async Task PreToolUse_BlocksOnMalformedJsonWhenFailClosed()
    {
        var runner = new FakeHookCommandRunner(
            ("malformed-json", new HookCommandExecutionResult(
                0,
                "{",
                "")));

        var observer = new CommandHookObserver(
        [
            new HookCommandDefinition
            {
                EventKind = HookEventKind.PreToolUse,
                Command = "malformed-json",
                FailOpen = false,
            },
        ],
            runner);

        var tool = new FakeTool { Name = "search" };
        var input = JsonSerializer.SerializeToElement(new { query = "needle" });

        var result = await observer.OnPreToolUseAsync(
            new PreToolUseHookContext(
                CreateToolUseBlock(),
                tool,
                CreateToolContext(tool),
                input));

        Assert.Equal(HookAction.Block, result.Action);
        Assert.Contains("Hook command returned invalid JSON", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PermissionRequest_CanApproveWithBooleanDecision()
    {
        var runner = new FakeHookCommandRunner(
            ("permission-bool", new HookCommandExecutionResult(
                0,
                """{"approved":true,"message":"approved by boolean"}""",
                "")));

        var observer = new CommandHookObserver(
        [
            new HookCommandDefinition
            {
                EventKind = HookEventKind.PermissionRequest,
                Command = "permission-bool",
            },
        ],
            runner);

        var tool = new FakeTool { Name = "search" };
        var result = await observer.OnPermissionRequestAsync(
            new PermissionRequestHookContext(
                CreateToolUseBlock(),
                tool,
                CreateToolContext(tool),
                JsonSerializer.SerializeToElement(new { query = "needle" }),
                "approve?"));

        Assert.True(result.HasDecision);
        Assert.True(result.Approved);
        Assert.Equal("approved by boolean", result.Message);
    }

    [Fact]
    public async Task PermissionRequest_CanDenyWithExplicitDecision()
    {
        var runner = new FakeHookCommandRunner(
            ("permission-deny", new HookCommandExecutionResult(
                0,
                """{"decision":"deny","message":"denied by hook"}""",
                "")));

        var observer = new CommandHookObserver(
        [
            new HookCommandDefinition
            {
                EventKind = HookEventKind.PermissionRequest,
                Command = "permission-deny",
            },
        ],
            runner);

        var tool = new FakeTool { Name = "search" };
        var result = await observer.OnPermissionRequestAsync(
            new PermissionRequestHookContext(
                CreateToolUseBlock(),
                tool,
                CreateToolContext(tool),
                JsonSerializer.SerializeToElement(new { query = "needle" }),
                "approve?"));

        Assert.True(result.HasDecision);
        Assert.False(result.Approved);
        Assert.Equal("denied by hook", result.Message);
    }

    [Fact]
    public async Task NonBlockingEvents_ForwardContextMetadataAndPayloadShape()
    {
        var runner = new FakeHookCommandRunner(
            ("post-tool", new HookCommandExecutionResult(0, "", "")),
            ("post-tool-failure", new HookCommandExecutionResult(0, "", "")),
            ("session-start", new HookCommandExecutionResult(0, "", "")),
            ("session-end", new HookCommandExecutionResult(0, "", "")),
            ("pre-compact", new HookCommandExecutionResult(0, "", "")),
            ("post-compact", new HookCommandExecutionResult(0, "", "")),
            ("stop", new HookCommandExecutionResult(0, "", "")),
            ("stop-failure", new HookCommandExecutionResult(0, "", "")));

        var observer = new CommandHookObserver(
        [
            new HookCommandDefinition { EventKind = HookEventKind.PostToolUse, Command = "post-tool" },
            new HookCommandDefinition { EventKind = HookEventKind.PostToolUseFailure, Command = "post-tool-failure" },
            new HookCommandDefinition { EventKind = HookEventKind.SessionStart, Command = "session-start" },
            new HookCommandDefinition { EventKind = HookEventKind.SessionEnd, Command = "session-end" },
            new HookCommandDefinition { EventKind = HookEventKind.PreCompact, Command = "pre-compact" },
            new HookCommandDefinition { EventKind = HookEventKind.PostCompact, Command = "post-compact" },
            new HookCommandDefinition { EventKind = HookEventKind.Stop, Command = "stop" },
            new HookCommandDefinition { EventKind = HookEventKind.StopFailure, Command = "stop-failure" },
        ],
            runner);

        var tool = new FakeTool { Name = "search" };
        var toolContext = CreateToolContext(tool);
        var invocation = CreateToolUseBlock();
        var input = JsonSerializer.SerializeToElement(new { query = "needle" });

        await observer.OnPostToolUseAsync(new PostToolUseHookContext(
            invocation,
            tool,
            toolContext,
            input,
            ToolResult.Success("done")));

        await observer.OnPostToolUseFailureAsync(new PostToolUseHookContext(
            invocation,
            tool,
            toolContext,
            input,
            ToolResult.Error("failed")));

        await observer.OnSessionStartAsync(new SessionHookContext(
            "session-1",
            "/work",
            "claude-sonnet-4-6",
            new ClaudeSharp.Core.Storage.ConversationSessionMetadata(),
            9));

        await observer.OnSessionEndAsync(new SessionEndHookContext(
            "session-1",
            "/work",
            "claude-sonnet-4-6",
            new ClaudeSharp.Core.Storage.ConversationSessionMetadata(),
            10,
            dueToClear: false));

        await observer.OnPreCompactAsync(new CompactHookContext(
            CompactionLifecycleKind.Conversation,
            automatic: true,
            reason: "budget",
            preserveTailCount: 4,
            messageCount: 12));

        await observer.OnPostCompactAsync(new CompactHookContext(
            CompactionLifecycleKind.SessionMemory,
            automatic: false,
            reason: "manual",
            preserveTailCount: 3,
            messageCount: 11,
            conversationResult: new ConversationCompactionResult
            {
                SummaryMessage = new ClaudeSharp.Core.Messages.UserMessage
                {
                    IsMeta = true,
                    Content = [new ClaudeSharp.Core.Messages.TextBlock("summary")],
                },
                ActiveMessages = [],
                RemovedMessageCount = 1,
            }));

        await observer.OnStopAsync(new StopHookContext(
            "session-1",
            "/work",
            "claude-sonnet-4-6",
            true,
            null,
            TimeSpan.FromSeconds(2),
            4,
            new ClaudeSharp.Core.Messages.TokenUsage
            {
                InputTokens = 1,
                OutputTokens = 2,
                CacheReadInputTokens = 3,
                CacheCreationInputTokens = 4,
            }));

        await observer.OnStopFailureAsync(new StopHookContext(
            "session-1",
            "/work",
            "claude-sonnet-4-6",
            false,
            "boom",
            TimeSpan.FromSeconds(3),
            5,
            ClaudeSharp.Core.Messages.TokenUsage.Empty));

        Assert.Equal(8, runner.Calls.Count);
        Assert.Contains(runner.Calls, call => ParseEvent(call.PayloadJson) == "PostToolUse");
        Assert.Contains(runner.Calls, call => ParseEvent(call.PayloadJson) == "PostToolUseFailure");
        Assert.Contains(runner.Calls, call => ParseEvent(call.PayloadJson) == "SessionStart");
        Assert.Contains(runner.Calls, call => ParseEvent(call.PayloadJson) == "SessionEnd");
        Assert.Contains(runner.Calls, call => ParseEvent(call.PayloadJson) == "PreCompact");
        Assert.Contains(runner.Calls, call => ParseEvent(call.PayloadJson) == "PostCompact");
        Assert.Contains(runner.Calls, call => ParseEvent(call.PayloadJson) == "Stop");
        Assert.Contains(runner.Calls, call => ParseEvent(call.PayloadJson) == "StopFailure");

        var sessionStart = Assert.Single(runner.Calls, call => call.Command == "session-start");
        Assert.Equal("session-1", sessionStart.AmbientEnvironment["CLAUDESHARP_SESSION_ID"]);
        Assert.Equal("/work", sessionStart.AmbientEnvironment["CLAUDESHARP_WORKDIR"]);
        Assert.Equal("claude-sonnet-4-6", sessionStart.AmbientEnvironment["CLAUDESHARP_MODEL"]);

        var stop = Assert.Single(runner.Calls, call => call.Command == "stop");
        Assert.Contains(@"""durationMs"":2000", stop.PayloadJson, StringComparison.Ordinal);
        Assert.Contains(@"""inputTokens"":1", stop.PayloadJson, StringComparison.Ordinal);
        Assert.Contains(@"""outputTokens"":2", stop.PayloadJson, StringComparison.Ordinal);
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

        public List<HookExecutionCall> Calls { get; } = [];

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
            Calls.Add(new HookExecutionCall(
                command.Command,
                payloadJson,
                fallbackWorkingDirectory,
                new Dictionary<string, string>(ambientEnvironment, StringComparer.OrdinalIgnoreCase)));
            Assert.Contains(@"""event"":", payloadJson, StringComparison.Ordinal);
            Assert.Equal(command.EventKind.ToString(), ambientEnvironment["CLAUDESHARP_HOOK_EVENT"]);
            return Task.FromResult(_results[command.Command]);
        }
    }

    private sealed record HookExecutionCall(
        string Command,
        string PayloadJson,
        string FallbackWorkingDirectory,
        IReadOnlyDictionary<string, string> AmbientEnvironment);

    private static string ParseEvent(string payloadJson) =>
        JsonDocument.Parse(payloadJson).RootElement.GetProperty("event").GetString()!;
}

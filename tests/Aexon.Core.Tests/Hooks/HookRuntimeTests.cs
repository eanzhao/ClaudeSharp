using System.Text.Json;
using Aexon.Core.Compaction;
using Aexon.Core.Hooks;
using Aexon.Core.Messages;
using Aexon.Core.Permissions;
using Aexon.Core.Storage;
using Aexon.Core.Tests.Runtime;
using Aexon.Core.Tools;

namespace Aexon.Core.Tests.Hooks;

/// <summary>
/// Contains tests for hook Runtime.
/// </summary>
public sealed class HookRuntimeTests
{
    [Fact]
    public async Task PassiveObserver_ReturnsDefaultNoOpResults()
    {
        var observer = new PassiveHookObserver();
        var tool = new FakeTool { Name = "search" };
        var context = CreateToolContext(tool);
        var invocation = CreateToolUseBlock();
        var input = JsonSerializer.SerializeToElement(new { query = "needle" });
        var permissionContext = new PermissionRequestHookContext(
            invocation,
            tool,
            context,
            input,
            "approve?");
        var sessionContext = new SessionHookContext(
            "session-1",
            "/work",
            "claude-sonnet-4-6",
            new ConversationSessionMetadata(),
            1);
        var sessionEndContext = new SessionEndHookContext(
            "session-1",
            "/work",
            "claude-sonnet-4-6",
            new ConversationSessionMetadata(),
            1,
            false);
        var compactContext = new CompactHookContext(
            CompactionLifecycleKind.Conversation,
            false,
            "manual",
            4,
            1);
        var stopContext = new StopHookContext(
            "session-1",
            "/work",
            "claude-sonnet-4-6",
            true,
            null,
            TimeSpan.FromSeconds(1),
            2,
            TokenUsage.Empty);

        var preToolResult = await observer.OnPreToolUseAsync(
            new PreToolUseHookContext(invocation, tool, context, input));
        var permissionResult = await observer.OnPermissionRequestAsync(permissionContext);

        await observer.OnPostToolUseAsync(
            new PostToolUseHookContext(
                invocation,
                tool,
                context,
                input,
                ToolResult.Success("ok")));
        await observer.OnPostToolUseFailureAsync(
            new PostToolUseHookContext(
                invocation,
                tool,
                context,
                input,
                ToolResult.Error("boom")));
        await observer.OnSessionStartAsync(sessionContext);
        await observer.OnSessionEndAsync(sessionEndContext);
        await observer.OnPreCompactAsync(compactContext);
        await observer.OnPostCompactAsync(
            new CompactHookContext(
                CompactionLifecycleKind.Conversation,
                false,
                "manual",
                4,
                1,
                conversationResult: new ConversationCompactionResult
                {
                    SummaryMessage = UserMessage.FromText("summary"),
                    ActiveMessages = [],
                    RemovedMessageCount = 0,
                }));
        await observer.OnStopAsync(stopContext);
        await observer.OnStopFailureAsync(
            new StopHookContext(
                stopContext.SessionId,
                stopContext.WorkingDirectory,
                stopContext.Model,
                false,
                stopContext.ErrorMessage,
                stopContext.Duration,
                stopContext.TurnCount,
                stopContext.TotalUsage));

        Assert.Equal(HookAction.Continue, preToolResult.Action);
        Assert.False(permissionResult.HasDecision);
        Assert.Equal(HookEventKind.PreToolUse, new PreToolUseHookContext(invocation, tool, context, input).Kind);
        Assert.Equal(HookEventKind.PostToolUse, new PostToolUseHookContext(invocation, tool, context, input, ToolResult.Success("ok")).Kind);
        Assert.Equal(HookEventKind.PostToolUseFailure, new PostToolUseHookContext(invocation, tool, context, input, ToolResult.Error("boom")).Kind);
        Assert.Equal(HookEventKind.PermissionRequest, permissionContext.Kind);
        Assert.Equal(HookEventKind.SessionStart, sessionContext.Kind);
        Assert.Equal(HookEventKind.SessionEnd, sessionEndContext.Kind);
        Assert.Equal(HookEventKind.PreCompact, compactContext.Kind);
        Assert.Equal(
            HookEventKind.PostCompact,
            new CompactHookContext(
                CompactionLifecycleKind.Conversation,
                false,
                "manual",
                4,
                1,
                conversationResult: new ConversationCompactionResult
                {
                    SummaryMessage = UserMessage.FromText("summary"),
                    ActiveMessages = [],
                    RemovedMessageCount = 0,
                }).Kind);
        Assert.Equal(HookEventKind.Stop, stopContext.Kind);
        Assert.Equal(
            HookEventKind.StopFailure,
            new StopHookContext(
                stopContext.SessionId,
                stopContext.WorkingDirectory,
                stopContext.Model,
                false,
                stopContext.ErrorMessage,
                stopContext.Duration,
                stopContext.TurnCount,
                stopContext.TotalUsage).Kind);
        Assert.Equal(HookAction.Block, PreToolUseHookResult.Block("nope").Action);
        Assert.Equal(HookAction.Continue, PreToolUseHookResult.Continue().Action);
        Assert.True(PermissionRequestHookResult.Allow().HasDecision);
        Assert.True(PermissionRequestHookResult.Deny().HasDecision);
    }

    [Fact]
    public async Task HookRuntime_PropagatesUpdatedInputAndDispatchesLifecycleCallbacks()
    {
        var first = new RecordingHookObserver
        {
            PreToolUseResult = PreToolUseHookResult.Continue(
                JsonSerializer.SerializeToElement(new { query = "updated" })),
            PermissionRequestResult = PermissionRequestHookResult.Allow("approved"),
        };
        var second = new RecordingHookObserver
        {
            PreToolUseResult = PreToolUseHookResult.Block("stop here"),
        };

        var runtime = new HookRuntime([first, second]);
        var tool = new FakeTool { Name = "search" };
        var toolContext = CreateToolContext(tool);
        var invocation = CreateToolUseBlock();
        var preContext = new PreToolUseHookContext(
            invocation,
            tool,
            toolContext,
            JsonSerializer.SerializeToElement(new { query = "needle" }));

        var preResult = await runtime.RunPreToolUseAsync(preContext);
        var permissionResult = await runtime.RunPermissionRequestAsync(
            new PermissionRequestHookContext(
                invocation,
                tool,
                toolContext,
                JsonSerializer.SerializeToElement(new { query = "needle" }),
                "approve?"));

        await runtime.OnPostToolUseAsync(
            new PostToolUseHookContext(
                invocation,
                tool,
                toolContext,
                JsonSerializer.SerializeToElement(new { query = "needle" }),
                ToolResult.Success("ok")));
        await runtime.OnPostToolUseFailureAsync(
            new PostToolUseHookContext(
                invocation,
                tool,
                toolContext,
                JsonSerializer.SerializeToElement(new { query = "needle" }),
                ToolResult.Error("boom")));
        await runtime.OnSessionStartAsync(
            new SessionHookContext("session-1", "/work", "model", new ConversationSessionMetadata(), 2));
        await runtime.OnSessionEndAsync(
            new SessionEndHookContext("session-1", "/work", "model", new ConversationSessionMetadata(), 2, false));
        await runtime.OnPreCompactAsync(
            new CompactHookContext(CompactionLifecycleKind.Microcompact, true, "auto", 4, 3));
        await runtime.OnPostCompactAsync(
            new CompactHookContext(
                CompactionLifecycleKind.SessionMemory,
                true,
                "auto",
                4,
                3,
                sessionMemoryResult: new SessionMemoryCompactionResult
                {
                    RewriteResult = new ConversationRewriteResult
                    {
                        Boundary = new ConversationRewriteBoundary
                        {
                            Direction = ConversationRewriteDirection.UpTo,
                            RequestedIndex = 2,
                            AppliedIndex = 2,
                            MessageCount = 3,
                            FoldedStartIndex = 0,
                            FoldedEndIndexExclusive = 2,
                        },
                        SummaryMessage = UserMessage.FromText("summary"),
                        Messages = [],
                        FoldedMessages = [],
                        PreservedMessages = [],
                    },
                    MemoryMessage = UserMessage.FromText("summary"),
                    FoldedMessages = [],
                    ActiveMessages = [],
                    SummaryText = "summary",
                    RequestedPreserveTailCount = 4,
                    EffectivePreserveTailCount = 1,
                }));
        await runtime.OnStopAsync(
            new StopHookContext("session-1", "/work", "model", true, null, TimeSpan.FromSeconds(1), 1, TokenUsage.Empty));
        await runtime.OnStopFailureAsync(
            new StopHookContext("session-1", "/work", "model", false, "boom", TimeSpan.FromSeconds(1), 1, TokenUsage.Empty));

        Assert.Equal(HookAction.Block, preResult.Action);
        Assert.Equal("stop here", preResult.Message);
        Assert.Equal("updated", preResult.UpdatedInput?.GetProperty("query").GetString());
        Assert.True(permissionResult.HasDecision);
        Assert.Single(first.Invocations);
        Assert.Single(second.Invocations);
        Assert.Equal("updated", second.Invocations[0].GetProperty("query").GetString());
        Assert.Contains("pre-tool-use", first.Events);
        Assert.Contains("permission-request", first.Events);
        Assert.Contains("post-tool-use", first.Events);
        Assert.Contains("post-tool-use-failure", first.Events);
        Assert.Contains("session-start", first.Events);
        Assert.Contains("session-end", first.Events);
        Assert.Contains("pre-compact", first.Events);
        Assert.Contains("post-compact", first.Events);
        Assert.Contains("stop", first.Events);
        Assert.Contains("stop-failure", first.Events);
    }

    [Fact]
    public void ContextAndResultTypesExposeExpectedMetadata()
    {
        var tool = new FakeTool { Name = "search" };
        var toolContext = CreateToolContext(tool);
        var invocation = CreateToolUseBlock();
        var input = JsonSerializer.SerializeToElement(new { query = "needle" });
        var conversationResult = new ConversationCompactionResult
        {
            SummaryMessage = UserMessage.FromText("summary"),
            ActiveMessages = [],
            RemovedMessageCount = 1,
        };
        var sessionMemoryResult = new SessionMemoryCompactionResult
        {
            RewriteResult = new ConversationRewriteResult
            {
                Boundary = new ConversationRewriteBoundary
                {
                    Direction = ConversationRewriteDirection.UpTo,
                    RequestedIndex = 2,
                    AppliedIndex = 2,
                    MessageCount = 3,
                    FoldedStartIndex = 0,
                    FoldedEndIndexExclusive = 2,
                },
                SummaryMessage = UserMessage.FromText("summary"),
                Messages = [],
                FoldedMessages = [],
                PreservedMessages = [],
            },
            MemoryMessage = UserMessage.FromText("summary"),
            FoldedMessages = [],
            ActiveMessages = [],
            SummaryText = "summary",
            RequestedPreserveTailCount = 4,
            EffectivePreserveTailCount = 1,
        };
        var microcompactResult = new MicrocompactResult
        {
            UpdatedMessages = [],
            Edits = [],
            ClearedToolResultCount = 0,
            ClearedThinkingBlockCount = 0,
        };

        var preTool = new PreToolUseHookContext(invocation, tool, toolContext, input);
        var postTool = new PostToolUseHookContext(invocation, tool, toolContext, input, ToolResult.Success("ok"));
        var postToolFailure = new PostToolUseHookContext(invocation, tool, toolContext, input, ToolResult.Error("boom"));
        var permission = new PermissionRequestHookContext(invocation, tool, toolContext, input, "approve?");
        var session = new SessionHookContext("session-1", "/work", "model", new ConversationSessionMetadata(), 2);
        var sessionEnd = new SessionEndHookContext("session-1", "/work", "model", new ConversationSessionMetadata(), 2, true);
        var preCompact = new CompactHookContext(CompactionLifecycleKind.Microcompact, true, "auto", 4, 3);
        var postCompact = new CompactHookContext(
            CompactionLifecycleKind.Conversation,
            true,
            "auto",
            4,
            3,
            conversationResult: conversationResult,
            sessionMemoryResult: sessionMemoryResult,
            microcompactResult: microcompactResult);
        var stop = new StopHookContext("session-1", "/work", "model", true, null, TimeSpan.FromSeconds(2), 3, TokenUsage.Empty);

        Assert.Equal(HookEventKind.PreToolUse, preTool.Kind);
        Assert.Equal(HookEventKind.PostToolUse, postTool.Kind);
        Assert.Equal(HookEventKind.PostToolUseFailure, postToolFailure.Kind);
        Assert.Equal(HookEventKind.PermissionRequest, permission.Kind);
        Assert.Equal(HookEventKind.SessionStart, session.Kind);
        Assert.Equal(HookEventKind.SessionEnd, sessionEnd.Kind);
        Assert.Equal(HookEventKind.PreCompact, preCompact.Kind);
        Assert.Equal(HookEventKind.PostCompact, postCompact.Kind);
        Assert.Equal(HookEventKind.Stop, stop.Kind);
        var summaryMessage = Assert.IsType<UserMessage>(conversationResult.SummaryMessage);
        Assert.Equal("summary", Assert.IsType<TextBlock>(summaryMessage.Content[0]).Text);
        Assert.Equal(1, conversationResult.RemovedMessageCount);
        Assert.Equal(1, sessionMemoryResult.EffectivePreserveTailCount);
        Assert.False(PreToolUseHookResult.Continue().Message is not null);
        Assert.True(PreToolUseHookResult.Block("blocked").Action == HookAction.Block);
        Assert.True(PermissionRequestHookResult.Allow().HasDecision);
        Assert.True(PermissionRequestHookResult.Deny().HasDecision);
        Assert.False(PermissionRequestHookResult.NoDecision().HasDecision);
        Assert.Equal("/work", preTool.ToolExecutionContext.WorkingDirectory);
        Assert.Equal("approve?", permission.Description);
        Assert.Equal("auto", preCompact.Reason);
        Assert.Same(conversationResult, postCompact.ConversationResult);
        Assert.Same(sessionMemoryResult, postCompact.SessionMemoryResult);
        Assert.Same(microcompactResult, postCompact.MicrocompactResult);
    }

    private static ToolExecutionContext CreateToolContext(ITool tool) =>
        new()
        {
            WorkingDirectory = "/work",
            PermissionContext = new PermissionContext(),
            Tools = [tool],
            Messages = [],
            CancellationToken = CancellationToken.None,
        };

    private static ToolUseBlock CreateToolUseBlock() =>
        new()
        {
            ToolUseId = "tool-1",
            Name = "search",
            Input = JsonSerializer.SerializeToElement(new { query = "needle" }),
        };

    private sealed class PassiveHookObserver : HookObserver
    {
    }

    private sealed class RecordingHookObserver : HookObserver
    {
        public List<JsonElement> Invocations { get; } = [];
        public List<string> Events { get; } = [];
        public PreToolUseHookResult PreToolUseResult { get; init; } = PreToolUseHookResult.Continue();
        public PermissionRequestHookResult PermissionRequestResult { get; init; } = PermissionRequestHookResult.NoDecision();

        public override ValueTask<PreToolUseHookResult> OnPreToolUseAsync(
            PreToolUseHookContext context,
            CancellationToken cancellationToken = default)
        {
            Invocations.Add(context.Input);
            Events.Add("pre-tool-use");
            return ValueTask.FromResult(PreToolUseResult);
        }

        public override ValueTask<PermissionRequestHookResult> OnPermissionRequestAsync(
            PermissionRequestHookContext context,
            CancellationToken cancellationToken = default)
        {
            Events.Add("permission-request");
            return ValueTask.FromResult(PermissionRequestResult);
        }

        public override ValueTask OnPostToolUseAsync(
            PostToolUseHookContext context,
            CancellationToken cancellationToken = default)
        {
            Events.Add("post-tool-use");
            return ValueTask.CompletedTask;
        }

        public override ValueTask OnPostToolUseFailureAsync(
            PostToolUseHookContext context,
            CancellationToken cancellationToken = default)
        {
            Events.Add("post-tool-use-failure");
            return ValueTask.CompletedTask;
        }

        public override ValueTask OnSessionStartAsync(
            SessionHookContext context,
            CancellationToken cancellationToken = default)
        {
            Events.Add("session-start");
            return ValueTask.CompletedTask;
        }

        public override ValueTask OnSessionEndAsync(
            SessionEndHookContext context,
            CancellationToken cancellationToken = default)
        {
            Events.Add("session-end");
            return ValueTask.CompletedTask;
        }

        public override ValueTask OnPreCompactAsync(
            CompactHookContext context,
            CancellationToken cancellationToken = default)
        {
            Events.Add("pre-compact");
            return ValueTask.CompletedTask;
        }

        public override ValueTask OnPostCompactAsync(
            CompactHookContext context,
            CancellationToken cancellationToken = default)
        {
            Events.Add("post-compact");
            return ValueTask.CompletedTask;
        }

        public override ValueTask OnStopAsync(
            StopHookContext context,
            CancellationToken cancellationToken = default)
        {
            Events.Add("stop");
            return ValueTask.CompletedTask;
        }

        public override ValueTask OnStopFailureAsync(
            StopHookContext context,
            CancellationToken cancellationToken = default)
        {
            Events.Add("stop-failure");
            return ValueTask.CompletedTask;
        }
    }
}

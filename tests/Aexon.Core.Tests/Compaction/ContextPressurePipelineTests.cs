using Aexon.Core.Compaction;
using Aexon.Core.Messages;

namespace Aexon.Core.Tests.Compaction;

/// <summary>
/// Contains tests for context Pressure Pipeline.
/// </summary>
public sealed class ContextPressurePipelineTests
{
    [Fact]
    public void Prepare_ReturnsDisabledDecisionWhenAutoCompactIsOff()
    {
        var policy = new RecordingPolicy();
        var pipeline = new DefaultContextPressurePipeline(policy);

        var result = pipeline.Prepare(
            [CompactionTestHelpers.UserText("hello")],
            CreateOptions(enableAutoCompact: false));

        Assert.False(result.HasChanges);
        Assert.Equal(AutoCompactAction.None, result.InitialDecision.Action);
        Assert.Equal(AutoCompactAction.None, result.FinalDecision.Action);
        Assert.Equal("auto-compact-disabled", result.InitialDecision.Reason);
        Assert.Equal(0, policy.CallCount);
    }

    [Fact]
    public void Prepare_UsesMicrocompactThenSessionMemoryBeforeFullCompact()
    {
        var policy = new RecordingPolicy(
            CreateDecision(AutoCompactAction.FullCompact, "initial"),
            CreateDecision(AutoCompactAction.FullCompact, "after micro"),
            CreateDecision(AutoCompactAction.None, "after session memory"));
        var microCompactor = new RecordingMicroCompactor
        {
            ResultToReturn = new MicrocompactResult
            {
                UpdatedMessages =
                [
                    CompactionTestHelpers.UserText("micro"),
                    CompactionTestHelpers.UserText("tail"),
                ],
                Edits =
                [
                    new MicrocompactEdit
                    {
                        MessageId = "m-1",
                        ClearToolResult = true,
                        ClearThinking = false,
                    },
                ],
                ClearedToolResultCount = 1,
                ClearedThinkingBlockCount = 0,
            },
        };
        var sessionMemoryCompactor = new RecordingSessionMemoryCompactor();
        var conversationCompactor = new RecordingConversationCompactor();
        var pipeline = new DefaultContextPressurePipeline(
            policy,
            microCompactor,
            sessionMemoryCompactor,
            conversationCompactor);

        var sessionRewrite = new ConversationRewriter().RewriteUpTo(
            [
                CompactionTestHelpers.UserText("older"),
                CompactionTestHelpers.UserText("tail"),
            ],
            1,
            CompactionTestHelpers.UserText("memory", isMeta: true));

        sessionMemoryCompactor.ResultToReturn = new SessionMemoryCompactionResult
        {
            RewriteResult = sessionRewrite,
            MemoryMessage = CompactionTestHelpers.UserText("memory", isMeta: true),
            FoldedMessages = sessionRewrite.FoldedMessages,
            ActiveMessages = sessionRewrite.Messages,
            SummaryText = "memory",
            RequestedPreserveTailCount = 1,
            EffectivePreserveTailCount = 1,
        };

        var result = pipeline.Prepare(
            [CompactionTestHelpers.UserText("hello")],
            CreateOptions(
                enableSessionMemoryCompact: true,
                preserveTailCount: 3,
                sessionMemoryPreserveTailCount: 1));

        Assert.True(result.HasChanges);
        Assert.Equal(AutoCompactAction.FullCompact, result.InitialDecision.Action);
        Assert.Equal(AutoCompactAction.None, result.FinalDecision.Action);
        Assert.NotNull(result.MicrocompactResult);
        Assert.NotNull(result.SessionMemoryResult);
        Assert.Null(result.CompactionResult);
        Assert.Equal(1, microCompactor.RunCalls);
        Assert.Equal(1, sessionMemoryCompactor.CallCount);
        Assert.Equal(3, sessionMemoryCompactor.LastOptions!.PreserveTailCount);
        Assert.Equal(0, conversationCompactor.CallCount);
        Assert.Equal(3, policy.CallCount);
    }

    [Fact]
    public void Prepare_FallsBackToFullCompactWhenSessionMemoryIsDisabled()
    {
        var policy = new RecordingPolicy(
            CreateDecision(AutoCompactAction.FullCompact, "initial"),
            CreateDecision(AutoCompactAction.FullCompact, "after micro"),
            CreateDecision(AutoCompactAction.None, "after compact"));
        var microCompactor = new RecordingMicroCompactor
        {
            ResultToReturn = new MicrocompactResult
            {
                UpdatedMessages =
                [
                    CompactionTestHelpers.UserText("older"),
                    CompactionTestHelpers.UserText("tail"),
                ],
                Edits = Array.Empty<MicrocompactEdit>(),
                ClearedToolResultCount = 0,
                ClearedThinkingBlockCount = 0,
            },
        };
        var conversationCompactor = new RecordingConversationCompactor
        {
            ResultToReturn = new ConversationCompactionResult
            {
                SummaryMessage = CompactionTestHelpers.UserText("summary", isMeta: true),
                ActiveMessages =
                [
                    CompactionTestHelpers.UserText("summary", isMeta: true),
                    CompactionTestHelpers.UserText("tail"),
                ],
                RemovedMessageCount = 1,
            },
        };
        var pipeline = new DefaultContextPressurePipeline(
            policy,
            microCompactor,
            new RecordingSessionMemoryCompactor(),
            conversationCompactor);

        var result = pipeline.Prepare(
            [CompactionTestHelpers.UserText("hello")],
            CreateOptions(enableSessionMemoryCompact: false));

        Assert.True(result.HasChanges);
        Assert.Equal(AutoCompactAction.FullCompact, result.InitialDecision.Action);
        Assert.Equal(AutoCompactAction.None, result.FinalDecision.Action);
        Assert.NotNull(result.CompactionResult);
        Assert.Null(result.SessionMemoryResult);
        Assert.Equal(1, conversationCompactor.CallCount);
        Assert.Equal(2, conversationCompactor.LastPreserveTailCount);
    }

    private static ContextPressureOptions CreateOptions(
        bool enableAutoCompact = true,
        bool enableSessionMemoryCompact = true,
        int preserveTailCount = 2,
        int sessionMemoryPreserveTailCount = 2) =>
        new()
        {
            EnableAutoCompact = enableAutoCompact,
            EnableSessionMemoryCompact = enableSessionMemoryCompact,
            PreserveTailCount = preserveTailCount,
            Policy = new AutoCompactPolicyOptions
            {
                ApproxContextWindowTokens = 2_000,
                MaxOutputTokens = 100,
                BufferTokens = 100,
                ApproxCharsPerToken = 4,
                MinimumMessageCount = 1,
                WarningRatio = 0.5,
                BlockingRatio = 0.8,
            },
            SessionMemory = new SessionMemoryCompactionOptions
            {
                PreserveTailCount = sessionMemoryPreserveTailCount,
                MinimumFoldedMessageCount = 1,
            },
        };

    private static AutoCompactDecision CreateDecision(
        AutoCompactAction action,
        string reason) =>
        new()
        {
            Action = action,
            Reason = reason,
            ApproxPromptTokens = 10,
            AvailableInputBudgetTokens = 100,
        };

    private sealed class RecordingPolicy : IAutoCompactPolicy
    {
        private readonly Queue<AutoCompactDecision> _decisions;

        public RecordingPolicy(params AutoCompactDecision[] decisions)
        {
            _decisions = new Queue<AutoCompactDecision>(decisions);
        }

        public int CallCount { get; private set; }
        public AutoCompactDecision Evaluate(
            IReadOnlyList<ConversationMessage> messages,
            AutoCompactPolicyOptions options)
        {
            CallCount++;

            if (_decisions.Count > 0)
                return _decisions.Dequeue();

            return CreateDecision(AutoCompactAction.None, "fallback");
        }
    }

    private sealed class RecordingMicroCompactor : IMicroCompactor
    {
        public int RunCalls { get; private set; }
        public MicrocompactResult? ResultToReturn { get; init; }

        public MicrocompactResult Run(
            IReadOnlyList<ConversationMessage> messages,
            MicrocompactRunOptions? options = null,
            DateTimeOffset? now = null)
        {
            RunCalls++;
            return ResultToReturn ?? new MicrocompactResult
            {
                UpdatedMessages = messages.ToArray(),
                Edits = Array.Empty<MicrocompactEdit>(),
                ClearedToolResultCount = 0,
                ClearedThinkingBlockCount = 0,
            };
        }
    }

    private sealed class RecordingSessionMemoryCompactor : ISessionMemoryCompactor
    {
        public int CallCount { get; private set; }
        public SessionMemoryCompactionOptions? LastOptions { get; private set; }
        public SessionMemoryCompactionResult? ResultToReturn { get; set; }

        public SessionMemoryCompactionResult? Compact(
            IReadOnlyList<ConversationMessage> messages,
            SessionMemoryCompactionOptions? options = null)
        {
            CallCount++;
            LastOptions = options;
            return ResultToReturn;
        }
    }

    private sealed class RecordingConversationCompactor : IConversationCompactor
    {
        public int CallCount { get; private set; }
        public int LastPreserveTailCount { get; private set; }
        public ConversationCompactionResult? ResultToReturn { get; init; }

        public ConversationCompactionResult? Compact(
            IReadOnlyList<ConversationMessage> messages,
            int preserveTailCount = 8)
        {
            CallCount++;
            LastPreserveTailCount = preserveTailCount;
            return ResultToReturn;
        }

        public ConversationCompactionResult? CompactUpTo(
            IReadOnlyList<ConversationMessage> messages,
            int upToIndex) => ResultToReturn;

        public ConversationCompactionResult? CompactFrom(
            IReadOnlyList<ConversationMessage> messages,
            int fromIndex) => ResultToReturn;
    }
}

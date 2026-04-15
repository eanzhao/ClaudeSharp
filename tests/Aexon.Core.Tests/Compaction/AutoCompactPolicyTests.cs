using Aexon.Core.Compaction;
using Aexon.Core.Messages;

namespace Aexon.Core.Tests.Compaction;

/// <summary>
/// Contains tests for auto Compact Policy.
/// </summary>
public sealed class AutoCompactPolicyTests
{
    [Fact]
    public void Evaluate_ReturnsNoneWhenBelowMinimumMessageCount()
    {
        var policy = new HeuristicAutoCompactPolicy(new FakeEstimator(3));
        var options = CreateOptions();
        var messages = new ConversationMessage[]
        {
            CompactionTestHelpers.UserText("hello"),
        };

        var decision = policy.Evaluate(messages, options);

        Assert.Equal(AutoCompactAction.None, decision.Action);
        Assert.Equal("message-count-below-threshold", decision.Reason);
        Assert.Equal(3, decision.ApproxPromptTokens);
        Assert.Equal(1_024, decision.AvailableInputBudgetTokens);
    }

    [Fact]
    public void Evaluate_ReturnsTryMicrocompactAtWarningThreshold()
    {
        var policy = new HeuristicAutoCompactPolicy(new FakeEstimator(900));
        var options = CreateOptions(
            contextWindowTokens: 2_000,
            maxOutputTokens: 100,
            bufferTokens: 100,
            minimumMessageCount: 1,
            warningRatio: 0.5,
            blockingRatio: 0.8);

        var decision = policy.Evaluate(
            [CompactionTestHelpers.UserText("hello")],
            options);

        Assert.Equal(AutoCompactAction.TryMicrocompact, decision.Action);
        Assert.Contains("warning threshold", decision.Reason);
        Assert.Equal(900, decision.ApproxPromptTokens);
        Assert.Equal(1_800, decision.AvailableInputBudgetTokens);
    }

    [Fact]
    public void Evaluate_ReturnsFullCompactAtBlockingThreshold()
    {
        var policy = new HeuristicAutoCompactPolicy(new FakeEstimator(1_600));
        var options = CreateOptions(
            contextWindowTokens: 2_000,
            maxOutputTokens: 100,
            bufferTokens: 100,
            minimumMessageCount: 1,
            warningRatio: 0.5,
            blockingRatio: 0.8);

        var decision = policy.Evaluate(
            [CompactionTestHelpers.UserText("hello")],
            options);

        Assert.Equal(AutoCompactAction.FullCompact, decision.Action);
        Assert.Contains("blocking threshold", decision.Reason);
        Assert.Equal(1_600, decision.ApproxPromptTokens);
        Assert.Equal(1_800, decision.AvailableInputBudgetTokens);
    }

    private static AutoCompactPolicyOptions CreateOptions(
        int contextWindowTokens = 1_000,
        int maxOutputTokens = 100,
        int bufferTokens = 100,
        int minimumMessageCount = 2,
        double warningRatio = 0.72,
        double blockingRatio = 0.82) =>
        new()
        {
            ApproxContextWindowTokens = contextWindowTokens,
            MaxOutputTokens = maxOutputTokens,
            BufferTokens = bufferTokens,
            ApproxCharsPerToken = 4,
            MinimumMessageCount = minimumMessageCount,
            WarningRatio = warningRatio,
            BlockingRatio = blockingRatio,
        };

    private sealed class FakeEstimator : IPromptTokenEstimator
    {
        private readonly int _totalTokens;

        public FakeEstimator(int totalTokens)
        {
            _totalTokens = totalTokens;
        }

        public PromptTokenEstimate Estimate(
            IReadOnlyList<ConversationMessage> messages,
            PromptTokenEstimatorOptions? options = null) =>
            new()
            {
                TotalTokens = _totalTokens,
                EnvelopeTokens = 0,
                TextTokens = 0,
                ThinkingTokens = 0,
                ToolUseTokens = 0,
                ToolResultTokens = 0,
                JsonTokens = 0,
                MessageCount = messages.Count,
            };
    }
}

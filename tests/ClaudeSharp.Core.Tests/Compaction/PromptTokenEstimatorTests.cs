using System.Text.Json;
using ClaudeSharp.Core.Compaction;
using ClaudeSharp.Core.Messages;

namespace ClaudeSharp.Core.Tests.Compaction;

public sealed class PromptTokenEstimatorTests
{
    [Fact]
    public void Estimate_CountsAllMessageKindsAndJsonBranches()
    {
        var estimator = new HeuristicPromptTokenEstimator();
        var messages = new ConversationMessage[]
        {
            CompactionTestHelpers.SystemText("sys"),
            CompactionTestHelpers.UserText("hello"),
            CompactionTestHelpers.UserToolResult("tool-1", "result"),
            CompactionTestHelpers.AssistantText("ok"),
            CompactionTestHelpers.AssistantThinking("abcd", "sig"),
            CompactionTestHelpers.AssistantToolUse(
                "tool-2",
                "tool",
                new
                {
                    x = "y",
                    arr = new object?[] { 1, false },
                    n = (string?)null,
                }),
        };

        var estimate = estimator.Estimate(messages);

        Assert.Equal(58, estimate.EnvelopeTokens);
        Assert.Equal(4, estimate.TextTokens);
        Assert.Equal(8, estimate.ThinkingTokens);
        Assert.Equal(9, estimate.ToolUseTokens);
        Assert.Equal(8, estimate.ToolResultTokens);
        Assert.Equal(22, estimate.JsonTokens);
        Assert.Equal(
            estimate.EnvelopeTokens +
            estimate.TextTokens +
            estimate.ThinkingTokens +
            estimate.ToolUseTokens +
            estimate.ToolResultTokens +
            estimate.JsonTokens,
            estimate.TotalTokens);
        Assert.Equal(6, estimate.MessageCount);
    }

    [Fact]
    public void Estimate_IgnoresWhitespaceOnlyText()
    {
        var estimator = new HeuristicPromptTokenEstimator();

        var estimate = estimator.Estimate(
            [
                CompactionTestHelpers.SystemText("   \t\r\n"),
                CompactionTestHelpers.UserText("a   b"),
            ]);

        Assert.Equal(19, estimate.TotalTokens);
        Assert.Equal(18, estimate.EnvelopeTokens);
        Assert.Equal(1, estimate.TextTokens);
    }
}

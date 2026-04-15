using Aexon.Core.Compaction;
using Aexon.Core.Messages;

namespace Aexon.Core.Tests.Compaction;

/// <summary>
/// Contains tests for conversation Rewriter.
/// </summary>
public sealed class ConversationRewriterTests
{
    [Fact]
    public void ResolveUpToBoundary_AdjustsIntoToolPairStart()
    {
        var rewriter = new ConversationRewriter();
        var messages = CreateAtomicThread();

        var boundary = rewriter.ResolveUpToBoundary(messages, 2);

        Assert.Equal(ConversationRewriteDirection.UpTo, boundary.Direction);
        Assert.Equal(2, boundary.RequestedIndex);
        Assert.Equal(1, boundary.AppliedIndex);
        Assert.True(boundary.WasAdjusted);
        Assert.Equal(0, boundary.FoldedStartIndex);
        Assert.Equal(1, boundary.FoldedEndIndexExclusive);
        Assert.Equal(1, boundary.FoldedMessageCount);
        Assert.Equal(3, boundary.PreservedMessageCount);
    }

    [Fact]
    public void ResolveFromBoundary_AdjustsIntoToolPairEnd()
    {
        var rewriter = new ConversationRewriter();
        var messages = CreateAtomicThread();

        var boundary = rewriter.ResolveFromBoundary(messages, 2);

        Assert.Equal(ConversationRewriteDirection.From, boundary.Direction);
        Assert.Equal(2, boundary.RequestedIndex);
        Assert.Equal(3, boundary.AppliedIndex);
        Assert.True(boundary.WasAdjusted);
        Assert.Equal(3, boundary.FoldedStartIndex);
        Assert.Equal(4, boundary.FoldedEndIndexExclusive);
        Assert.Equal(1, boundary.FoldedMessageCount);
        Assert.Equal(3, boundary.PreservedMessageCount);
    }

    [Fact]
    public void RewriteUpTo_PreservesToolPairAndPrependsSummary()
    {
        var rewriter = new ConversationRewriter();
        var messages = CreateAtomicThread();
        var summary = CompactionTestHelpers.UserText("summary", isMeta: true);

        var result = rewriter.RewriteUpTo(messages, 2, summary);

        Assert.True(result.HasChanges);
        Assert.Equal(summary, result.SummaryMessage);
        Assert.Equal(4, result.Messages.Count);
        Assert.Equal(summary, result.Messages[0]);
        Assert.Equal("assistant", result.Messages[1].Type);
        Assert.Equal("user", result.Messages[2].Type);
        Assert.Equal("user", result.Messages[3].Type);
        Assert.Single(result.FoldedMessages);
        Assert.Equal(3, result.PreservedMessages.Count);
    }

    [Fact]
    public void RewriteFrom_PreservesPrefixAndAppendsSummary()
    {
        var rewriter = new ConversationRewriter();
        var messages = CreateAtomicThread();
        var summary = CompactionTestHelpers.UserText("summary", isMeta: true);

        var result = rewriter.RewriteFrom(messages, 2, summary);

        Assert.True(result.HasChanges);
        Assert.Equal(summary, result.SummaryMessage);
        Assert.Equal(4, result.Messages.Count);
        Assert.Equal("user", result.Messages[0].Type);
        Assert.Equal("assistant", result.Messages[1].Type);
        Assert.Equal("user", result.Messages[2].Type);
        Assert.Equal(summary, result.Messages[3]);
        Assert.Single(result.FoldedMessages);
        Assert.Equal(3, result.PreservedMessages.Count);
    }

    private static IReadOnlyList<ConversationMessage> CreateAtomicThread() =>
        [
            CompactionTestHelpers.UserText("prefix"),
            CompactionTestHelpers.AssistantToolUse("tool-1", "read", new { path = "a.txt" }),
            CompactionTestHelpers.UserToolResult("tool-1", "done"),
            CompactionTestHelpers.UserText("tail"),
        ];
}

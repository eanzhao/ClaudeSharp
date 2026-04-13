using Aexon.Core.Compaction;
using Aexon.Core.Messages;

namespace Aexon.Core.Tests.Compaction;

/// <summary>
/// Contains tests for conversation Compactor.
/// </summary>
public sealed class ConversationCompactorTests
{
    [Fact]
    public void Compact_ReturnsNullWhenHistoryAlreadyFits()
    {
        var compactor = new HeuristicConversationCompactor();

        var result = compactor.Compact(
            [
                CompactionTestHelpers.UserText("one"),
                CompactionTestHelpers.AssistantText("two"),
            ],
            preserveTailCount: 2);

        Assert.Null(result);
    }

    [Fact]
    public void Compact_ProducesMetaSummaryForOlderPrefix()
    {
        var compactor = new HeuristicConversationCompactor();

        var result = compactor.Compact(
            [
                CompactionTestHelpers.UserText("one"),
                CompactionTestHelpers.AssistantText("two"),
                CompactionTestHelpers.UserText("three"),
                CompactionTestHelpers.AssistantText("four"),
            ],
            preserveTailCount: 2);

        Assert.NotNull(result);
        Assert.Equal(2, result!.RemovedMessageCount);
        Assert.Equal(3, result.ActiveMessages.Count);
        var summary = Assert.IsType<UserMessage>(result.SummaryMessage);
        Assert.True(summary.IsMeta);
        Assert.Contains("Conversation summary before compaction", Assert.IsType<TextBlock>(summary.Content[0]).Text);
        Assert.Contains("earlier messages", Assert.IsType<TextBlock>(summary.Content[0]).Text);
        Assert.Equal(summary, result.ActiveMessages[0]);
    }

    [Fact]
    public void CompactFrom_SummarizesLaterMessagesAndKeepsPrefix()
    {
        var compactor = new HeuristicConversationCompactor();

        var result = compactor.CompactFrom(
            [
                CompactionTestHelpers.UserText("one"),
                CompactionTestHelpers.AssistantText("two"),
                CompactionTestHelpers.UserText("three"),
                CompactionTestHelpers.AssistantText("four"),
            ],
            fromIndex: 2);

        Assert.NotNull(result);
        Assert.Equal(2, result!.RemovedMessageCount);
        Assert.Equal(3, result.ActiveMessages.Count);
        var summary = Assert.IsType<UserMessage>(result.SummaryMessage);
        var text = Assert.IsType<TextBlock>(summary.Content[0]).Text;
        Assert.Contains("later messages", text);
        Assert.Contains("earliest 2 messages", text);
        Assert.Equal(summary, result.ActiveMessages[^1]);
    }

    [Fact]
    public void CompactUpTo_AdjustsToolBoundaryAndKeepsTail()
    {
        var compactor = new HeuristicConversationCompactor();

        var result = compactor.CompactUpTo(
            [
                CompactionTestHelpers.UserText("prefix"),
                CompactionTestHelpers.AssistantToolUse("tool-1", "read", new { path = "a.txt" }),
                CompactionTestHelpers.UserToolResult("tool-1", "done"),
                CompactionTestHelpers.UserText("tail"),
            ],
            upToIndex: 2);

        Assert.NotNull(result);
        Assert.Equal(1, result.RemovedMessageCount);
        Assert.Equal(4, result.ActiveMessages.Count);
        var summary = Assert.IsType<UserMessage>(result.SummaryMessage);
        Assert.Contains("earlier messages", Assert.IsType<TextBlock>(summary.Content[0]).Text);
        Assert.Equal(summary, result.ActiveMessages[0]);
    }
}

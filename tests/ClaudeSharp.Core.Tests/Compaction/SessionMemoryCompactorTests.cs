using ClaudeSharp.Core.Compaction;
using ClaudeSharp.Core.Messages;

namespace ClaudeSharp.Core.Tests.Compaction;

/// <summary>
/// Contains tests for session Memory Compactor.
/// </summary>
public sealed class SessionMemoryCompactorTests
{
    [Fact]
    public void Compact_ReturnsNullWhenPreserveTailConsumesAllHistory()
    {
        var compactor = new SessionMemoryCompactor();

        var result = compactor.Compact(
            [
                CompactionTestHelpers.UserText("one"),
                CompactionTestHelpers.AssistantText("two"),
            ],
            new SessionMemoryCompactionOptions
            {
                PreserveTailCount = 2,
            });

        Assert.Null(result);
    }

    [Fact]
    public void Compact_ReturnsNullWhenFoldedSpanIsSmallerThanMinimum()
    {
        var compactor = new SessionMemoryCompactor();

        var result = compactor.Compact(
            [
                CompactionTestHelpers.UserText("one"),
                CompactionTestHelpers.AssistantText("two"),
                CompactionTestHelpers.UserText("three"),
            ],
            new SessionMemoryCompactionOptions
            {
                PreserveTailCount = 1,
                MinimumFoldedMessageCount = 4,
            });

        Assert.Null(result);
    }

    [Fact]
    public void Compact_FoldsOlderMessagesIntoMetaSummary()
    {
        var compactor = new SessionMemoryCompactor();

        var result = compactor.Compact(
            [
                CompactionTestHelpers.UserText("one"),
                CompactionTestHelpers.AssistantText("two"),
                CompactionTestHelpers.UserText("three"),
                CompactionTestHelpers.AssistantText("four"),
                CompactionTestHelpers.UserText("five"),
            ],
            new SessionMemoryCompactionOptions
            {
                PreserveTailCount = 2,
                MinimumFoldedMessageCount = 1,
            });

        Assert.NotNull(result);
        Assert.Equal(3, result!.FoldedMessageCount);
        Assert.Equal(2, result.RequestedPreserveTailCount);
        Assert.Equal(2, result.EffectivePreserveTailCount);
        Assert.Equal(3, result.ActiveMessages.Count);
        var memoryMessage = Assert.IsType<UserMessage>(result.MemoryMessage);
        Assert.True(memoryMessage.IsMeta);
        var text = Assert.IsType<TextBlock>(memoryMessage.Content[0]).Text;
        Assert.Contains("Folded 3 earlier messages.", text);
        Assert.Contains("Kept the latest 2 messages verbatim.", text);
        Assert.Equal(memoryMessage, result.ActiveMessages[0]);
    }

    [Fact]
    public void Compact_AdjustsBoundaryToKeepToolPairTogether()
    {
        var compactor = new SessionMemoryCompactor();

        var result = compactor.Compact(
            [
                CompactionTestHelpers.UserText("prefix"),
                CompactionTestHelpers.AssistantToolUse("tool-1", "read", new { path = "a.txt" }),
                CompactionTestHelpers.UserToolResult("tool-1", "done"),
                CompactionTestHelpers.AssistantText("tail-1"),
                CompactionTestHelpers.UserText("tail-2"),
            ],
            new SessionMemoryCompactionOptions
            {
                PreserveTailCount = 3,
                MinimumFoldedMessageCount = 1,
            });

        Assert.NotNull(result);
        Assert.Equal(1, result.FoldedMessageCount);
        Assert.Equal(3, result.RequestedPreserveTailCount);
        Assert.Equal(4, result.EffectivePreserveTailCount);
        Assert.Equal(5, result.ActiveMessages.Count);
        var text = Assert.IsType<TextBlock>(Assert.IsType<UserMessage>(result.MemoryMessage).Content[0]).Text;
        Assert.Contains("Folded 1 earlier messages.", text);
        Assert.Contains("Kept the latest 4 messages verbatim.", text);
    }
}

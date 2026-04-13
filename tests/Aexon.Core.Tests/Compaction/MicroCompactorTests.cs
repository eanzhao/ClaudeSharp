using Aexon.Core.Compaction;
using Aexon.Core.Messages;

namespace Aexon.Core.Tests.Compaction;

/// <summary>
/// Contains tests for micro Compactor.
/// </summary>
public sealed class MicroCompactorTests
{
    [Fact]
    public void Run_ReturnsOriginalMessagesWhenCooldownHasNotElapsed()
    {
        var compactor = new TimeBasedMicroCompactor();
        var recentAssistant = CompactionTestHelpers.AssistantText("recent");
        recentAssistant = recentAssistant with
        {
            Timestamp = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(5),
        };
        var tail = CompactionTestHelpers.UserText("tail");

        var result = compactor.Run(
            [recentAssistant, tail],
            new MicrocompactRunOptions
            {
                PreserveTailCount = 1,
                Force = false,
                CacheCooldown = TimeSpan.FromHours(1),
            },
            DateTimeOffset.UtcNow);

        Assert.False(result.HasChanges);
        Assert.Empty(result.Edits);
        Assert.Same(recentAssistant, result.UpdatedMessages[0]);
        Assert.Same(tail, result.UpdatedMessages[1]);
    }

    [Fact]
    public void Run_ClearsOldToolResultAndThinkingWhenForced()
    {
        var compactor = new TimeBasedMicroCompactor();
        var oldToolResult = CompactionTestHelpers.UserToolResult("tool-1", "original");
        oldToolResult = oldToolResult with
        {
            Timestamp = DateTimeOffset.UtcNow - TimeSpan.FromHours(2),
        };

        var oldAssistant = CompactionTestHelpers.AssistantThinking("thinking trace", "sig-1");
        oldAssistant = oldAssistant with
        {
            Timestamp = DateTimeOffset.UtcNow - TimeSpan.FromHours(2),
        };

        var tail = CompactionTestHelpers.UserText("tail");

        var result = compactor.Run(
            [oldToolResult, oldAssistant, tail],
            new MicrocompactRunOptions
            {
                PreserveTailCount = 1,
                Force = true,
                ClearThinkingBlocks = true,
            });

        Assert.True(result.HasChanges);
        Assert.Equal(1, result.ClearedToolResultCount);
        Assert.Equal(1, result.ClearedThinkingBlockCount);

        var rewrittenToolResult = Assert.IsType<UserMessage>(result.UpdatedMessages[0]);
        var rewrittenAssistant = Assert.IsType<AssistantMessage>(result.UpdatedMessages[1]);

        Assert.Equal(MicrocompactPlaceholders.OldToolResult, rewrittenToolResult.ToolUseResult);
        Assert.Equal(MicrocompactPlaceholders.OldToolResult, Assert.IsType<ToolResultBlock>(rewrittenToolResult.Content[0]).Content);
        Assert.Equal(MicrocompactPlaceholders.OldThinking, Assert.IsType<ThinkingBlock>(rewrittenAssistant.Content[0]).Text);
        Assert.Same(tail, result.UpdatedMessages[2]);
    }

    [Fact]
    public void Run_LeavesThinkingUntouchedWhenDisabled()
    {
        var compactor = new TimeBasedMicroCompactor();
        var oldToolResult = CompactionTestHelpers.UserToolResult("tool-1", "original");
        oldToolResult = oldToolResult with
        {
            Timestamp = DateTimeOffset.UtcNow - TimeSpan.FromHours(2),
        };

        var oldAssistant = CompactionTestHelpers.AssistantThinking("thinking trace", "sig-1");
        oldAssistant = oldAssistant with
        {
            Timestamp = DateTimeOffset.UtcNow - TimeSpan.FromHours(2),
        };

        var tail = CompactionTestHelpers.UserText("tail");

        var result = compactor.Run(
            [oldToolResult, oldAssistant, tail],
            new MicrocompactRunOptions
            {
                PreserveTailCount = 1,
                Force = true,
                ClearThinkingBlocks = false,
            });

        Assert.True(result.HasChanges);
        Assert.Equal(1, result.ClearedToolResultCount);
        Assert.Equal(0, result.ClearedThinkingBlockCount);

        var rewrittenToolResult = Assert.IsType<UserMessage>(result.UpdatedMessages[0]);
        Assert.Equal(MicrocompactPlaceholders.OldToolResult, rewrittenToolResult.ToolUseResult);
        Assert.Equal(MicrocompactPlaceholders.OldToolResult, Assert.IsType<ToolResultBlock>(rewrittenToolResult.Content[0]).Content);
        Assert.Same(oldAssistant, result.UpdatedMessages[1]);
    }
}

using Aexon.Core.Compaction;
using Aexon.Core.Messages;

namespace Aexon.Core.Tests.Compaction;

/// <summary>
/// Contains tests for session Memory Compactor Deep.
/// </summary>
public sealed class SessionMemoryCompactorDeepTests
{
    [Fact]
    public void Compact_UsesFallbackLineWhenFoldedSpanHasNoTextualHighlights()
    {
        var compactor = new SessionMemoryCompactor();

        var result = compactor.Compact(
            [
                new UserMessage { Content = [] },
                new AssistantMessage { Content = [] },
                CompactionTestHelpers.UserText("tail"),
            ],
            new SessionMemoryCompactionOptions
            {
                PreserveTailCount = 1,
                MinimumFoldedMessageCount = 1,
            });

        Assert.NotNull(result);
        Assert.Contains(
            "no textual content",
            result!.SummaryText);
    }

    [Fact]
    public void Compact_SummarizesMetaToolResultToolUseThinkingAndTrimmedText()
    {
        var compactor = new SessionMemoryCompactor();

        var result = compactor.Compact(
            [
                new UserMessage
                {
                    IsMeta = true,
                    Content =
                    [
                        new TextBlock("line one\nline two\tline three"),
                        new ToolResultBlock("tool-1", "tool result payload"),
                    ],
                },
                new AssistantMessage
                {
                    Content =
                    [
                        new ToolUseBlock
                        {
                            ToolUseId = "tool-1",
                            Name = "read_file",
                            Input = System.Text.Json.JsonSerializer.SerializeToElement(new { path = "a.txt" }),
                        },
                        new ThinkingBlock("very long internal reasoning"),
                    ],
                },
                CompactionTestHelpers.UserText("tail"),
            ],
            new SessionMemoryCompactionOptions
            {
                PreserveTailCount = 1,
                MinimumFoldedMessageCount = 1,
                PreviewCharactersPerMessage = 8,
            });

        Assert.NotNull(result);
        Assert.Contains("meta message", result!.SummaryText);
        Assert.Contains("tool_use:read_file", result.SummaryText);
        Assert.Contains("thinking:very ", result.SummaryText);
        Assert.Contains("line ...", result.SummaryText);
    }

    [Fact]
    public void Compact_AddsOmissionLineWhenPreviewOrBudgetIsExceeded()
    {
        var compactor = new SessionMemoryCompactor();

        var result = compactor.Compact(
            [
                CompactionTestHelpers.UserText(new string('a', 60)),
                CompactionTestHelpers.AssistantText(new string('b', 60)),
                CompactionTestHelpers.UserText("tail"),
            ],
            new SessionMemoryCompactionOptions
            {
                PreserveTailCount = 1,
                MinimumFoldedMessageCount = 1,
                MaximumSummaryCharacters = 140,
                PreviewCharactersPerMessage = 20,
            });

        Assert.NotNull(result);
        Assert.Contains(
            "Additional earlier details were omitted to keep the memory compact.",
            result!.SummaryText);
    }
}

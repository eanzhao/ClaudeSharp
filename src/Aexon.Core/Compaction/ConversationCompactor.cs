using System.Text;
using Aexon.Core.Messages;

namespace Aexon.Core.Compaction;

/// <summary>
/// Represents conversation compaction result.
/// </summary>
public sealed class ConversationCompactionResult
{
    public required ConversationMessage SummaryMessage { get; init; }
    public required IReadOnlyList<ConversationMessage> ActiveMessages { get; init; }
    public required int RemovedMessageCount { get; init; }
    public ConversationRewriteResult? RewriteResult { get; init; }
}

/// <summary>
/// Defines the contract for conversation compactor.
/// </summary>
public interface IConversationCompactor
{
    ConversationCompactionResult? Compact(
        IReadOnlyList<ConversationMessage> messages,
        int preserveTailCount = 8);

    ConversationCompactionResult? CompactUpTo(
        IReadOnlyList<ConversationMessage> messages,
        int upToIndex);

    ConversationCompactionResult? CompactFrom(
        IReadOnlyList<ConversationMessage> messages,
        int fromIndex);
}

/// <summary>
/// Provides heuristic conversation compactor.
/// </summary>
public sealed class HeuristicConversationCompactor : IConversationCompactor
{
    private const int MaxSummaryChars = 4000;
    private const int PerMessagePreviewChars = 180;
    private readonly IConversationRewriter _rewriter;

    public HeuristicConversationCompactor(IConversationRewriter? rewriter = null)
    {
        _rewriter = rewriter ?? new ConversationRewriter();
    }

    public ConversationCompactionResult? Compact(
        IReadOnlyList<ConversationMessage> messages,
        int preserveTailCount = 8)
    {
        preserveTailCount = Math.Max(2, preserveTailCount);
        if (messages.Count <= preserveTailCount)
            return null;

        return CompactUpTo(messages, messages.Count - preserveTailCount);
    }

    public ConversationCompactionResult? CompactUpTo(
        IReadOnlyList<ConversationMessage> messages,
        int upToIndex)
    {
        var boundary = _rewriter.ResolveUpToBoundary(messages, upToIndex);
        return Compact(messages, boundary);
    }

    public ConversationCompactionResult? CompactFrom(
        IReadOnlyList<ConversationMessage> messages,
        int fromIndex)
    {
        var boundary = _rewriter.ResolveFromBoundary(messages, fromIndex);
        return Compact(messages, boundary);
    }

    private ConversationCompactionResult? Compact(
        IReadOnlyList<ConversationMessage> messages,
        ConversationRewriteBoundary boundary)
    {
        if (boundary.FoldedMessageCount == 0)
            return null;

        var foldedMessages = messages
            .Skip(boundary.FoldedStartIndex)
            .Take(boundary.FoldedMessageCount)
            .ToArray();
        if (foldedMessages.Length == 0)
            return null;

        var summaryMessage = new UserMessage
        {
            IsMeta = true,
            Content =
            [
                new TextBlock(BuildSummary(
                    foldedMessages,
                    boundary.PreservedMessageCount,
                    boundary.Direction)),
            ],
        };

        var rewriteResult = _rewriter.Rewrite(messages, boundary, summaryMessage);

        if (!rewriteResult.HasChanges)
            return null;

        return new ConversationCompactionResult
        {
            SummaryMessage = summaryMessage,
            ActiveMessages = rewriteResult.Messages,
            RemovedMessageCount = rewriteResult.FoldedMessages.Count,
            RewriteResult = rewriteResult,
        };
    }

    private static string BuildSummary(
        IReadOnlyList<ConversationMessage> removedMessages,
        int preservedCount,
        ConversationRewriteDirection direction)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Conversation summary before compaction:");
        builder.AppendLine(direction == ConversationRewriteDirection.UpTo
            ? $"- Compressed {removedMessages.Count} earlier messages and kept the latest {preservedCount} messages in full."
            : $"- Compressed {removedMessages.Count} later messages and kept the earliest {preservedCount} messages in full.");
        builder.AppendLine("- Key checkpoints:");

        foreach (var message in removedMessages)
        {
            var line = $"- {DescribeMessage(message)}";
            if (builder.Length + line.Length + Environment.NewLine.Length > MaxSummaryChars)
            {
                builder.AppendLine("- Additional earlier details were omitted to keep the checkpoint compact.");
                break;
            }

            builder.AppendLine(line);
        }

        return builder.ToString().TrimEnd();
    }

    private static string DescribeMessage(ConversationMessage message) =>
        message switch
        {
            TombstoneMessage tombstone => $"[deleted: {tombstone.DeletedMessageId}]",
            UserMessage user => $"User: {SummarizeUserMessage(user)}",
            AssistantMessage assistant => $"Assistant: {SummarizeAssistantMessage(assistant)}",
            SystemMessage system => $"System: {Trim(system.Content)}",
            _ => $"Message<{message.Type}>",
        };

    private static string SummarizeUserMessage(UserMessage message)
    {
        var segments = new List<string>();
        foreach (var block in message.Content)
        {
            switch (block)
            {
                case TextBlock text when !string.IsNullOrWhiteSpace(text.Text):
                    segments.Add(Trim(text.Text));
                    break;

                case ToolResultBlock result:
                    segments.Add(
                        $"tool_result:{result.ToolUseId} {Trim(result.Content)}");
                    break;
            }

            if (segments.Count >= 2)
                break;
        }

        if (segments.Count == 0)
            segments.Add(message.IsMeta ? "meta message" : "no textual content");

        return string.Join(" | ", segments);
    }

    private static string SummarizeAssistantMessage(AssistantMessage message)
    {
        var segments = new List<string>();
        foreach (var block in message.Content)
        {
            switch (block)
            {
                case TextBlock text when !string.IsNullOrWhiteSpace(text.Text):
                    segments.Add(Trim(text.Text));
                    break;

                case ToolUseBlock toolUse:
                    segments.Add($"tool_use:{toolUse.Name}");
                    break;

                case ThinkingBlock thinking when !string.IsNullOrWhiteSpace(thinking.Text):
                    segments.Add($"thinking:{Trim(thinking.Text)}");
                    break;
            }

            if (segments.Count >= 2)
                break;
        }

        if (segments.Count == 0)
            segments.Add("no textual content");

        return string.Join(" | ", segments);
    }

    private static string Trim(string value)
    {
        var collapsed = string.Join(
            ' ',
            value.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries));

        if (collapsed.Length <= PerMessagePreviewChars)
            return collapsed;

        return $"{collapsed[..(PerMessagePreviewChars - 3)]}...";
    }
}

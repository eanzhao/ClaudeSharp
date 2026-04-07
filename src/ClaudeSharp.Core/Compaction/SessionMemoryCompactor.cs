using System.Text;
using ClaudeSharp.Core.Messages;

namespace ClaudeSharp.Core.Compaction;

public sealed class SessionMemoryCompactionOptions
{
    public int PreserveTailCount { get; init; } = 8;
    public int MinimumFoldedMessageCount { get; init; } = 4;
    public int MaximumSummaryCharacters { get; init; } = 3200;
    public int MaximumPreviewMessages { get; init; } = 10;
    public int PreviewCharactersPerMessage { get; init; } = 180;
    public string SummaryHeading { get; init; } = "Session memory summary before compaction:";
    public string TailNote { get; init; } = "Recent original messages remain verbatim.";
}

public sealed class SessionMemoryCompactionResult
{
    public required ConversationRewriteResult RewriteResult { get; init; }
    public required ConversationMessage MemoryMessage { get; init; }
    public required IReadOnlyList<ConversationMessage> FoldedMessages { get; init; }
    public required IReadOnlyList<ConversationMessage> ActiveMessages { get; init; }
    public required string SummaryText { get; init; }
    public required int RequestedPreserveTailCount { get; init; }
    public required int EffectivePreserveTailCount { get; init; }

    public int FoldedMessageCount => FoldedMessages.Count;
    public bool HasChanges => RewriteResult.HasChanges;
}

public interface ISessionMemoryCompactor
{
    SessionMemoryCompactionResult? Compact(
        IReadOnlyList<ConversationMessage> messages,
        SessionMemoryCompactionOptions? options = null);
}

public sealed class SessionMemoryCompactor : ISessionMemoryCompactor
{
    private readonly IConversationRewriter _rewriter;

    public SessionMemoryCompactor(IConversationRewriter? rewriter = null)
    {
        _rewriter = rewriter ?? new ConversationRewriter();
    }

    public SessionMemoryCompactionResult? Compact(
        IReadOnlyList<ConversationMessage> messages,
        SessionMemoryCompactionOptions? options = null)
    {
        options ??= new SessionMemoryCompactionOptions();

        if (messages.Count == 0)
            return null;

        var requestedUpToIndex = messages.Count - Math.Max(0, options.PreserveTailCount);
        if (requestedUpToIndex <= 0)
            return null;

        var boundary = _rewriter.ResolveUpToBoundary(messages, requestedUpToIndex);
        if (boundary.FoldedMessageCount < options.MinimumFoldedMessageCount)
            return null;

        var foldedMessages = messages.Take(boundary.FoldedMessageCount).ToArray();
        var summaryText = BuildSummaryText(foldedMessages, boundary, options);
        if (string.IsNullOrWhiteSpace(summaryText))
            return null;

        var memoryMessage = new UserMessage
        {
            IsMeta = true,
            Content = [new TextBlock(summaryText)],
        };

        var rewriteResult = _rewriter.Rewrite(messages, boundary, memoryMessage);
        if (!rewriteResult.HasChanges)
            return null;

        return new SessionMemoryCompactionResult
        {
            RewriteResult = rewriteResult,
            MemoryMessage = memoryMessage,
            FoldedMessages = rewriteResult.FoldedMessages,
            ActiveMessages = rewriteResult.Messages,
            SummaryText = summaryText,
            RequestedPreserveTailCount = Math.Max(0, options.PreserveTailCount),
            EffectivePreserveTailCount = rewriteResult.PreservedMessages.Count,
        };
    }

    private static string BuildSummaryText(
        IReadOnlyList<ConversationMessage> foldedMessages,
        ConversationRewriteBoundary boundary,
        SessionMemoryCompactionOptions options)
    {
        var builder = new StringBuilder();
        builder.AppendLine(options.SummaryHeading);
        builder.AppendLine($"- Folded {foldedMessages.Count} earlier messages.");
        builder.AppendLine($"- Kept the latest {boundary.PreservedMessageCount} messages verbatim.");
        builder.AppendLine("- Earlier context highlights:");

        var previewCount = 0;
        foreach (var message in foldedMessages)
        {
            if (previewCount >= options.MaximumPreviewMessages)
            {
                builder.AppendLine("- Additional earlier details were omitted to keep the memory compact.");
                break;
            }

            var line = $"- {DescribeMessage(message, options.PreviewCharactersPerMessage)}";
            if (builder.Length + line.Length + Environment.NewLine.Length > options.MaximumSummaryCharacters)
            {
                builder.AppendLine("- Additional earlier details were omitted to keep the memory compact.");
                break;
            }

            builder.AppendLine(line);
            previewCount++;
        }

        if (previewCount == 0)
            builder.AppendLine("- No textual highlights were available in the folded span.");

        builder.AppendLine(options.TailNote);
        return builder.ToString().TrimEnd();
    }

    private static string DescribeMessage(ConversationMessage message, int maxCharacters) =>
        message switch
        {
            UserMessage user => $"User: {SummarizeUserMessage(user, maxCharacters)}",
            AssistantMessage assistant => $"Assistant: {SummarizeAssistantMessage(assistant, maxCharacters)}",
            SystemMessage system => $"System: {Trim(system.Content, maxCharacters)}",
            _ => $"Message<{message.Type}>",
        };

    private static string SummarizeUserMessage(UserMessage message, int maxCharacters)
    {
        var segments = new List<string>();

        if (message.IsMeta)
            segments.Add("meta message");

        foreach (var block in message.Content)
        {
            switch (block)
            {
                case TextBlock text when !string.IsNullOrWhiteSpace(text.Text):
                    segments.Add(Trim(text.Text, maxCharacters));
                    break;

                case ToolResultBlock result:
                    segments.Add($"tool_result:{result.ToolUseId} {Trim(result.Content, maxCharacters)}");
                    break;
            }

            if (segments.Count >= 2)
                break;
        }

        if (segments.Count == 0)
            segments.Add("no textual content");

        return string.Join(" | ", segments);
    }

    private static string SummarizeAssistantMessage(AssistantMessage message, int maxCharacters)
    {
        var segments = new List<string>();

        foreach (var block in message.Content)
        {
            switch (block)
            {
                case TextBlock text when !string.IsNullOrWhiteSpace(text.Text):
                    segments.Add(Trim(text.Text, maxCharacters));
                    break;

                case ToolUseBlock toolUse:
                    segments.Add($"tool_use:{toolUse.Name}");
                    break;

                case ThinkingBlock thinking when !string.IsNullOrWhiteSpace(thinking.Text):
                    segments.Add($"thinking:{Trim(thinking.Text, maxCharacters)}");
                    break;
            }

            if (segments.Count >= 2)
                break;
        }

        if (segments.Count == 0)
            segments.Add("no textual content");

        return string.Join(" | ", segments);
    }

    private static string Trim(string value, int maxCharacters)
    {
        var collapsed = string.Join(
            ' ',
            value.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries));

        if (collapsed.Length <= maxCharacters)
            return collapsed;

        if (maxCharacters <= 3)
            return collapsed[..Math.Min(collapsed.Length, maxCharacters)];

        return $"{collapsed[..(maxCharacters - 3)]}...";
    }
}

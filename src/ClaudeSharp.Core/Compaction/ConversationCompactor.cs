using System.Text;
using ClaudeSharp.Core.Messages;

namespace ClaudeSharp.Core.Compaction;

public sealed class ConversationCompactionResult
{
    public required ConversationMessage SummaryMessage { get; init; }
    public required IReadOnlyList<ConversationMessage> ActiveMessages { get; init; }
    public required int RemovedMessageCount { get; init; }
}

public interface IConversationCompactor
{
    ConversationCompactionResult? Compact(
        IReadOnlyList<ConversationMessage> messages,
        int preserveTailCount = 8);
}

public sealed class HeuristicConversationCompactor : IConversationCompactor
{
    private const int MaxSummaryChars = 4000;
    private const int PerMessagePreviewChars = 180;

    public ConversationCompactionResult? Compact(
        IReadOnlyList<ConversationMessage> messages,
        int preserveTailCount = 8)
    {
        preserveTailCount = Math.Max(2, preserveTailCount);
        if (messages.Count <= preserveTailCount)
            return null;

        var removedMessages = messages.Take(messages.Count - preserveTailCount).ToList();
        var preservedMessages = messages.Skip(messages.Count - preserveTailCount).ToList();
        if (removedMessages.Count == 0)
            return null;

        var summaryMessage = new UserMessage
        {
            IsMeta = true,
            Content = [new TextBlock(BuildSummary(removedMessages, preservedMessages.Count))],
        };

        var activeMessages = new List<ConversationMessage>(preservedMessages.Count + 1)
        {
            summaryMessage,
        };
        activeMessages.AddRange(preservedMessages);

        return new ConversationCompactionResult
        {
            SummaryMessage = summaryMessage,
            ActiveMessages = activeMessages,
            RemovedMessageCount = removedMessages.Count,
        };
    }

    private static string BuildSummary(
        IReadOnlyList<ConversationMessage> removedMessages,
        int preservedCount)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Conversation summary before compaction:");
        builder.AppendLine(
            $"- Compressed {removedMessages.Count} earlier messages and kept the latest {preservedCount} messages in full.");
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

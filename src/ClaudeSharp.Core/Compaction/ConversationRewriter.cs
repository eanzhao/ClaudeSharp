using ClaudeSharp.Core.Messages;

namespace ClaudeSharp.Core.Compaction;

public interface IConversationRewriter
{
    ConversationRewriteBoundary ResolveFromBoundary(
        IReadOnlyList<ConversationMessage> messages,
        int fromIndex);

    ConversationRewriteBoundary ResolveUpToBoundary(
        IReadOnlyList<ConversationMessage> messages,
        int upToIndex);

    ConversationRewriteResult RewriteFrom(
        IReadOnlyList<ConversationMessage> messages,
        int fromIndex,
        ConversationMessage summaryMessage);

    ConversationRewriteResult RewriteUpTo(
        IReadOnlyList<ConversationMessage> messages,
        int upToIndex,
        ConversationMessage summaryMessage);

    ConversationRewriteResult Rewrite(
        IReadOnlyList<ConversationMessage> messages,
        ConversationRewriteBoundary boundary,
        ConversationMessage summaryMessage);
}

public sealed class ConversationRewriter : IConversationRewriter
{
    public ConversationRewriteBoundary ResolveFromBoundary(
        IReadOnlyList<ConversationMessage> messages,
        int fromIndex) =>
        ResolveBoundary(messages, fromIndex, ConversationRewriteDirection.From);

    public ConversationRewriteBoundary ResolveUpToBoundary(
        IReadOnlyList<ConversationMessage> messages,
        int upToIndex) =>
        ResolveBoundary(messages, upToIndex, ConversationRewriteDirection.UpTo);

    public ConversationRewriteResult RewriteFrom(
        IReadOnlyList<ConversationMessage> messages,
        int fromIndex,
        ConversationMessage summaryMessage) =>
        Rewrite(messages, ResolveFromBoundary(messages, fromIndex), summaryMessage);

    public ConversationRewriteResult RewriteUpTo(
        IReadOnlyList<ConversationMessage> messages,
        int upToIndex,
        ConversationMessage summaryMessage) =>
        RewriteCore(messages, ResolveUpToBoundary(messages, upToIndex), summaryMessage);

    public ConversationRewriteResult Rewrite(
        IReadOnlyList<ConversationMessage> messages,
        ConversationRewriteBoundary boundary,
        ConversationMessage summaryMessage) =>
        RewriteCore(messages, boundary, summaryMessage);

    private static ConversationRewriteResult RewriteCore(
        IReadOnlyList<ConversationMessage> messages,
        ConversationRewriteBoundary boundary,
        ConversationMessage summaryMessage)
    {
        if (boundary.FoldedMessageCount == 0)
        {
            return new ConversationRewriteResult
            {
                Boundary = boundary,
                SummaryMessage = summaryMessage,
                Messages = messages.ToArray(),
                FoldedMessages = Array.Empty<ConversationMessage>(),
                PreservedMessages = messages.ToArray(),
            };
        }

        var foldedMessages = boundary.Direction == ConversationRewriteDirection.From
            ? messages.Skip(boundary.FoldedStartIndex).Take(boundary.FoldedMessageCount).ToArray()
            : messages.Take(boundary.FoldedMessageCount).ToArray();

        var preservedMessages = boundary.Direction == ConversationRewriteDirection.From
            ? messages.Take(boundary.FoldedStartIndex).ToArray()
            : messages.Skip(boundary.FoldedEndIndexExclusive).ToArray();

        var rewritten = new List<ConversationMessage>(messages.Count + 1);
        if (boundary.Direction == ConversationRewriteDirection.From)
        {
            rewritten.AddRange(preservedMessages);
            rewritten.Add(summaryMessage);
        }
        else
        {
            rewritten.Add(summaryMessage);
            rewritten.AddRange(preservedMessages);
        }

        return new ConversationRewriteResult
        {
            Boundary = boundary,
            SummaryMessage = summaryMessage,
            Messages = rewritten,
            FoldedMessages = foldedMessages,
            PreservedMessages = preservedMessages,
        };
    }

    private static ConversationRewriteBoundary ResolveBoundary(
        IReadOnlyList<ConversationMessage> messages,
        int requestedIndex,
        ConversationRewriteDirection direction)
    {
        var messageCount = messages.Count;
        var clampedIndex = Math.Clamp(requestedIndex, 0, messageCount);
        var appliedIndex = clampedIndex;

        foreach (var span in BuildAtomicSpans(messages))
        {
            if (clampedIndex <= span.StartIndex || clampedIndex >= span.EndIndexExclusive)
                continue;

            appliedIndex = direction == ConversationRewriteDirection.From
                ? span.EndIndexExclusive
                : span.StartIndex;
            break;
        }

        var foldedStartIndex = direction == ConversationRewriteDirection.From
            ? appliedIndex
            : 0;

        var foldedEndIndexExclusive = direction == ConversationRewriteDirection.From
            ? messageCount
            : appliedIndex;

        return new ConversationRewriteBoundary
        {
            Direction = direction,
            RequestedIndex = requestedIndex,
            AppliedIndex = appliedIndex,
            MessageCount = messageCount,
            FoldedStartIndex = foldedStartIndex,
            FoldedEndIndexExclusive = foldedEndIndexExclusive,
        };
    }

    private static List<MessageSpan> BuildAtomicSpans(IReadOnlyList<ConversationMessage> messages)
    {
        var spans = new List<MessageSpan>(messages.Count);

        for (var index = 0; index < messages.Count;)
        {
            if (messages[index] is AssistantMessage assistant && HasToolUse(assistant))
            {
                var startIndex = index;
                index++;

                while (index < messages.Count &&
                       messages[index] is UserMessage user &&
                       HasToolResult(user))
                {
                    index++;
                }

                spans.Add(new MessageSpan(startIndex, index));
                continue;
            }

            spans.Add(new MessageSpan(index, index + 1));
            index++;
        }

        return spans;
    }

    private static bool HasToolUse(AssistantMessage message) =>
        message.Content.OfType<ToolUseBlock>().Any();

    private static bool HasToolResult(UserMessage message) =>
        message.Content.OfType<ToolResultBlock>().Any();

    private readonly record struct MessageSpan(int StartIndex, int EndIndexExclusive);
}

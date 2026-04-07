using ClaudeSharp.Core.Messages;

namespace ClaudeSharp.Core.Compaction;

/// <summary>
/// Defines conversation rewrite direction values.
/// </summary>
public enum ConversationRewriteDirection
{
    From,
    UpTo,
}

/// <summary>
/// Represents conversation rewrite boundary.
/// </summary>
public sealed record ConversationRewriteBoundary
{
    public required ConversationRewriteDirection Direction { get; init; }
    public required int RequestedIndex { get; init; }
    public required int AppliedIndex { get; init; }
    public required int MessageCount { get; init; }
    public required int FoldedStartIndex { get; init; }
    public required int FoldedEndIndexExclusive { get; init; }

    public bool WasAdjusted => RequestedIndex != AppliedIndex;
    public int FoldedMessageCount => Math.Max(0, FoldedEndIndexExclusive - FoldedStartIndex);
    public int PreservedMessageCount => Math.Max(0, MessageCount - FoldedMessageCount);
}

/// <summary>
/// Represents conversation rewrite result.
/// </summary>
public sealed record ConversationRewriteResult
{
    public required ConversationRewriteBoundary Boundary { get; init; }
    public required ConversationMessage SummaryMessage { get; init; }
    public required IReadOnlyList<ConversationMessage> Messages { get; init; }
    public required IReadOnlyList<ConversationMessage> FoldedMessages { get; init; }
    public required IReadOnlyList<ConversationMessage> PreservedMessages { get; init; }

    public bool HasChanges => FoldedMessages.Count > 0;
}

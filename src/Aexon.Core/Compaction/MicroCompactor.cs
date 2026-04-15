using Aexon.Core.Messages;

namespace Aexon.Core.Compaction;

/// <summary>
/// Represents microcompact edit.
/// </summary>
public sealed class MicrocompactEdit
{
    public required string MessageId { get; init; }
    public bool ClearToolResult { get; init; }
    public bool ClearThinking { get; init; }
}

/// <summary>
/// Represents options for microcompact run.
/// </summary>
public sealed class MicrocompactRunOptions
{
    public int PreserveTailCount { get; init; } = 8;
    public bool Force { get; init; }
    public TimeSpan CacheCooldown { get; init; } = TimeSpan.FromHours(1);
    public bool ClearThinkingBlocks { get; init; } = true;
}

/// <summary>
/// Represents microcompact result.
/// </summary>
public sealed class MicrocompactResult
{
    public required IReadOnlyList<ConversationMessage> UpdatedMessages { get; init; }
    public required IReadOnlyList<MicrocompactEdit> Edits { get; init; }
    public required int ClearedToolResultCount { get; init; }
    public required int ClearedThinkingBlockCount { get; init; }

    public bool HasChanges => Edits.Count > 0;
}

/// <summary>
/// Defines the contract for micro compactor.
/// </summary>
public interface IMicroCompactor
{
    MicrocompactResult Run(
        IReadOnlyList<ConversationMessage> messages,
        MicrocompactRunOptions? options = null,
        DateTimeOffset? now = null);
}

/// <summary>
/// Represents microcompact placeholders.
/// </summary>
public static class MicrocompactPlaceholders
{
    public const string OldToolResult = "[Old tool result content cleared]";
    public const string OldThinking = "[Old thinking content cleared]";
}

/// <summary>
/// Provides time based micro compactor.
/// </summary>
public sealed class TimeBasedMicroCompactor : IMicroCompactor
{
    public MicrocompactResult Run(
        IReadOnlyList<ConversationMessage> messages,
        MicrocompactRunOptions? options = null,
        DateTimeOffset? now = null)
    {
        options ??= new MicrocompactRunOptions();
        now ??= DateTimeOffset.UtcNow;

        var preserveTailCount = Math.Max(0, options.PreserveTailCount);
        var eligibleMessages = messages.Count <= preserveTailCount
            ? Array.Empty<ConversationMessage>()
            : messages.Take(messages.Count - preserveTailCount).ToArray();

        if (!options.Force && !ShouldRun(messages, options.CacheCooldown, now.Value))
        {
            return new MicrocompactResult
            {
                UpdatedMessages = messages.ToArray(),
                Edits = Array.Empty<MicrocompactEdit>(),
                ClearedToolResultCount = 0,
                ClearedThinkingBlockCount = 0,
            };
        }

        var edits = new List<MicrocompactEdit>();
        var updatedMessages = new List<ConversationMessage>(messages.Count);
        var clearedToolResults = 0;
        var clearedThinkingBlocks = 0;
        var eligibleIds = eligibleMessages
            .Select(message => message.Id)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var message in messages)
        {
            if (!eligibleIds.Contains(message.Id))
            {
                updatedMessages.Add(message);
                continue;
            }

            var rewritten = RewriteMessage(
                message,
                options.ClearThinkingBlocks,
                out var clearedToolResult,
                out var clearedThinking);

            if (clearedToolResult || clearedThinking)
            {
                edits.Add(new MicrocompactEdit
                {
                    MessageId = message.Id,
                    ClearToolResult = clearedToolResult,
                    ClearThinking = clearedThinking,
                });
            }

            if (clearedToolResult)
                clearedToolResults++;

            if (clearedThinking)
                clearedThinkingBlocks++;

            updatedMessages.Add(rewritten);
        }

        return new MicrocompactResult
        {
            UpdatedMessages = updatedMessages,
            Edits = edits,
            ClearedToolResultCount = clearedToolResults,
            ClearedThinkingBlockCount = clearedThinkingBlocks,
        };
    }

    private static bool ShouldRun(
        IReadOnlyList<ConversationMessage> messages,
        TimeSpan cacheCooldown,
        DateTimeOffset now)
    {
        var lastAssistant = messages
            .OfType<AssistantMessage>()
            .OrderByDescending(message => message.Timestamp)
            .FirstOrDefault();

        if (lastAssistant == null)
            return false;

        return now - lastAssistant.Timestamp >= cacheCooldown;
    }

    private static ConversationMessage RewriteMessage(
        ConversationMessage message,
        bool clearThinkingBlocks,
        out bool clearedToolResult,
        out bool clearedThinking)
    {
        clearedToolResult = false;
        clearedThinking = false;

        switch (message)
        {
            case UserMessage user:
                {
                    var rewritten = new List<ContentBlock>(user.Content.Count);
                    foreach (var block in user.Content)
                    {
                        if (block is ToolResultBlock result &&
                            !string.Equals(result.Content, MicrocompactPlaceholders.OldToolResult, StringComparison.Ordinal))
                        {
                            rewritten.Add(new ToolResultBlock(
                                result.ToolUseId,
                                MicrocompactPlaceholders.OldToolResult,
                                result.IsError));
                            clearedToolResult = true;
                        }
                        else
                        {
                            rewritten.Add(block);
                        }
                    }

                    return clearedToolResult
                        ? user with
                        {
                            Content = rewritten,
                            ToolUseResult = string.IsNullOrWhiteSpace(user.ToolUseResult)
                                ? user.ToolUseResult
                                : MicrocompactPlaceholders.OldToolResult,
                        }
                        : user;
                }

            case AssistantMessage assistant when clearThinkingBlocks:
                {
                    var rewritten = new List<ContentBlock>(assistant.Content.Count);
                    foreach (var block in assistant.Content)
                    {
                        if (block is ThinkingBlock thinking &&
                            !string.Equals(thinking.Text, MicrocompactPlaceholders.OldThinking, StringComparison.Ordinal))
                        {
                            rewritten.Add(new ThinkingBlock(MicrocompactPlaceholders.OldThinking));
                            clearedThinking = true;
                        }
                        else
                        {
                            rewritten.Add(block);
                        }
                    }

                    return clearedThinking
                        ? assistant with { Content = rewritten }
                        : assistant;
                }

            default:
                return message;
        }
    }
}

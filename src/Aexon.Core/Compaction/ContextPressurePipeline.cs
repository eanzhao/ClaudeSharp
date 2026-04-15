using Aexon.Core.Messages;

namespace Aexon.Core.Compaction;

/// <summary>
/// Represents options for context pressure.
/// </summary>
public sealed class ContextPressureOptions
{
    public required bool EnableAutoCompact { get; init; }
    public required bool EnableSessionMemoryCompact { get; init; }
    public required int PreserveTailCount { get; init; }
    public required AutoCompactPolicyOptions Policy { get; init; }
    public SessionMemoryCompactionOptions SessionMemory { get; init; } = new();
}

/// <summary>
/// Represents context preparation result.
/// </summary>
public sealed class ContextPreparationResult
{
    public required AutoCompactDecision InitialDecision { get; init; }
    public required AutoCompactDecision FinalDecision { get; init; }
    public MicrocompactResult? MicrocompactResult { get; init; }
    public SessionMemoryCompactionResult? SessionMemoryResult { get; init; }
    public ConversationCompactionResult? CompactionResult { get; init; }

    public bool HasChanges =>
        MicrocompactResult?.HasChanges == true ||
        SessionMemoryResult?.HasChanges == true ||
        CompactionResult != null;
}

/// <summary>
/// Defines the contract for context pressure pipeline.
/// </summary>
public interface IContextPressurePipeline
{
    ContextPreparationResult Prepare(
        IReadOnlyList<ConversationMessage> messages,
        ContextPressureOptions options,
        DateTimeOffset? now = null);
}

/// <summary>
/// Provides default context pressure pipeline.
/// </summary>
public sealed class DefaultContextPressurePipeline : IContextPressurePipeline
{
    private readonly IAutoCompactPolicy _policy;
    private readonly IMicroCompactor _microCompactor;
    private readonly ISessionMemoryCompactor _sessionMemoryCompactor;
    private readonly IConversationCompactor _conversationCompactor;

    public DefaultContextPressurePipeline(
        IAutoCompactPolicy? policy = null,
        IMicroCompactor? microCompactor = null,
        ISessionMemoryCompactor? sessionMemoryCompactor = null,
        IConversationCompactor? conversationCompactor = null)
    {
        _policy = policy ?? new HeuristicAutoCompactPolicy();
        _microCompactor = microCompactor ?? new TimeBasedMicroCompactor();
        _sessionMemoryCompactor = sessionMemoryCompactor ?? new SessionMemoryCompactor();
        _conversationCompactor = conversationCompactor ?? new HeuristicConversationCompactor();
    }

    public ContextPreparationResult Prepare(
        IReadOnlyList<ConversationMessage> messages,
        ContextPressureOptions options,
        DateTimeOffset? now = null)
    {
        now ??= DateTimeOffset.UtcNow;

        var disabledDecision = new AutoCompactDecision
        {
            Action = AutoCompactAction.None,
            Reason = "auto-compact-disabled",
            ApproxPromptTokens = 0,
            AvailableInputBudgetTokens = options.Policy.ApproxContextWindowTokens,
        };

        if (!options.EnableAutoCompact)
        {
            return new ContextPreparationResult
            {
                InitialDecision = disabledDecision,
                FinalDecision = disabledDecision,
            };
        }

        var initialDecision = _policy.Evaluate(messages, options.Policy);
        var workingMessages = messages;
        MicrocompactResult? microcompactResult = null;

        if (initialDecision.Action is AutoCompactAction.TryMicrocompact or AutoCompactAction.FullCompact)
        {
            var attempt = _microCompactor.Run(
                workingMessages,
                new MicrocompactRunOptions
                {
                    PreserveTailCount = options.PreserveTailCount,
                    Force = false,
                },
                now);

            if (attempt.HasChanges)
            {
                microcompactResult = attempt;
                workingMessages = attempt.UpdatedMessages;
            }
        }

        var finalDecision = _policy.Evaluate(workingMessages, options.Policy);
        SessionMemoryCompactionResult? sessionMemoryResult = null;
        if (options.EnableSessionMemoryCompact &&
            finalDecision.Action == AutoCompactAction.FullCompact)
        {
            sessionMemoryResult = _sessionMemoryCompactor.Compact(
                workingMessages,
                new SessionMemoryCompactionOptions
                {
                    PreserveTailCount = Math.Max(
                        options.SessionMemory.PreserveTailCount,
                        options.PreserveTailCount),
                    MinimumFoldedMessageCount = options.SessionMemory.MinimumFoldedMessageCount,
                    MaximumSummaryCharacters = options.SessionMemory.MaximumSummaryCharacters,
                    MaximumPreviewMessages = options.SessionMemory.MaximumPreviewMessages,
                    PreviewCharactersPerMessage = options.SessionMemory.PreviewCharactersPerMessage,
                    SummaryHeading = options.SessionMemory.SummaryHeading,
                    TailNote = options.SessionMemory.TailNote,
                });

            if (sessionMemoryResult?.HasChanges == true)
            {
                workingMessages = sessionMemoryResult.ActiveMessages;
                finalDecision = _policy.Evaluate(workingMessages, options.Policy);
            }
        }

        ConversationCompactionResult? compactionResult = null;
        if (finalDecision.Action == AutoCompactAction.FullCompact)
        {
            compactionResult = _conversationCompactor.Compact(
                workingMessages,
                options.PreserveTailCount);

            if (compactionResult != null)
            {
                workingMessages = compactionResult.ActiveMessages;
                finalDecision = _policy.Evaluate(workingMessages, options.Policy);
            }
        }

        return new ContextPreparationResult
        {
            InitialDecision = initialDecision,
            FinalDecision = finalDecision,
            MicrocompactResult = microcompactResult,
            SessionMemoryResult = sessionMemoryResult,
            CompactionResult = compactionResult,
        };
    }
}

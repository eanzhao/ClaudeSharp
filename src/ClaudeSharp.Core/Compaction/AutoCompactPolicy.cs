using ClaudeSharp.Core.Messages;

namespace ClaudeSharp.Core.Compaction;

public enum AutoCompactAction
{
    None,
    TryMicrocompact,
    FullCompact,
}

public sealed class AutoCompactDecision
{
    public required AutoCompactAction Action { get; init; }
    public required string Reason { get; init; }
    public required int ApproxPromptTokens { get; init; }
    public required int AvailableInputBudgetTokens { get; init; }
}

public sealed class AutoCompactPolicyOptions
{
    public required int ApproxContextWindowTokens { get; init; }
    public required int MaxOutputTokens { get; init; }
    public required int BufferTokens { get; init; }
    public required int ApproxCharsPerToken { get; init; }
    public required int MinimumMessageCount { get; init; }
    public required double WarningRatio { get; init; }
    public required double BlockingRatio { get; init; }
}

public interface IAutoCompactPolicy
{
    AutoCompactDecision Evaluate(
        IReadOnlyList<ConversationMessage> messages,
        AutoCompactPolicyOptions options);
}

public sealed class HeuristicAutoCompactPolicy : IAutoCompactPolicy
{
    private readonly IPromptTokenEstimator _tokenEstimator;

    public HeuristicAutoCompactPolicy(IPromptTokenEstimator? tokenEstimator = null)
    {
        _tokenEstimator = tokenEstimator ?? new HeuristicPromptTokenEstimator();
    }

    public AutoCompactDecision Evaluate(
        IReadOnlyList<ConversationMessage> messages,
        AutoCompactPolicyOptions options)
    {
        var budget = Math.Max(
            1_024,
            options.ApproxContextWindowTokens - options.MaxOutputTokens - options.BufferTokens);
        var estimate = _tokenEstimator.Estimate(messages, ToEstimatorOptions(options));
        var approxTokens = estimate.TotalTokens;

        if (messages.Count < options.MinimumMessageCount)
        {
            return new AutoCompactDecision
            {
                Action = AutoCompactAction.None,
                Reason = "message-count-below-threshold",
                ApproxPromptTokens = approxTokens,
                AvailableInputBudgetTokens = budget,
            };
        }

        var warningThreshold = (int)(budget * options.WarningRatio);
        var blockingThreshold = (int)(budget * options.BlockingRatio);

        if (approxTokens >= blockingThreshold)
        {
            return new AutoCompactDecision
            {
                Action = AutoCompactAction.FullCompact,
                Reason = $"approx-prompt-tokens={approxTokens} exceeds blocking threshold {blockingThreshold}",
                ApproxPromptTokens = approxTokens,
                AvailableInputBudgetTokens = budget,
            };
        }

        if (approxTokens >= warningThreshold)
        {
            return new AutoCompactDecision
            {
                Action = AutoCompactAction.TryMicrocompact,
                Reason = $"approx-prompt-tokens={approxTokens} exceeds warning threshold {warningThreshold}",
                ApproxPromptTokens = approxTokens,
                AvailableInputBudgetTokens = budget,
            };
        }

        return new AutoCompactDecision
        {
            Action = AutoCompactAction.None,
            Reason = "below-pressure-threshold",
            ApproxPromptTokens = approxTokens,
            AvailableInputBudgetTokens = budget,
        };
    }

    private static PromptTokenEstimatorOptions ToEstimatorOptions(AutoCompactPolicyOptions options)
    {
        return new PromptTokenEstimatorOptions
        {
            ApproxCharsPerToken = options.ApproxCharsPerToken,
        };
    }
}

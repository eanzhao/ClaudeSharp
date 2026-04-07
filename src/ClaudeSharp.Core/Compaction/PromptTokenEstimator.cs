using System.Text.Json;
using ClaudeSharp.Core.Messages;

namespace ClaudeSharp.Core.Compaction;

/// <summary>
/// Represents options for prompt token estimator.
/// </summary>
public sealed class PromptTokenEstimatorOptions
{
    public int ApproxCharsPerToken { get; init; } = 4;
    public int MessageEnvelopeTokens { get; init; } = 8;
    public int UserMessageEnvelopeTokens { get; init; } = 10;
    public int AssistantMessageEnvelopeTokens { get; init; } = 10;
    public int SystemMessageEnvelopeTokens { get; init; } = 8;
    public int TextBlockOverheadTokens { get; init; } = 1;
    public int ThinkingBlockOverheadTokens { get; init; } = 3;
    public int ToolUseBlockOverheadTokens { get; init; } = 8;
    public int ToolResultBlockOverheadTokens { get; init; } = 6;
    public int JsonContainerOverheadTokens { get; init; } = 2;
    public int JsonPropertyOverheadTokens { get; init; } = 2;
    public int JsonEscapePenaltyDivisor { get; init; } = 12;
    public int SignatureOverheadTokens { get; init; } = 4;
    public int MinimumTextTokens { get; init; } = 1;
}

/// <summary>
/// Represents prompt token estimate.
/// </summary>
public sealed class PromptTokenEstimate
{
    public required int TotalTokens { get; init; }
    public required int EnvelopeTokens { get; init; }
    public required int TextTokens { get; init; }
    public required int ThinkingTokens { get; init; }
    public required int ToolUseTokens { get; init; }
    public required int ToolResultTokens { get; init; }
    public required int JsonTokens { get; init; }
    public required int MessageCount { get; init; }
}

/// <summary>
/// Defines the contract for prompt token estimator.
/// </summary>
public interface IPromptTokenEstimator
{
    PromptTokenEstimate Estimate(
        IReadOnlyList<ConversationMessage> messages,
        PromptTokenEstimatorOptions? options = null);
}

/// <summary>
/// Represents heuristic prompt token estimator.
/// </summary>
public sealed class HeuristicPromptTokenEstimator : IPromptTokenEstimator
{
    public PromptTokenEstimate Estimate(
        IReadOnlyList<ConversationMessage> messages,
        PromptTokenEstimatorOptions? options = null)
    {
        options ??= new PromptTokenEstimatorOptions();

        var envelopeTokens = 0;
        var textTokens = 0;
        var thinkingTokens = 0;
        var toolUseTokens = 0;
        var toolResultTokens = 0;
        var jsonTokens = 0;

        foreach (var message in messages)
        {
            envelopeTokens += EstimateMessageEnvelopeTokens(message, options);

            switch (message)
            {
                case UserMessage user:
                    foreach (var block in user.Content)
                    {
                        switch (block)
                        {
                            case TextBlock text:
                                textTokens += EstimateTextTokens(text.Text, options);
                                break;

                            case ToolResultBlock result:
                                toolResultTokens += options.ToolResultBlockOverheadTokens;
                                toolResultTokens += EstimateTextTokens(result.Content, options);
                                break;
                        }
                    }
                    break;

                case AssistantMessage assistant:
                    foreach (var block in assistant.Content)
                    {
                        switch (block)
                        {
                            case TextBlock text:
                                textTokens += EstimateTextTokens(text.Text, options);
                                break;

                            case ThinkingBlock thinking:
                                thinkingTokens += options.ThinkingBlockOverheadTokens;
                                thinkingTokens += EstimateTextTokens(thinking.Text, options);
                                if (!string.IsNullOrWhiteSpace(thinking.Signature))
                                    thinkingTokens += options.SignatureOverheadTokens;
                                break;

                            case ToolUseBlock toolUse:
                                toolUseTokens += options.ToolUseBlockOverheadTokens;
                                toolUseTokens += EstimateTextTokens(toolUse.Name, options);
                                jsonTokens += EstimateJsonTokens(toolUse.Input, options);
                                break;
                        }
                    }
                    break;

                case SystemMessage system:
                    textTokens += EstimateTextTokens(system.Content, options);
                    break;
            }
        }

        var totalTokens =
            envelopeTokens +
            textTokens +
            thinkingTokens +
            toolUseTokens +
            toolResultTokens +
            jsonTokens;

        return new PromptTokenEstimate
        {
            TotalTokens = totalTokens,
            EnvelopeTokens = envelopeTokens,
            TextTokens = textTokens,
            ThinkingTokens = thinkingTokens,
            ToolUseTokens = toolUseTokens,
            ToolResultTokens = toolResultTokens,
            JsonTokens = jsonTokens,
            MessageCount = messages.Count,
        };
    }

    private static int EstimateMessageEnvelopeTokens(
        ConversationMessage message,
        PromptTokenEstimatorOptions options)
    {
        return message switch
        {
            UserMessage => options.UserMessageEnvelopeTokens,
            AssistantMessage => options.AssistantMessageEnvelopeTokens,
            SystemMessage => options.SystemMessageEnvelopeTokens,
            _ => options.MessageEnvelopeTokens,
        };
    }

    private static int EstimateTextTokens(
        string? text,
        PromptTokenEstimatorOptions options)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        var visibleChars = 0;
        var escapedPenalty = 0;
        var inWhitespace = false;

        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!inWhitespace)
                {
                    visibleChars++;
                    inWhitespace = true;
                }
            }
            else
            {
                visibleChars++;
                inWhitespace = false;
            }

            if (ch is '\\' or '"' or '\n' or '\r' or '\t')
                escapedPenalty++;
        }

        var baseTokens = (int)Math.Ceiling(visibleChars / (double)Math.Max(1, options.ApproxCharsPerToken));
        var penaltyTokens = escapedPenalty / Math.Max(1, options.JsonEscapePenaltyDivisor);
        return Math.Max(options.MinimumTextTokens, baseTokens + penaltyTokens);
    }

    private static int EstimateJsonTokens(
        JsonElement element,
        PromptTokenEstimatorOptions options)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => EstimateObjectTokens(element, options),
            JsonValueKind.Array => EstimateArrayTokens(element, options),
            JsonValueKind.String => EstimateTextTokens(element.GetString(), options) + 1,
            JsonValueKind.Number => EstimateTextTokens(element.GetRawText(), options),
            JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null => 1,
            _ => EstimateTextTokens(element.GetRawText(), options),
        };
    }

    private static int EstimateObjectTokens(
        JsonElement element,
        PromptTokenEstimatorOptions options)
    {
        var tokens = options.JsonContainerOverheadTokens;
        foreach (var property in element.EnumerateObject())
        {
            tokens += options.JsonPropertyOverheadTokens;
            tokens += EstimateTextTokens(property.Name, options);
            tokens += EstimateJsonTokens(property.Value, options);
        }

        return tokens;
    }

    private static int EstimateArrayTokens(
        JsonElement element,
        PromptTokenEstimatorOptions options)
    {
        var tokens = options.JsonContainerOverheadTokens;
        foreach (var item in element.EnumerateArray())
        {
            tokens += options.JsonPropertyOverheadTokens;
            tokens += EstimateJsonTokens(item, options);
        }

        return tokens;
    }
}

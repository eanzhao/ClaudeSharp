using Aexon.Core.Query;
using Anthropic.Models.Messages;
using Microsoft.Extensions.AI;

namespace Aexon.Cli;

/// <summary>
/// Injects Anthropic-specific thinking configuration into <see cref="ChatOptions"/>
/// based on the provider-agnostic <c>ThinkingMode</c> / <c>ThinkingBudgetTokens</c>
/// additional properties set by QueryEngine.
/// </summary>
internal sealed class AnthropicThinkingMiddleware(IChatClient inner) : DelegatingChatClient(inner)
{
    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return base.GetResponseAsync(messages, ApplyThinkingConfig(options), cancellationToken);
    }

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return base.GetStreamingResponseAsync(messages, ApplyThinkingConfig(options), cancellationToken);
    }

    private static ChatOptions? ApplyThinkingConfig(ChatOptions? options)
    {
        if (options?.AdditionalProperties == null)
            return options;

        if (!options.AdditionalProperties.TryGetValue(ChatClientPropertyKeys.ThinkingMode, out var modeObj) ||
            modeObj is not string modeStr)
            return options;

        var budget = 10240;
        if (options.AdditionalProperties.TryGetValue(ChatClientPropertyKeys.ThinkingBudgetTokens, out var budgetObj))
            budget = Convert.ToInt32(budgetObj);

        ThinkingConfigParam? thinkingConfig = modeStr switch
        {
            "Disabled" => new ThinkingConfigDisabled(),
            "Enabled" => new ThinkingConfigEnabled(budget),
            "Adaptive" => new ThinkingConfigAdaptive(),
            _ => null,
        };

        if (thinkingConfig == null)
            return options;

        var cloned = options.Clone();
        var existingFactory = cloned.RawRepresentationFactory;
        cloned.RawRepresentationFactory = requestOptions =>
        {
            if (existingFactory?.Invoke(requestOptions) is MessageCreateParams existingRequest)
            {
                return new MessageCreateParams(existingRequest)
                {
                    Thinking = thinkingConfig,
                };
            }

            return new MessageCreateParams
            {
                Model = options.ModelId ?? "claude-sonnet-4-6",
                MaxTokens = options.MaxOutputTokens ?? 16384,
                Messages = [],
                Thinking = thinkingConfig,
            };
        };

        return cloned;
    }
}

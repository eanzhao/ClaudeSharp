using Aexon.Core.Query;
using Microsoft.Extensions.AI;
using OpenAI.Chat;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Aexon.Cli;

/// <summary>
/// Injects OpenAI-specific reasoning effort into <see cref="ChatOptions"/>
/// for o-series models based on the provider-agnostic <c>ThinkingMode</c>
/// additional property set by QueryEngine.
/// </summary>
internal sealed class OpenAIReasoningMiddleware(IChatClient inner) : DelegatingChatClient(inner)
{
    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return base.GetResponseAsync(messages, ApplyReasoningConfig(options), cancellationToken);
    }

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return base.GetStreamingResponseAsync(messages, ApplyReasoningConfig(options), cancellationToken);
    }

    private static ChatOptions? ApplyReasoningConfig(ChatOptions? options)
    {
        if (options?.AdditionalProperties == null)
            return options;

#pragma warning disable OPENAI001
        ChatReasoningEffortLevel? effort = null;
#pragma warning restore OPENAI001
        if (options.AdditionalProperties.TryGetValue(ChatClientPropertyKeys.Effort, out var effortObj) &&
            effortObj is string effortStr)
        {
#pragma warning disable OPENAI001
            effort = effortStr switch
            {
                "Fast" => ChatReasoningEffortLevel.Low,
                "Balanced" => ChatReasoningEffortLevel.Medium,
                "Thorough" => ChatReasoningEffortLevel.High,
                _ => null,
            };
#pragma warning restore OPENAI001
        }

        if (!options.AdditionalProperties.TryGetValue(ChatClientPropertyKeys.ThinkingMode, out var modeObj) ||
            modeObj is not string modeStr)
        {
            return effort == null || !IsReasoningModel(options.ModelId)
                ? options
                : CloneWithReasoningEffort(options, effort.Value);
        }

        if (!IsReasoningModel(options.ModelId))
            return options;

#pragma warning disable OPENAI001
        effort ??= modeStr switch
        {
            "Disabled" => null,
            "Enabled" => ChatReasoningEffortLevel.High,
            "Adaptive" => ChatReasoningEffortLevel.Medium,
            _ => null,
        };
#pragma warning restore OPENAI001

        if (effort == null)
            return options;

        return CloneWithReasoningEffort(options, effort.Value);
    }

    private static ChatOptions CloneWithReasoningEffort(
        ChatOptions options,
#pragma warning disable OPENAI001
        ChatReasoningEffortLevel effort)
#pragma warning restore OPENAI001
    {
        var cloned = options.Clone();
#pragma warning disable OPENAI001
        cloned.RawRepresentationFactory = _ => new ChatCompletionOptions
        {
            ReasoningEffortLevel = effort,
        };
#pragma warning restore OPENAI001
        return cloned;
    }

    private static bool IsReasoningModel(string? modelId) =>
        modelId != null &&
        (modelId.StartsWith("o1", StringComparison.OrdinalIgnoreCase) ||
         modelId.StartsWith("o3", StringComparison.OrdinalIgnoreCase) ||
         modelId.StartsWith("o4", StringComparison.OrdinalIgnoreCase));
}

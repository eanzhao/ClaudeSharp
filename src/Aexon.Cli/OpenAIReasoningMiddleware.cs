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

        if (!options.AdditionalProperties.TryGetValue("ThinkingMode", out var modeObj) ||
            modeObj is not string modeStr)
            return options;

        if (!IsReasoningModel(options.ModelId))
            return options;

#pragma warning disable OPENAI001
        var effort = modeStr switch
        {
            "Disabled" => (ChatReasoningEffortLevel?)null,
            "Enabled" => ChatReasoningEffortLevel.High,
            "Adaptive" => ChatReasoningEffortLevel.Medium,
            _ => null,
        };
#pragma warning restore OPENAI001

        if (effort == null)
            return options;

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

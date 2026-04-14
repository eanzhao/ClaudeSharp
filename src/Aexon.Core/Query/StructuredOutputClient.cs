using System.Text.Json;
using Aexon.Core.Messages;
using Microsoft.Extensions.AI;

namespace Aexon.Core.Query;

/// <summary>
/// Requests strongly typed responses from the underlying chat client.
/// </summary>
public sealed class StructuredOutputClient
{
    private readonly IChatClient _chatClient;
    private readonly JsonSerializerOptions _serializerOptions;

    public StructuredOutputClient(
        IChatClient chatClient,
        JsonSerializerOptions? serializerOptions = null)
    {
        _chatClient = chatClient;
        _serializerOptions = serializerOptions ?? new JsonSerializerOptions(JsonSerializerDefaults.Web);
    }

    public Task<StructuredOutputResult<T>> GetResponseAsync<T>(
        IReadOnlyList<ConversationMessage> messages,
        ChatOptions? options = null,
        int maxFormatAttempts = 2,
        CancellationToken cancellationToken = default) =>
        GetResponseAsync<T>(
            ChatMessageConverter.ToMeaiMessages(messages),
            options,
            maxFormatAttempts,
            cancellationToken);

    public async Task<StructuredOutputResult<T>> GetResponseAsync<T>(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options = null,
        int maxFormatAttempts = 2,
        CancellationToken cancellationToken = default)
    {
        if (maxFormatAttempts < 1)
            throw new ArgumentOutOfRangeException(nameof(maxFormatAttempts));

        var requestMessages = messages.Select(message => message.Clone()).ToArray();

        for (var attempt = 1; attempt <= maxFormatAttempts; attempt++)
        {
            var responseOptions = options?.Clone() ?? new ChatOptions();
            responseOptions.ResponseFormat ??= ChatResponseFormat.ForJsonSchema<T>(_serializerOptions);

            var response = await _chatClient.GetResponseAsync<T>(
                requestMessages.Select(message => message.Clone()),
                _serializerOptions,
                responseOptions,
                useJsonSchemaResponseFormat: false,
                cancellationToken);

            if (response.TryGetResult(out var result))
            {
                return new StructuredOutputResult<T>(
                    result!,
                    response.Text,
                    ChatMessageConverter.ToTokenUsage(response.Usage));
            }
        }

        throw new InvalidOperationException(
            $"The model did not return valid structured output after {maxFormatAttempts} attempts.");
    }
}

/// <summary>
/// Represents a successful structured-output response.
/// </summary>
public sealed record StructuredOutputResult<T>(
    T Result,
    string RawText,
    TokenUsage Usage);

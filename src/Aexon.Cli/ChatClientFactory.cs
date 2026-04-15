using System.ClientModel;
using Aexon.Core.Configuration;
using Aexon.Core.Query;
using Anthropic;
using Microsoft.Extensions.AI;
using OpenAI;

namespace Aexon.Cli;

internal sealed record ChatClientFactoryResult(
    IChatClient ChatClient,
    bool HasApiKey,
    IDisposable? Disposable) : IDisposable
{
    public void Dispose() => Disposable?.Dispose();
}

internal static class ChatClientFactory
{
    public static ChatClientFactoryResult Create(
        AiProvider provider,
        string model,
        AnthropicClientSettings anthropicSettings)
    {
        return provider switch
        {
            AiProvider.OpenAI => CreateOpenAI(model),
            _ => CreateAnthropic(model, anthropicSettings),
        };
    }

    private static ChatClientFactoryResult CreateAnthropic(string model, AnthropicClientSettings settings)
    {
        var responseObserver = new ApiResponseObserver();
        var client = CreateAnthropicClient(settings, responseObserver);
        var chatClient = new AnthropicRetryMiddleware(
            new AnthropicThinkingMiddleware(
                client.AsIChatClient(model, defaultMaxOutputTokens: 16384)),
            responseObserver);

        return new ChatClientFactoryResult(chatClient, settings.HasApiKey, client);
    }

    private static ChatClientFactoryResult CreateOpenAI(string model)
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var baseUrl = Environment.GetEnvironmentVariable("OPENAI_BASE_URL");

        OpenAIClientOptions? clientOptions = null;
        if (!string.IsNullOrWhiteSpace(baseUrl))
            clientOptions = new OpenAIClientOptions { Endpoint = new Uri(baseUrl) };

        var credential = new ApiKeyCredential(apiKey ?? "dummy-key");
        var client = clientOptions != null
            ? new OpenAIClient(credential, clientOptions)
            : new OpenAIClient(credential);

        var chatClient = client
            .GetChatClient(model)
            .AsIChatClient()
            .AsBuilder()
            .Use(inner => new OpenAIReasoningMiddleware(inner))
            .Build();

        return new ChatClientFactoryResult(chatClient, !string.IsNullOrWhiteSpace(apiKey), null);
    }

    private static AnthropicClient CreateAnthropicClient(
        AnthropicClientSettings settings,
        ApiResponseObserver? responseObserver = null)
    {
        var httpClient = responseObserver?.CreateHttpClient();

        if (settings.HasApiKey && !string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            return httpClient != null
                ? new AnthropicClient
                {
                    ApiKey = settings.ApiKey!,
                    BaseUrl = settings.BaseUrl,
                    HttpClient = httpClient,
                    MaxRetries = 0,
                }
                : new AnthropicClient
                {
                    ApiKey = settings.ApiKey!,
                    BaseUrl = settings.BaseUrl,
                    MaxRetries = 0,
                };
        }

        if (settings.HasApiKey)
        {
            return httpClient != null
                ? new AnthropicClient
                {
                    ApiKey = settings.ApiKey!,
                    HttpClient = httpClient,
                    MaxRetries = 0,
                }
                : new AnthropicClient
                {
                    ApiKey = settings.ApiKey!,
                    MaxRetries = 0,
                };
        }

        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            return httpClient != null
                ? new AnthropicClient
                {
                    BaseUrl = settings.BaseUrl,
                    HttpClient = httpClient,
                    MaxRetries = 0,
                }
                : new AnthropicClient
                {
                    BaseUrl = settings.BaseUrl,
                    MaxRetries = 0,
                };
        }

        return httpClient != null
            ? new AnthropicClient
            {
                HttpClient = httpClient,
                MaxRetries = 0,
            }
            : new AnthropicClient
            {
                MaxRetries = 0,
            };
    }
}

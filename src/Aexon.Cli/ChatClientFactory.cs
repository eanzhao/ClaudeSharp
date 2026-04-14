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
        var client = CreateAnthropicClient(settings);
        var chatClient = client
            .AsIChatClient(model, defaultMaxOutputTokens: 16384)
            .AsBuilder()
            .Use(inner => new AnthropicThinkingMiddleware(inner))
            .Build();

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

    private static AnthropicClient CreateAnthropicClient(AnthropicClientSettings settings)
    {
        if (settings.HasApiKey && !string.IsNullOrWhiteSpace(settings.BaseUrl))
            return new AnthropicClient { ApiKey = settings.ApiKey!, BaseUrl = settings.BaseUrl };

        if (settings.HasApiKey)
            return new AnthropicClient { ApiKey = settings.ApiKey! };

        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            return new AnthropicClient { BaseUrl = settings.BaseUrl };

        return new AnthropicClient();
    }
}

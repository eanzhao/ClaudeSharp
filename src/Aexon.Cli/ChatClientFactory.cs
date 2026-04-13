using System.ClientModel;
using Aexon.Core.Configuration;
using Anthropic;
using Microsoft.Extensions.AI;
using OpenAI;

namespace Aexon.Cli;

internal enum AiProvider
{
    Anthropic,
    OpenAI,
}

internal sealed record ChatClientFactoryResult(
    IChatClient ChatClient,
    bool HasApiKey,
    string ProviderName,
    IDisposable? Disposable)
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

    public static AiProvider DetectProvider(string? providerFlag, string model)
    {
        if (!string.IsNullOrWhiteSpace(providerFlag))
        {
            return providerFlag.Equals("openai", StringComparison.OrdinalIgnoreCase)
                ? AiProvider.OpenAI
                : AiProvider.Anthropic;
        }

        if (model.StartsWith("gpt-", StringComparison.OrdinalIgnoreCase) ||
            model.StartsWith("o1", StringComparison.OrdinalIgnoreCase) ||
            model.StartsWith("o3", StringComparison.OrdinalIgnoreCase) ||
            model.StartsWith("o4", StringComparison.OrdinalIgnoreCase))
        {
            return AiProvider.OpenAI;
        }

        return AiProvider.Anthropic;
    }

    public static string ResolveModel(string? input, AiProvider provider)
    {
        if (!string.IsNullOrWhiteSpace(input) && provider == AiProvider.OpenAI)
            return input;

        return provider == AiProvider.OpenAI ? "gpt-4o" : Aexon.Core.Query.ClaudeModels.Resolve(input);
    }

    private static ChatClientFactoryResult CreateAnthropic(string model, AnthropicClientSettings settings)
    {
        var client = CreateAnthropicClient(settings);
        var chatClient = client
            .AsIChatClient(model, defaultMaxOutputTokens: 16384)
            .AsBuilder()
            .Use(inner => new AnthropicThinkingMiddleware(inner))
            .Build();

        return new ChatClientFactoryResult(chatClient, settings.HasApiKey, "Anthropic", client);
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

        return new ChatClientFactoryResult(chatClient, !string.IsNullOrWhiteSpace(apiKey), "OpenAI", null);
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

using System.ClientModel;
using Aexon.Core.Configuration;
using Aexon.Core.Query;
using Anthropic;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OpenAI;

namespace Aexon.Cli;

internal sealed record ChatClientBootstrap(
    IChatClient ChatClient,
    bool HasRequiredConfiguration,
    string? StartupSummary);

internal static class ChatClientFactory
{
    public static ChatClientBootstrap Create(
        AiProvider provider,
        string model,
        AnthropicClientSettings anthropicSettings)
    {
        return provider switch
        {
            AiProvider.OpenAI => CreateOpenAI(model),
            AiProvider.Ollama => CreateOllama(model),
            _ => CreateAnthropic(model, anthropicSettings),
        };
    }

    private static ChatClientBootstrap CreateAnthropic(
        string model,
        AnthropicClientSettings settings)
    {
        var client = CreateAnthropicClient(settings);
        var chatClient = new OwnedChatClient(
            client.AsIChatClient(model, defaultMaxOutputTokens: 16384),
            client);

        return new ChatClientBootstrap(
            chatClient,
            settings.HasApiKey,
            settings.StartupSummary);
    }

    private static ChatClientBootstrap CreateOpenAI(string model)
    {
        var settings = OpenAIClientSettingsLoader.Load();

        OpenAIClientOptions? clientOptions = null;
        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            clientOptions = new OpenAIClientOptions { Endpoint = new Uri(settings.BaseUrl) };

        var credential = new ApiKeyCredential(settings.ApiKey ?? "dummy-key");
        var client = clientOptions != null
            ? new OpenAIClient(credential, clientOptions)
            : new OpenAIClient(credential);

        return new ChatClientBootstrap(
            client.GetChatClient(model).AsIChatClient(),
            settings.HasUsableConfiguration,
            settings.StartupSummary);
    }

    private static ChatClientBootstrap CreateOllama(string model)
    {
        var settings = OllamaClientSettingsLoader.Load();
        var client = new OllamaApiClient(new Uri(settings.BaseUrl), model);

        return new ChatClientBootstrap(
            (IChatClient)client,
            true,
            settings.StartupSummary);
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

    private sealed class OwnedChatClient(
        IChatClient innerClient,
        IDisposable? ownedResource = null) : DelegatingChatClient(innerClient)
    {
        protected override void Dispose(bool disposing)
        {
            try
            {
                base.Dispose(disposing);
            }
            finally
            {
                if (disposing)
                    ownedResource?.Dispose();
            }
        }
    }
}

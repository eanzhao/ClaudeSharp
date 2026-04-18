using System.ClientModel;
using System.ClientModel.Primitives;
using Aexon.Core.Auth;
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
        NyxIdRoutingContext? nyxIdRouting = null)
    {
        // When the user selected a NyxID AI Service (proxy slug) as their
        // default, we treat it as an OpenAI-compatible provider and point
        // the OpenAI SDK at `/api/v1/proxy/s/{slug}/v1/` instead of the
        // gateway-native `/api/v1/llm/openai/v1/` path.
        if (nyxIdRouting?.ProxyServiceSlug is { Length: > 0 })
        {
            return CreateOpenAICompatibleProxyService(
                model,
                nyxIdRouting!);
        }

        return provider switch
        {
            AiProvider.Ollama => CreateOllama(model),
            AiProvider.OpenAI => CreateOpenAIWithNyxId(
                model,
                RequireNyxId(nyxIdRouting, AiProvider.OpenAI)),
            _ => CreateAnthropicWithNyxId(
                model,
                RequireNyxId(nyxIdRouting, AiProvider.Anthropic)),
        };
    }

    private static NyxIdRoutingContext RequireNyxId(
        NyxIdRoutingContext? routing,
        AiProvider provider)
    {
        if (routing == null)
        {
            throw new InvalidOperationException(
                $"Provider '{provider}' requires NyxID routing. Run `aexon login` first.");
        }

        return routing;
    }

    private static ChatClientBootstrap CreateOpenAIWithNyxId(
        string model,
        NyxIdRoutingContext routing)
    {
        var proxyBaseUrl = NyxIdProxyEndpoints.Resolve(routing.BaseUrl, AiProvider.OpenAI);
        return CreateOpenAICompatible(model, routing, proxyBaseUrl, "OpenAI");
    }

    private static ChatClientBootstrap CreateOpenAICompatibleProxyService(
        string model,
        NyxIdRoutingContext routing)
    {
        var proxyBaseUrl = NyxIdProxyEndpoints.ResolveProxyServiceEndpoint(
            routing.BaseUrl,
            routing.ProxyServiceSlug!);
        var displayName = string.IsNullOrWhiteSpace(routing.ProxyServiceLabel)
            ? $"NyxID proxy '{routing.ProxyServiceSlug}'"
            : $"{routing.ProxyServiceLabel} (proxy '{routing.ProxyServiceSlug}')";
        return CreateOpenAICompatible(model, routing, proxyBaseUrl, displayName);
    }

    private static ChatClientBootstrap CreateOpenAICompatible(
        string model,
        NyxIdRoutingContext routing,
        Uri proxyBaseUrl,
        string displayName)
    {
        var httpClient = new HttpClient(
            new NyxIdProxyAuthenticationHandler(routing.TokenProvider)
            {
                InnerHandler = new HttpClientHandler(),
            },
            disposeHandler: true);
        var client = new OpenAIClient(
            new ApiKeyCredential("nyxid-proxy"),
            new OpenAIClientOptions
            {
                Endpoint = proxyBaseUrl,
                Transport = new HttpClientPipelineTransport(httpClient),
            });

        return new ChatClientBootstrap(
            new OwnedChatClient(client.GetChatClient(model).AsIChatClient(), httpClient),
            routing.HasStoredCredentials,
            BuildNyxIdStartupSummary(displayName, proxyBaseUrl, routing.HasStoredCredentials));
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

    private static ChatClientBootstrap CreateAnthropicWithNyxId(
        string model,
        NyxIdRoutingContext routing)
    {
        var responseObserver = new ApiResponseObserver();
        var proxyBaseUrl = NyxIdProxyEndpoints.Resolve(routing.BaseUrl, AiProvider.Anthropic);
        var client = new AnthropicClient
        {
            ApiKey = "nyxid-proxy",
            BaseUrl = proxyBaseUrl.ToString().TrimEnd('/'),
            HttpClient = responseObserver.CreateHttpClient(
                new NyxIdProxyAuthenticationHandler(routing.TokenProvider)
                {
                    InnerHandler = new HttpClientHandler(),
                }),
            MaxRetries = 0,
        };

        var chatClient = new OwnedChatClient(
            new AnthropicRetryMiddleware(
                new AnthropicThinkingMiddleware(
                    client.AsIChatClient(model, defaultMaxOutputTokens: 16384)),
                responseObserver),
            client);

        return new ChatClientBootstrap(
            chatClient,
            routing.HasStoredCredentials,
            BuildNyxIdStartupSummary("Anthropic", proxyBaseUrl, routing.HasStoredCredentials));
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

    private static string BuildNyxIdStartupSummary(
        string providerName,
        Uri proxyBaseUrl,
        bool hasStoredCredentials)
    {
        var summary = $"NyxID routing: proxying {providerName} via {proxyBaseUrl}.";
        if (hasStoredCredentials)
            return summary;

        return $"{summary}{Environment.NewLine}NyxID routing is enabled, but no NyxID credentials are stored yet. Run `aexon login` first.";
    }
}

internal sealed record NyxIdRoutingContext(
    string BaseUrl,
    NyxIdTokenProvider TokenProvider,
    bool HasStoredCredentials,
    string? ProxyServiceSlug = null,
    string? ProxyServiceLabel = null);

namespace Aexon.Core.Query;

/// <summary>
/// Resolves NyxID LLM gateway endpoints for supported chat providers.
/// </summary>
public static class NyxIdProxyEndpoints
{
    public static Uri Resolve(string nyxIdBaseUrl, AiProvider provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nyxIdBaseUrl);
        if (!Uri.TryCreate(nyxIdBaseUrl.Trim().TrimEnd('/'), UriKind.Absolute, out var baseUri))
            throw new ArgumentException("NyxID base URL must be an absolute URL.", nameof(nyxIdBaseUrl));

        var slug = ProviderSlug(provider);
        return new Uri(baseUri, $"/api/v1/llm/{slug}/v1/");
    }

    public static Uri ResolveStatusEndpoint(string nyxIdBaseUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nyxIdBaseUrl);
        if (!Uri.TryCreate(nyxIdBaseUrl.Trim().TrimEnd('/'), UriKind.Absolute, out var baseUri))
            throw new ArgumentException("NyxID base URL must be an absolute URL.", nameof(nyxIdBaseUrl));

        return new Uri(baseUri, "/api/v1/llm/status");
    }

    /// <summary>
    /// Returns the NyxID provider slug for a supported chat provider. These match the
    /// <c>provider_slug</c> values NyxID returns from <c>GET /api/v1/llm/status</c>.
    /// </summary>
    public static string ProviderSlug(AiProvider provider) =>
        provider switch
        {
            AiProvider.OpenAI => "openai",
            AiProvider.Anthropic => "anthropic",
            _ => throw new NotSupportedException($"NyxID routing does not support provider '{provider}'."),
        };
}

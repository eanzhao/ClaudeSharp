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
    /// Endpoint that lists the caller's AI Services (per-user "keys" in the
    /// NyxID dashboard): <c>GET /api/v1/keys</c>.
    /// </summary>
    public static Uri ResolveKeysEndpoint(string nyxIdBaseUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nyxIdBaseUrl);
        if (!Uri.TryCreate(nyxIdBaseUrl.Trim().TrimEnd('/'), UriKind.Absolute, out var baseUri))
            throw new ArgumentException("NyxID base URL must be an absolute URL.", nameof(nyxIdBaseUrl));

        return new Uri(baseUri, "/api/v1/keys");
    }

    /// <summary>
    /// Base URL for an individual proxy AI Service — e.g. Chrono LLM's
    /// <c>/api/v1/proxy/s/chrono-llm/</c>. The chat client and model probe
    /// relative-append <c>chat/completions</c> / <c>models</c> onto this,
    /// and NyxID's proxy handler forwards <c>{base}/{appended-path}</c>
    /// straight to the service's configured <c>endpoint_url</c> (which
    /// per NyxID convention already includes any <c>/v1</c> prefix — e.g.
    /// Chrono LLM's endpoint is <c>https://llm.aelf.dev/v1</c>). Adding
    /// our own <c>/v1/</c> here would double the segment.
    /// </summary>
    public static Uri ResolveProxyServiceEndpoint(string nyxIdBaseUrl, string serviceSlug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nyxIdBaseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceSlug);
        if (!Uri.TryCreate(nyxIdBaseUrl.Trim().TrimEnd('/'), UriKind.Absolute, out var baseUri))
            throw new ArgumentException("NyxID base URL must be an absolute URL.", nameof(nyxIdBaseUrl));

        return new Uri(baseUri, $"/api/v1/proxy/s/{serviceSlug.Trim('/')}/");
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

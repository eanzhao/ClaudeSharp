using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Aexon.Core.Query;

namespace Aexon.Core.Auth;

/// <summary>
/// Queries NyxID's LLM gateway status endpoint to discover which providers
/// the signed-in user has connected credentials for.
/// </summary>
public sealed class NyxIdLlmStatusClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly NyxIdTokenProvider _tokenProvider;
    private readonly bool _ownsHttpClient;

    public NyxIdLlmStatusClient(NyxIdTokenProvider tokenProvider, HttpClient? httpClient = null)
    {
        _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
        if (httpClient != null)
        {
            _httpClient = httpClient;
            _ownsHttpClient = false;
        }
        else
        {
            _httpClient = new HttpClient();
            _ownsHttpClient = true;
        }
    }

    public async Task<NyxIdLlmStatus> GetStatusAsync(
        string nyxIdBaseUrl,
        CancellationToken cancellationToken = default)
    {
        var endpoint = NyxIdProxyEndpoints.ResolveStatusEndpoint(nyxIdBaseUrl);
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        var accessToken = await _tokenProvider.GetValidAccessTokenAsync(cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new NotLoggedInException("NyxID session is no longer valid. Run /login again.");
        }

        response.EnsureSuccessStatusCode();

        var payload = await response.Content
            .ReadFromJsonAsync<NyxIdLlmStatusPayload>(cancellationToken)
            .ConfigureAwait(false);

        if (payload == null)
            throw new InvalidOperationException("NyxID /api/v1/llm/status returned an empty body.");

        return new NyxIdLlmStatus(
            payload.Providers ?? [],
            payload.GatewayUrl ?? string.Empty,
            payload.SupportedModels ?? []);
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }

    private sealed record NyxIdLlmStatusPayload
    {
        [JsonPropertyName("providers")]
        public IReadOnlyList<NyxIdLlmProviderStatus>? Providers { get; init; }

        [JsonPropertyName("gateway_url")]
        public string? GatewayUrl { get; init; }

        [JsonPropertyName("supported_models")]
        public IReadOnlyList<string>? SupportedModels { get; init; }
    }
}

/// <summary>
/// Parsed NyxID LLM gateway status.
/// </summary>
public sealed record NyxIdLlmStatus(
    IReadOnlyList<NyxIdLlmProviderStatus> Providers,
    string GatewayUrl,
    IReadOnlyList<string> SupportedModels);

/// <summary>
/// A single provider entry returned by NyxID's <c>GET /api/v1/llm/status</c>.
/// </summary>
public sealed record NyxIdLlmProviderStatus
{
    [JsonPropertyName("provider_slug")]
    public string ProviderSlug { get; init; } = string.Empty;

    [JsonPropertyName("provider_name")]
    public string ProviderName { get; init; } = string.Empty;

    /// <summary>
    /// One of <c>ready</c>, <c>not_connected</c>, or <c>expired</c>.
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("proxy_url")]
    public string ProxyUrl { get; init; } = string.Empty;

    public bool IsReady => string.Equals(Status, "ready", StringComparison.OrdinalIgnoreCase);
}

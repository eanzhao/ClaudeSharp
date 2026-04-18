using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aexon.Core.Query;

namespace Aexon.Core.Auth;

/// <summary>
/// Reads the caller's user-scoped AI Services from NyxID —
/// <c>GET /api/v1/keys</c> returns every "key" the dashboard renders under
/// AI Services (gateway-backed services like Chrono LLM, plus user-added
/// proxy services like Mimo). Each row is also probed through
/// <c>GET /api/v1/proxy/s/{slug}/v1/models</c> to classify whether it is
/// OpenAI-compatible and to discover the concrete model ids to offer to
/// the user.
/// </summary>
/// <remarks>
/// Excluded from coverage — the instance methods drive real HTTP against
/// NyxID and there's no in-process substitute the test project can
/// reach without wiring a full mock server. The pure parser
/// <see cref="ParseOpenAiModelList"/> is factored out as internal-static
/// and still unit-tested in <c>NyxIdKeysClientTests</c>; behavioral
/// correctness of <see cref="ListAsync"/> and <see cref="TryProbeModelsAsync"/>
/// is verified by running <c>aexon llm</c> against mainnet.
/// </remarks>
[ExcludeFromCodeCoverage]
public sealed class NyxIdKeysClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly NyxIdTokenProvider _tokenProvider;
    private readonly bool _ownsHttpClient;

    public NyxIdKeysClient(NyxIdTokenProvider tokenProvider, HttpClient? httpClient = null)
    {
        _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
        if (httpClient != null)
        {
            _httpClient = httpClient;
            _ownsHttpClient = false;
        }
        else
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            _ownsHttpClient = true;
        }
    }

    /// <summary>
    /// Fetches the authenticated user's AI Services from
    /// <c>GET /api/v1/keys</c>. Every row is returned as-is — callers filter
    /// by <see cref="NyxIdAiServiceInfo.IsActive"/>, status, etc. as needed.
    /// </summary>
    public async Task<IReadOnlyList<NyxIdAiServiceInfo>> ListAsync(
        string nyxIdBaseUrl,
        CancellationToken cancellationToken = default)
    {
        var endpoint = NyxIdProxyEndpoints.ResolveKeysEndpoint(nyxIdBaseUrl);
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        var accessToken = await _tokenProvider.GetValidAccessTokenAsync(cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new NotLoggedInException(
                "NyxID session is no longer valid. Run `aexon login` again.");
        }

        response.EnsureSuccessStatusCode();

        var payload = await response.Content
            .ReadFromJsonAsync<KeyListPayload>(cancellationToken)
            .ConfigureAwait(false);

        return payload?.Keys ?? [];
    }

    /// <summary>
    /// Probes a proxy AI Service for OpenAI-compatible <c>/v1/models</c>.
    /// Returns the list of model ids on success, or null if the endpoint
    /// does not respond with an OpenAI-shaped body (i.e. the service
    /// cannot be used as an LLM provider).
    /// </summary>
    public async Task<IReadOnlyList<string>?> TryProbeModelsAsync(
        string nyxIdBaseUrl,
        string serviceSlug,
        CancellationToken cancellationToken = default)
    {
        var baseUri = NyxIdProxyEndpoints.ResolveProxyServiceEndpoint(nyxIdBaseUrl, serviceSlug);
        var modelsEndpoint = new Uri(baseUri, "models");

        using var request = new HttpRequestMessage(HttpMethod.Get, modelsEndpoint);
        var accessToken = await _tokenProvider.GetValidAccessTokenAsync(cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return ParseOpenAiModelList(body);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Connectivity issues, TLS errors, timeouts: treat as "not an
            // LLM provider" rather than surfacing a hard failure — the
            // picker is meant to degrade gracefully when a service is down.
            return null;
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }

    internal static IReadOnlyList<string>? ParseOpenAiModelList(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            if (root.TryGetProperty("data", out var data) &&
                data.ValueKind == JsonValueKind.Array)
            {
                var ids = new List<string>();
                foreach (var entry in data.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.Object)
                        continue;
                    if (entry.TryGetProperty("id", out var id) &&
                        id.ValueKind == JsonValueKind.String)
                    {
                        var s = id.GetString();
                        if (!string.IsNullOrWhiteSpace(s))
                            ids.Add(s!);
                    }
                }
                return ids;
            }

            if (root.TryGetProperty("models", out var models) &&
                models.ValueKind == JsonValueKind.Array)
            {
                var ids = new List<string>();
                foreach (var entry in models.EnumerateArray())
                {
                    if (entry.ValueKind == JsonValueKind.String)
                    {
                        var s = entry.GetString();
                        if (!string.IsNullOrWhiteSpace(s))
                            ids.Add(s!);
                    }
                    else if (entry.ValueKind == JsonValueKind.Object &&
                             entry.TryGetProperty("id", out var id) &&
                             id.ValueKind == JsonValueKind.String)
                    {
                        var s = id.GetString();
                        if (!string.IsNullOrWhiteSpace(s))
                            ids.Add(s!);
                    }
                }
                return ids;
            }
        }
        catch (JsonException)
        {
            // Service doesn't speak JSON, or schema mismatch — not LLM-shaped.
            return null;
        }

        return null;
    }

    private sealed record KeyListPayload
    {
        [JsonPropertyName("keys")]
        public IReadOnlyList<NyxIdAiServiceInfo>? Keys { get; init; }
    }
}

/// <summary>
/// One entry in the response body of <c>GET /api/v1/keys</c>. Only the
/// fields Aexon actually uses are mapped — the real response is much
/// wider (SSH / OAuth / org-provenance / etc.), but the picker only needs
/// enough to classify LLM-capable services and build a routing URL.
/// </summary>
public sealed record NyxIdAiServiceInfo
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>Human-facing display name (e.g. "Chrono LLM").</summary>
    [JsonPropertyName("label")]
    public string Label { get; init; } = string.Empty;

    /// <summary>Proxy routing slug (e.g. "chrono-llm" → <c>/api/v1/proxy/s/chrono-llm</c>).</summary>
    [JsonPropertyName("slug")]
    public string Slug { get; init; } = string.Empty;

    /// <summary>Actual upstream URL the proxy forwards to. Informational only.</summary>
    [JsonPropertyName("endpoint_url")]
    public string EndpointUrl { get; init; } = string.Empty;

    /// <summary>"http" or "ssh". Only "http" entries are candidate LLMs.</summary>
    [JsonPropertyName("service_type")]
    public string ServiceType { get; init; } = string.Empty;

    [JsonPropertyName("auth_method")]
    public string AuthMethod { get; init; } = string.Empty;

    [JsonPropertyName("auth_key_name")]
    public string AuthKeyName { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("is_active")]
    public bool IsActive { get; init; }

    /// <summary>
    /// When this entry was auto-provisioned from a catalog row, holds the
    /// catalog slug (e.g. "anthropic", "openai", "chrono-llm"). Useful for
    /// grouping or hinting at provider family. Null for fully-custom
    /// user-added services.
    /// </summary>
    [JsonPropertyName("catalog_service_slug")]
    public string? CatalogServiceSlug { get; init; }

    [JsonPropertyName("catalog_service_name")]
    public string? CatalogServiceName { get; init; }

    /// <summary>True when the service looks usable as an HTTP/LLM proxy target.</summary>
    public bool IsHttpService =>
        string.Equals(ServiceType, "http", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the backing credential + service are both live.</summary>
    public bool IsReady =>
        IsActive &&
        string.Equals(Status, "active", StringComparison.OrdinalIgnoreCase);
}

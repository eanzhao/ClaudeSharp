using System.Text.Json.Serialization;

namespace Aexon.Core.Auth;

/// <summary>
/// Stores NyxID tokens and client metadata for the active login.
/// </summary>
public sealed record NyxIdCredentials
{
    [JsonPropertyName("base_url")]
    public string BaseUrl { get; init; } = string.Empty;

    [JsonPropertyName("access_token")]
    public string AccessToken { get; init; } = string.Empty;

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; init; }

    [JsonPropertyName("id_token")]
    public string? IdToken { get; init; }

    [JsonPropertyName("expires_at")]
    public DateTimeOffset ExpiresAt { get; init; }

    [JsonPropertyName("client_id")]
    public string ClientId { get; init; } = string.Empty;

    [JsonPropertyName("default_provider")]
    public string? DefaultProvider { get; init; }

    [JsonPropertyName("default_model")]
    public string? DefaultModel { get; init; }

    /// <summary>
    /// When set, Aexon routes through the NyxID AI Service with this slug
    /// (<c>/api/v1/proxy/s/{slug}/v1/</c>) as an OpenAI-compatible provider
    /// instead of the gateway-native <c>/api/v1/llm/{provider}/v1/</c> path.
    /// Mutually exclusive with <see cref="DefaultProvider"/> —
    /// <see cref="NyxIdProviderPicker.SaveDefaultProxyService"/> keeps the
    /// invariant by clearing the other slot when it writes.
    /// </summary>
    [JsonPropertyName("default_proxy_slug")]
    public string? DefaultProxySlug { get; init; }

    /// <summary>
    /// Display label of the proxy service that <see cref="DefaultProxySlug"/>
    /// refers to (e.g. "Chrono LLM"). Purely cosmetic — the picker captures
    /// it so <c>/llm show</c> can render a human-friendly name without
    /// having to re-query NyxID.
    /// </summary>
    [JsonPropertyName("default_proxy_label")]
    public string? DefaultProxyLabel { get; init; }
}

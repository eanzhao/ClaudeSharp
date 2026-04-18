using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using Aexon.Core.Auth;

namespace Aexon.Core.Aevatar;

/// <summary>
/// HTTP client for an aevatar backend's explorer proxy to chrono-storage
/// (<c>/api/explorer/*</c>). All requests are scoped to the logged-in user on
/// the server side via the NyxID bearer token — aexon never talks directly
/// to chrono-storage.
///
/// Endpoint mapping:
///   GET    /api/explorer/manifest        — list scope manifest
///   GET    /api/explorer/files/{key}     — download file (text or bytes)
///   PUT    /api/explorer/files/{key}     — write text (blocked for workflow/script)
///   POST   /api/explorer/upload/{key}    — multipart binary upload (≤50 MB)
///   DELETE /api/explorer/files/{key}     — delete file (blocked for workflow/script)
/// </summary>
public sealed class AevatarStorageClient : IDisposable
{
    private const string MultipartFieldName = "file";

    private readonly HttpClient _http;
    private readonly NyxIdTokenProvider _tokenProvider;
    private readonly bool _ownsHttpClient;

    public AevatarStorageClient(string baseUrl, NyxIdTokenProvider tokenProvider, HttpClient? httpClient = null)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new ArgumentException("baseUrl is required", nameof(baseUrl));

        _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
        _ownsHttpClient = httpClient is null;
        _http = httpClient ?? new HttpClient();
        _http.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
    }

    public Uri BaseAddress => _http.BaseAddress!;

    public async Task<IReadOnlyList<AevatarStorageFile>> ListAsync(CancellationToken ct)
    {
        using var request = await BuildRequestAsync(HttpMethod.Get, "api/explorer/manifest", content: null, ct);
        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);

        var payload = await response.Content.ReadFromJsonAsync<ManifestResponse>(cancellationToken: ct);
        return payload?.Files ?? Array.Empty<AevatarStorageFile>();
    }

    /// <summary>
    /// Downloads the raw bytes plus the response media type. Works for both text
    /// and binary files; the caller decides how to interpret them.
    /// </summary>
    public async Task<AevatarStorageContent> GetAsync(string key, CancellationToken ct)
    {
        using var request = await BuildRequestAsync(
            HttpMethod.Get,
            $"api/explorer/files/{EscapeKey(key)}",
            content: null,
            ct);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        await EnsureSuccessAsync(response, ct);

        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        var mediaType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        return new AevatarStorageContent(bytes, mediaType);
    }

    public async Task PutTextAsync(string key, string content, string? mediaType, CancellationToken ct)
    {
        using var body = new StringContent(
            content ?? string.Empty,
            Encoding.UTF8,
            string.IsNullOrWhiteSpace(mediaType) ? "text/plain" : mediaType);

        using var request = await BuildRequestAsync(
            HttpMethod.Put,
            $"api/explorer/files/{EscapeKey(key)}",
            body,
            ct);

        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);
    }

    public async Task<AevatarStorageUpload> UploadAsync(
        string key,
        Stream stream,
        string fileName,
        string? mediaType,
        CancellationToken ct)
    {
        using var multipart = new MultipartFormDataContent();
        var streamContent = new StreamContent(stream);
        if (!string.IsNullOrWhiteSpace(mediaType) &&
            MediaTypeHeaderValue.TryParse(mediaType, out var parsed))
        {
            streamContent.Headers.ContentType = parsed;
        }

        multipart.Add(streamContent, MultipartFieldName, fileName);

        using var request = await BuildRequestAsync(
            HttpMethod.Post,
            $"api/explorer/upload/{EscapeKey(key)}",
            multipart,
            ct);

        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);

        var result = await response.Content.ReadFromJsonAsync<AevatarStorageUpload>(cancellationToken: ct);
        return result ?? new AevatarStorageUpload { Key = key, Size = 0, ContentType = mediaType };
    }

    public async Task DeleteAsync(string key, CancellationToken ct)
    {
        using var request = await BuildRequestAsync(
            HttpMethod.Delete,
            $"api/explorer/files/{EscapeKey(key)}",
            content: null,
            ct);

        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);
    }

    // ── Infrastructure ──

    private async Task<HttpRequestMessage> BuildRequestAsync(
        HttpMethod method,
        string relativePath,
        HttpContent? content,
        CancellationToken ct)
    {
        var token = await _tokenProvider.GetValidAccessTokenAsync(ct);
        var request = new HttpRequestMessage(method, relativePath)
        {
            Content = content,
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    /// <summary>
    /// Chrono-storage keys frequently contain slashes (e.g. <c>chat-media/abc.png</c>).
    /// Escape each segment so the slashes survive the backend's path routing.
    /// </summary>
    private static string EscapeKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return string.Join(
            "/",
            key.Split('/', StringSplitOptions.None).Select(Uri.EscapeDataString));
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = string.Empty;
        try
        {
            body = await response.Content.ReadAsStringAsync(ct);
        }
        catch
        {
            // best-effort diagnostic
        }

        var truncated = body.Length > 500 ? body[..500] + "…" : body;
        var detail = string.IsNullOrWhiteSpace(truncated) ? string.Empty : $" — {truncated}";
        throw new AevatarStorageException(
            response.StatusCode,
            $"aevatar storage request failed: {(int)response.StatusCode} {response.StatusCode}{detail}");
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
            _http.Dispose();
    }

    private sealed record ManifestResponse(
        [property: JsonPropertyName("files")] AevatarStorageFile[]? Files);
}

public sealed record AevatarStorageFile
{
    [JsonPropertyName("key")]
    public string Key { get; init; } = string.Empty;

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("updatedAt")]
    public string? UpdatedAt { get; init; }

    [JsonPropertyName("size")]
    public long? Size { get; init; }
}

public sealed record AevatarStorageContent(byte[] Bytes, string MediaType)
{
    public bool IsLikelyText =>
        MediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
        MediaType.Contains("json", StringComparison.OrdinalIgnoreCase) ||
        MediaType.Contains("yaml", StringComparison.OrdinalIgnoreCase) ||
        MediaType.Contains("xml", StringComparison.OrdinalIgnoreCase) ||
        MediaType.Contains("javascript", StringComparison.OrdinalIgnoreCase);
}

public sealed record AevatarStorageUpload
{
    [JsonPropertyName("key")]
    public string Key { get; init; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; init; }

    [JsonPropertyName("contentType")]
    public string? ContentType { get; init; }
}

public sealed class AevatarStorageException : Exception
{
    public AevatarStorageException(HttpStatusCode statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}

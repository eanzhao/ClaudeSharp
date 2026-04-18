using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web;

namespace Aexon.Core.Auth;

/// <summary>
/// Handles NyxID login, refresh, and logout flows using the same <c>/cli-auth</c>
/// protocol as the upstream <c>nyxid</c> Rust CLI. The browser login pattern:
///   1. <c>GET {base}/api/v1/public/config</c> to discover the frontend URL.
///   2. Open <c>{frontend}/cli-auth?port=...&amp;state=...&amp;client_ua=...</c>.
///   3. Listen on a loopback port for <c>GET /callback?access_token=...&amp;refresh_token=...&amp;state=...</c>.
/// The password flow (<see cref="LoginWithPasswordAsync"/>) hits
/// <c>POST {base}/api/v1/auth/login</c> directly and mirrors the CLI's
/// <c>nyxid login --password</c> command.
/// </summary>
public sealed class NyxIdAuthService
{
    internal const string ClientUserAgent = "aexon-cli";

    /// <summary>
    /// Pseudo-client-id persisted into <see cref="NyxIdCredentials.ClientId"/> so
    /// the existing credential store keeps validating saved files. The nyxid CLI
    /// flow does not use OAuth dynamic registration, so there is no real client id.
    /// </summary>
    internal const string SyntheticClientId = "nyxid-cli";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan CallbackTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan FallbackAccessTokenLifetime = TimeSpan.FromMinutes(30);

    private readonly HttpClient _httpClient;
    private readonly NyxIdCredentialStore _credentialStore;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Action<Uri> _browserLauncher;

    public NyxIdAuthService(
        HttpClient? httpClient = null,
        NyxIdCredentialStore? credentialStore = null,
        Func<DateTimeOffset>? clock = null,
        Action<Uri>? browserLauncher = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _credentialStore = credentialStore ?? new NyxIdCredentialStore();
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _browserLauncher = browserLauncher ?? OpenBrowser;
    }

    /// <summary>
    /// Runs the nyxid CLI-style browser login: fetches the frontend URL from
    /// <c>/api/v1/public/config</c>, binds a loopback listener, opens
    /// <c>/cli-auth</c>, and waits for the redirect back to <c>/callback</c>.
    /// </summary>
    public async Task<NyxIdCredentials> LoginAsync(
        string baseUrl,
        CancellationToken cancellationToken = default)
    {
        var normalizedBaseUrl = NormalizeBaseUrl(baseUrl);

        var frontendUrl = await FetchFrontendUrlAsync(normalizedBaseUrl, cancellationToken);

        var port = AllocateLoopbackPort();
        var state = GenerateRandomHex(16);
        using var listener = CreateLoopbackListener(port);

        var authUrl = BuildCliAuthUrl(frontendUrl, port, state);
        _browserLauncher(new Uri(authUrl));

        var callback = await WaitForCallbackAsync(listener, state, cancellationToken);
        var expiresAt = ResolveAccessTokenExpiry(callback.AccessToken);

        return new NyxIdCredentials
        {
            BaseUrl = normalizedBaseUrl,
            ClientId = SyntheticClientId,
            AccessToken = callback.AccessToken,
            RefreshToken = callback.RefreshToken,
            IdToken = null,
            ExpiresAt = expiresAt,
        };
    }

    /// <summary>
    /// Email/password login, mirroring <c>nyxid login --password</c>. Useful for
    /// headless environments where no browser is available.
    /// </summary>
    public async Task<NyxIdCredentials> LoginWithPasswordAsync(
        string baseUrl,
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var normalizedBaseUrl = NormalizeBaseUrl(baseUrl);

        using var response = await _httpClient.PostAsJsonAsync(
            $"{normalizedBaseUrl}/api/v1/auth/login",
            new PasswordLoginRequest(email.Trim(), password, "cli"),
            SerializerOptions,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>(SerializerOptions, cancellationToken)
                   ?? throw new InvalidOperationException("NyxID login response was empty.");
        if (string.IsNullOrWhiteSpace(body.AccessToken))
            throw new InvalidOperationException("NyxID login response is missing access_token.");

        return new NyxIdCredentials
        {
            BaseUrl = normalizedBaseUrl,
            ClientId = SyntheticClientId,
            AccessToken = body.AccessToken,
            RefreshToken = body.RefreshToken,
            IdToken = null,
            ExpiresAt = ResolveAccessTokenExpiry(body.AccessToken),
        };
    }

    /// <summary>
    /// Refreshes an access token against <c>POST /api/v1/auth/refresh</c>. The
    /// server returns a new <c>access_token</c> and <c>refresh_token</c>.
    /// </summary>
    public async Task<NyxIdCredentials> RefreshAsync(
        string baseUrl,
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);

        var normalizedBaseUrl = NormalizeBaseUrl(baseUrl);

        using var response = await _httpClient.PostAsJsonAsync(
            $"{normalizedBaseUrl}/api/v1/auth/refresh",
            new RefreshRequest(refreshToken),
            SerializerOptions,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var body = await response.Content.ReadFromJsonAsync<RefreshResponse>(SerializerOptions, cancellationToken)
                   ?? throw new InvalidOperationException("NyxID refresh response was empty.");
        if (string.IsNullOrWhiteSpace(body.AccessToken))
            throw new InvalidOperationException("NyxID refresh response is missing access_token.");

        return new NyxIdCredentials
        {
            BaseUrl = normalizedBaseUrl,
            ClientId = SyntheticClientId,
            AccessToken = body.AccessToken,
            RefreshToken = string.IsNullOrWhiteSpace(body.RefreshToken) ? refreshToken : body.RefreshToken,
            IdToken = null,
            ExpiresAt = ResolveAccessTokenExpiry(body.AccessToken),
        };
    }

    /// <summary>
    /// Best-effort server-side logout via <c>POST /api/v1/auth/logout</c>, then
    /// clears the local credential file. The <paramref name="bearerToken"/> should
    /// be an access token; pass <see cref="string.Empty"/> to skip the network call.
    /// </summary>
    public async Task LogoutAsync(
        string baseUrl,
        string bearerToken,
        CancellationToken cancellationToken = default)
    {
        var normalizedBaseUrl = NormalizeBaseUrl(baseUrl);

        try
        {
            if (!string.IsNullOrWhiteSpace(bearerToken))
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Post,
                    $"{normalizedBaseUrl}/api/v1/auth/logout");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
                using var response = await _httpClient.SendAsync(request, cancellationToken);
                // Do not throw: matches nyxid CLI's best-effort behavior.
                _ = response;
            }
        }
        finally
        {
            _credentialStore.Clear();
        }
    }

    // ── Internals used by tests ──

    internal static string BuildCliAuthUrl(string frontendUrl, int port, string state)
    {
        if (!Uri.TryCreate(frontendUrl.TrimEnd('/') + "/cli-auth", UriKind.Absolute, out var baseUri))
            throw new InvalidOperationException("NyxID frontend URL is not absolute.");

        var builder = new UriBuilder(baseUri);
        var query = HttpUtility.ParseQueryString(string.Empty);
        query["port"] = port.ToString(CultureInfo.InvariantCulture);
        query["state"] = state;
        query["client_ua"] = ClientUserAgent;
        builder.Query = query.ToString();
        return builder.Uri.ToString();
    }

    internal static CallbackTokens? ParseCallback(string requestLine, string expectedState)
    {
        if (string.IsNullOrWhiteSpace(requestLine))
            return null;

        var firstLine = requestLine.Split('\n').FirstOrDefault();
        if (string.IsNullOrWhiteSpace(firstLine))
            return null;

        var parts = firstLine.Split(' ');
        if (parts.Length < 2)
            return null;

        var path = parts[1];
        if (!path.StartsWith("/callback", StringComparison.Ordinal))
            return null;

        var queryStart = path.IndexOf('?');
        if (queryStart < 0)
            return null;

        var query = HttpUtility.ParseQueryString(path[(queryStart + 1)..]);
        var state = query["state"];
        if (!string.Equals(state, expectedState, StringComparison.Ordinal))
            return null;

        var accessToken = query["access_token"];
        if (string.IsNullOrWhiteSpace(accessToken))
            return null;

        var refreshToken = query["refresh_token"];
        return new CallbackTokens(accessToken!, string.IsNullOrWhiteSpace(refreshToken) ? null : refreshToken);
    }

    internal DateTimeOffset ResolveAccessTokenExpiry(string accessToken)
    {
        if (TryReadJwtExpiry(accessToken, out var expiresAt))
            return expiresAt;

        return _clock().Add(FallbackAccessTokenLifetime);
    }

    internal static bool TryReadJwtExpiry(string? jwt, out DateTimeOffset expiresAt)
    {
        expiresAt = default;
        if (!NyxIdJwtPayloadReader.TryParsePayload(jwt, out var payload) || payload is null)
            return false;

        using var doc = payload;
        if (!doc.RootElement.TryGetProperty("exp", out var expProp))
            return false;

        long seconds;
        if (expProp.ValueKind == JsonValueKind.Number)
        {
            if (!expProp.TryGetInt64(out seconds))
                return false;
        }
        else if (expProp.ValueKind == JsonValueKind.String &&
                 long.TryParse(expProp.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            seconds = parsed;
        }
        else
        {
            return false;
        }

        expiresAt = DateTimeOffset.FromUnixTimeSeconds(seconds);
        return true;
    }

    // ── Browser flow helpers ──

    internal sealed record CallbackTokens(string AccessToken, string? RefreshToken);

    private async Task<string> FetchFrontendUrlAsync(string baseUrl, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"{baseUrl}/api/v1/public/config",
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var config = await response.Content.ReadFromJsonAsync<PublicConfig>(SerializerOptions, cancellationToken)
                     ?? throw new InvalidOperationException("NyxID public config response was empty.");
        if (string.IsNullOrWhiteSpace(config.FrontendUrl))
            throw new InvalidOperationException("NyxID public config did not include frontend_url.");

        return config.FrontendUrl.TrimEnd('/');
    }

    private async Task<CallbackTokens> WaitForCallbackAsync(
        TcpListener listener,
        string expectedState,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(CallbackTimeout);

        while (!timeoutCts.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"NyxID login timed out after {CallbackTimeout.TotalSeconds:0}s. Please try again.");
            }

            using (client)
            await using (var stream = client.GetStream())
            {
                var buffer = new byte[8192];
                var read = await stream.ReadAsync(buffer.AsMemory(), timeoutCts.Token);
                var request = Encoding.UTF8.GetString(buffer, 0, read);

                var parsed = ParseCallback(request, expectedState);
                if (parsed is not null)
                {
                    await WriteHttpResponseAsync(
                        stream,
                        "200 OK",
                        "text/html",
                        CallbackSuccessHtml,
                        timeoutCts.Token);
                    return parsed;
                }

                await WriteHttpResponseAsync(
                    stream,
                    "404 Not Found",
                    "text/plain",
                    "Not Found",
                    timeoutCts.Token);
            }
        }

        throw new TimeoutException(
            $"NyxID login timed out after {CallbackTimeout.TotalSeconds:0}s. Please try again.");
    }

    private static async Task WriteHttpResponseAsync(
        NetworkStream stream,
        string status,
        string contentType,
        string body,
        CancellationToken cancellationToken)
    {
        var payload = Encoding.UTF8.GetBytes(
            $"HTTP/1.1 {status}\r\nContent-Type: {contentType}\r\nConnection: close\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\n\r\n{body}");
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static TcpListener CreateLoopbackListener(int port)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        return listener;
    }

    private static int AllocateLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static string GenerateRandomHex(int byteCount)
    {
        var bytes = RandomNumberGenerator.GetBytes(byteCount);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string NormalizeBaseUrl(string baseUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri))
            throw new ArgumentException("NyxID base URL must be an absolute URL.", nameof(baseUrl));
        return uri.ToString().TrimEnd('/');
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        string? errorCode = null;
        string? detail = null;
        try
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            detail = string.IsNullOrWhiteSpace(text) ? null : (text.Length > 400 ? text[..400] + "…" : text);
            if (!string.IsNullOrWhiteSpace(text))
            {
                try
                {
                    using var parsed = JsonDocument.Parse(text);
                    if (parsed.RootElement.ValueKind == JsonValueKind.Object &&
                        parsed.RootElement.TryGetProperty("error", out var errProp) &&
                        errProp.ValueKind == JsonValueKind.String)
                    {
                        errorCode = errProp.GetString();
                    }
                }
                catch (JsonException)
                {
                    // ignore; we already captured the raw body in `detail`.
                }
            }
        }
        catch
        {
            // best-effort body capture
        }

        throw new NyxIdProtocolException(
            response.StatusCode,
            errorCode,
            $"NyxID request failed with status {(int)response.StatusCode}{(detail is null ? string.Empty : ": " + detail)}");
    }

    private static void OpenBrowser(Uri uri)
    {
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = uri.ToString(),
            UseShellExecute = true,
        });

        if (process == null)
            throw new InvalidOperationException($"Failed to open the browser. Open this URL manually: {uri}");
    }

    private const string CallbackSuccessHtml = """
    <!doctype html>
    <html>
    <head><title>Aexon · NyxID</title></head>
    <body style="display:flex;align-items:center;justify-content:center;min-height:100vh;font-family:system-ui;background:#0f172a;color:#e2e8f0">
    <div style="text-align:center">
      <h2>Login successful</h2>
      <p style="color:#94a3b8">You can close this tab and return to aexon.</p>
    </div>
    </body>
    </html>
    """;

    // ── Wire types ──

    private sealed record PublicConfig(
        [property: JsonPropertyName("frontend_url")] string FrontendUrl);

    private sealed record PasswordLoginRequest(
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("password")] string Password,
        [property: JsonPropertyName("client")] string Client);

    private sealed record LoginResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken);

    private sealed record RefreshRequest(
        [property: JsonPropertyName("refresh_token")] string RefreshToken);

    private sealed record RefreshResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken);

    public sealed class NyxIdProtocolException : InvalidOperationException
    {
        public NyxIdProtocolException(HttpStatusCode statusCode, string? errorCode, string message)
            : base(message)
        {
            StatusCode = statusCode;
            ErrorCode = errorCode;
        }

        public HttpStatusCode StatusCode { get; }

        public string? ErrorCode { get; }
    }
}

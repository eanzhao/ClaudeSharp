using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Aexon.Core.Aevatar;
using Aexon.Core.Auth;

namespace Aexon.Core.Tests.Aevatar;

/// <summary>
/// HTTP-contract tests for <see cref="AevatarStorageClient"/>. Every request is
/// captured via a <see cref="StubHandler"/> so we can assert method, path,
/// <c>Authorization</c> header, and body shape without touching a real backend.
/// </summary>
public sealed class AevatarStorageClientTests
{
    private const string BaseUrl = "https://aevatar.example/";

    [Fact]
    public async Task ListAsyncReturnsManifestEntries()
    {
        var handler = new StubHandler(
            (HttpStatusCode.OK,
                """
                {
                  "files": [
                    { "key": "config.json", "type": "file", "updatedAt": "2026-04-18T00:00:00Z" },
                    { "key": "chat-media/abc.jpeg", "type": "file", "name": "photo", "updatedAt": "2026-04-17T00:00:00Z" }
                  ]
                }
                """,
                "application/json"));
        using var client = BuildClient(handler);

        var files = await client.ListAsync(CancellationToken.None);

        Assert.Equal(2, files.Count);
        Assert.Equal("config.json", files[0].Key);
        Assert.Equal("chat-media/abc.jpeg", files[1].Key);
        Assert.Equal("photo", files[1].Name);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.EndsWith("/api/explorer/manifest", request.RequestUri!.AbsolutePath);
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
    }

    [Fact]
    public async Task ListAsyncReturnsEmptyWhenResponseIsEmpty()
    {
        var handler = new StubHandler((HttpStatusCode.OK, """{"files":null}""", "application/json"));
        using var client = BuildClient(handler);

        var files = await client.ListAsync(CancellationToken.None);

        Assert.Empty(files);
    }

    [Fact]
    public async Task GetAsyncReturnsBytesAndMediaType()
    {
        var handler = new StubHandler((HttpStatusCode.OK, "hello world", "text/plain"));
        using var client = BuildClient(handler);

        var content = await client.GetAsync("config.json", CancellationToken.None);

        Assert.Equal("hello world", Encoding.UTF8.GetString(content.Bytes));
        Assert.Equal("text/plain", content.MediaType);
        Assert.True(content.IsLikelyText);
    }

    [Fact]
    public async Task GetAsyncEscapesKeysWithSlashes()
    {
        var handler = new StubHandler((HttpStatusCode.OK, "payload", "application/octet-stream"));
        using var client = BuildClient(handler);

        await client.GetAsync("chat-media/abc file.png", CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        // Slashes between segments are preserved; space inside a segment is escaped.
        Assert.EndsWith("/api/explorer/files/chat-media/abc%20file.png", request.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task GetAsyncFallsBackToOctetStreamWhenContentTypeMissing()
    {
        var handler = new StubHandler((HttpStatusCode.OK, "\u0001\u0002\u0003", null));
        using var client = BuildClient(handler);

        var content = await client.GetAsync("mystery", CancellationToken.None);

        Assert.Equal("application/octet-stream", content.MediaType);
        Assert.False(content.IsLikelyText);
    }

    [Fact]
    public async Task PutTextAsyncSendsUtf8BodyAndInferredMediaType()
    {
        var handler = new StubHandler((HttpStatusCode.NoContent, string.Empty, "text/plain"));
        using var client = BuildClient(handler);

        await client.PutTextAsync("notes.md", "# hi", "text/markdown", CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.EndsWith("/api/explorer/files/notes.md", request.RequestUri!.AbsolutePath);
        Assert.Equal("text/markdown", request.Content!.Headers.ContentType?.MediaType);
        Assert.Equal("# hi", await request.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PutTextAsyncDefaultsToTextPlainWhenMediaTypeMissing()
    {
        var handler = new StubHandler((HttpStatusCode.NoContent, string.Empty, "text/plain"));
        using var client = BuildClient(handler);

        await client.PutTextAsync("a", "b", mediaType: null, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("text/plain", request.Content!.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task UploadAsyncUsesMultipartWithFileField()
    {
        var handler = new StubHandler(
            (HttpStatusCode.OK,
                """{"key":"chat-media/x.png","size":42,"contentType":"image/png"}""",
                "application/json"));
        using var client = BuildClient(handler);

        var bytes = Encoding.UTF8.GetBytes("PNG-BYTES");
        using var stream = new MemoryStream(bytes);
        var result = await client.UploadAsync("chat-media/x.png", stream, "x.png", "image/png", CancellationToken.None);

        Assert.Equal("chat-media/x.png", result.Key);
        Assert.Equal(42, result.Size);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.EndsWith("/api/explorer/upload/chat-media/x.png", request.RequestUri!.AbsolutePath);
        var contentType = request.Content!.Headers.ContentType;
        Assert.Equal("multipart/form-data", contentType?.MediaType);
        var bodyText = await request.Content.ReadAsStringAsync();
        Assert.Contains("name=file", bodyText);
        Assert.Contains("filename=x.png", bodyText);
        Assert.Contains("PNG-BYTES", bodyText);
    }

    [Fact]
    public async Task UploadAsyncTolerateUnparseableMediaType()
    {
        var handler = new StubHandler(
            (HttpStatusCode.OK,
                """{"key":"k","size":1,"contentType":null}""",
                "application/json"));
        using var client = BuildClient(handler);

        using var stream = new MemoryStream([0x00]);
        await client.UploadAsync("k", stream, "k.bin", mediaType: "not a real media type", CancellationToken.None);

        // succeeded: the client ignored the bad media type, the upload still worked
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task DeleteAsyncSendsDeleteRequest()
    {
        var handler = new StubHandler((HttpStatusCode.NoContent, string.Empty, "text/plain"));
        using var client = BuildClient(handler);

        await client.DeleteAsync("chat-media/abc.png", CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.EndsWith("/api/explorer/files/chat-media/abc.png", request.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task FailedRequestThrowsAevatarStorageExceptionWithTruncatedBody()
    {
        var longBody = new string('e', 900);
        var handler = new StubHandler((HttpStatusCode.Forbidden, longBody, "text/plain"));
        using var client = BuildClient(handler);

        var ex = await Assert.ThrowsAsync<AevatarStorageException>(
            () => client.DeleteAsync("some-key", CancellationToken.None));

        Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);
        Assert.Contains("403", ex.Message);
        Assert.Contains("eee…", ex.Message);
    }

    [Fact]
    public void ContentIsLikelyTextCoversCommonTextFormats()
    {
        foreach (var type in new[] { "text/plain", "application/json", "application/yaml", "text/html", "application/javascript" })
            Assert.True(new AevatarStorageContent([], type).IsLikelyText, $"{type} should be text");

        foreach (var type in new[] { "image/png", "video/mp4", "application/pdf", "application/octet-stream" })
            Assert.False(new AevatarStorageContent([], type).IsLikelyText, $"{type} should be binary");
    }

    [Fact]
    public async Task ConstructorRejectsEmptyBaseUrl()
    {
        using var http = new HttpClient();
        var tokenProvider = BuildTokenProvider();
        Assert.Throws<ArgumentException>(() =>
            new AevatarStorageClient(string.Empty, tokenProvider, http));
        await Task.CompletedTask;
    }

    // ── Plumbing ──

    private static AevatarStorageClient BuildClient(StubHandler handler)
    {
        var http = new HttpClient(handler);
        var tokenProvider = BuildTokenProvider();
        return new AevatarStorageClient(BaseUrl, tokenProvider, http);
    }

    private static NyxIdTokenProvider BuildTokenProvider()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "aexon-test-" + Guid.NewGuid().ToString("N"));
        var exp = DateTimeOffset.UtcNow.AddMinutes(15).ToUnixTimeSeconds();
        var jwt = BuildJwt($"{{\"exp\":{exp}}}");

        var store = new NyxIdCredentialStore(tempDir, Path.Combine(tempDir, "preferences.json"));
        store.Save(new NyxIdCredentials
        {
            BaseUrl = "https://nyx.example",
            ClientId = NyxIdAuthService.SyntheticClientId,
            AccessToken = jwt,
            RefreshToken = "r",
            ExpiresAt = DateTimeOffset.FromUnixTimeSeconds(exp),
        });
        var authService = new NyxIdAuthService(credentialStore: store);
        return new NyxIdTokenProvider(store, authService);
    }

    private static string BuildJwt(string payloadJson)
    {
        var header = Base64UrlEncode(Encoding.UTF8.GetBytes("""{"alg":"none"}"""));
        var payload = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));
        return $"{header}.{payload}.";
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class StubHandler(params (HttpStatusCode Status, string Body, string? ContentType)[] responses) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(await CloneAsync(request, cancellationToken));
            var (status, body, contentType) = responses[Math.Min(Requests.Count - 1, responses.Length - 1)];
            var response = new HttpResponseMessage(status);
            if (contentType is null)
            {
                response.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body ?? string.Empty));
                response.Content.Headers.ContentType = null;
            }
            else
            {
                response.Content = new StringContent(body ?? string.Empty, Encoding.UTF8, contentType);
            }
            return response;
        }

        private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage source, CancellationToken ct)
        {
            var clone = new HttpRequestMessage(source.Method, source.RequestUri);
            foreach (var header in source.Headers)
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            if (source.Content is not null)
            {
                var bytes = await source.Content.ReadAsByteArrayAsync(ct);
                clone.Content = new ByteArrayContent(bytes);
                foreach (var header in source.Content.Headers)
                    clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            return clone;
        }
    }
}

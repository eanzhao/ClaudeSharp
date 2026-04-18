using System.Net;
using System.Text;
using System.Text.Json;
using Aexon.Core.Aevatar;
using Aexon.Core.Auth;
using Aexon.Core.Tests.Runtime;

namespace Aexon.Core.Tests.Aevatar;

/// <summary>
/// HTTP-contract tests for <see cref="AevatarChatClient"/>. Each request is
/// captured via a <see cref="StubHandler"/> so we can assert method, path,
/// Authorization header, and body shape without touching a real backend.
/// </summary>
public sealed class AevatarChatClientTests
{
    private const string BaseUrl = "https://aevatar.example/";
    private const string Scope = "default";

    [Fact]
    public async Task CreateConversationPostsAndReturnsActorId()
    {
        var handler = new StubHandler(
            (HttpStatusCode.OK, """{"actorId":"nyxid-chat-abc"}""", "application/json"));
        using var client = BuildClient(handler, out _);

        var actorId = await client.CreateConversationAsync(Scope, CancellationToken.None);

        Assert.Equal("nyxid-chat-abc", actorId);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(
            $"{BaseUrl.TrimEnd('/')}/api/scopes/{Scope}/nyxid-chat/conversations",
            request.RequestUri!.ToString());
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
    }

    [Fact]
    public async Task ListConversationsParsesActorIdList()
    {
        var handler = new StubHandler(
            (HttpStatusCode.OK,
                """[{"actorId":"a-1"},{"actorId":"a-2"},{"actorId":""}]""",
                "application/json"));
        using var client = BuildClient(handler, out _);

        var ids = await client.ListConversationsAsync(Scope, CancellationToken.None);

        Assert.Equal(new[] { "a-1", "a-2" }, ids);
    }

    [Fact]
    public async Task DeleteConversationSendsDeleteRequest()
    {
        var handler = new StubHandler((HttpStatusCode.NoContent, string.Empty, "text/plain"));
        using var client = BuildClient(handler, out _);

        await client.DeleteConversationAsync(Scope, "a-1", CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.EndsWith($"/api/scopes/{Scope}/nyxid-chat/conversations/a-1", request.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task StreamMessageSendsPromptAndYieldsFrames()
    {
        const string sse = """
            data: {"type":"RUN_STARTED","actorId":"a-1"}

            data: {"type":"TEXT_MESSAGE_CONTENT","textMessageContent":{"delta":"hi"}}

            data: {"type":"RUN_FINISHED"}

            """;
        var handler = new StubHandler((HttpStatusCode.OK, sse, "text/event-stream"));
        using var client = BuildClient(handler, out _);

        var collected = new List<AevatarChatFrameType>();
        await foreach (var frame in client.StreamMessageAsync(Scope, "a-1", "hello", sessionId: null, CancellationToken.None))
            collected.Add(frame.Type);

        Assert.Equal(
            new[]
            {
                AevatarChatFrameType.RunStarted,
                AevatarChatFrameType.TextMessageContent,
                AevatarChatFrameType.RunFinished,
            },
            collected);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.EndsWith($"/api/scopes/{Scope}/nyxid-chat/conversations/a-1:stream", request.RequestUri!.AbsolutePath);
        Assert.Contains("text/event-stream", request.Headers.Accept.ToString());
        var body = await request.Content!.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal("hello", json.RootElement.GetProperty("prompt").GetString());
    }

    [Fact]
    public async Task SaveHistoryPutsMetaAndMessagesPayload()
    {
        var handler = new StubHandler((HttpStatusCode.NoContent, string.Empty, "text/plain"));
        using var client = BuildClient(handler, out _);

        var meta = new AevatarConversationMeta
        {
            Id = "a-1",
            Title = "smoke",
            ServiceId = "nyxid-chat",
            ServiceKind = "nyxid-chat",
            CreatedAt = "2026-04-18T00:00:00Z",
            UpdatedAt = "2026-04-18T00:00:01Z",
            MessageCount = 2,
        };
        var messages = new List<AevatarStoredMessage>
        {
            new() { Id = "m1", Role = "user", Content = "hi", Timestamp = 1 },
            new() { Id = "m2", Role = "assistant", Content = "yo", Timestamp = 2 },
        };

        await client.SaveHistoryAsync(Scope, "a-1", meta, messages, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.EndsWith($"/api/scopes/{Scope}/chat-history/conversations/a-1", request.RequestUri!.AbsolutePath);

        var body = await request.Content!.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal("smoke", json.RootElement.GetProperty("meta").GetProperty("title").GetString());
        Assert.Equal(2, json.RootElement.GetProperty("messages").GetArrayLength());
        Assert.Equal("user", json.RootElement.GetProperty("messages")[0].GetProperty("role").GetString());
    }

    [Fact]
    public async Task ListHistoryParsesConversationMeta()
    {
        var handler = new StubHandler(
            (HttpStatusCode.OK,
                """
                {
                  "conversations": [
                    {
                      "id": "a-1",
                      "actorId": "a-1",
                      "title": "t",
                      "serviceId": "nyxid-chat",
                      "serviceKind": "nyxid-chat",
                      "createdAt": "2026-04-17T00:00:00Z",
                      "updatedAt": "2026-04-18T00:00:00Z",
                      "messageCount": 4
                    }
                  ]
                }
                """,
                "application/json"));
        using var client = BuildClient(handler, out _);

        var conversations = await client.ListHistoryAsync(Scope, CancellationToken.None);

        var meta = Assert.Single(conversations);
        Assert.Equal("a-1", meta.Id);
        Assert.Equal("t", meta.Title);
        Assert.Equal(4, meta.MessageCount);
    }

    [Fact]
    public async Task GetHistoryReturnsEmptyWhenBodyIsNull()
    {
        var handler = new StubHandler((HttpStatusCode.OK, "null", "application/json"));
        using var client = BuildClient(handler, out _);

        var messages = await client.GetHistoryAsync(Scope, "a-1", CancellationToken.None);

        Assert.Empty(messages);
    }

    [Fact]
    public async Task DeleteHistorySendsDeleteRequest()
    {
        var handler = new StubHandler((HttpStatusCode.NoContent, string.Empty, "text/plain"));
        using var client = BuildClient(handler, out _);

        await client.DeleteHistoryAsync(Scope, "a-1", CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.EndsWith($"/api/scopes/{Scope}/chat-history/conversations/a-1", request.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task ApproveToolCallYieldsContinuationFrames()
    {
        const string sse = """
            data: {"type":"TEXT_MESSAGE_CONTENT","textMessageContent":{"delta":"ok"}}

            data: {"type":"RUN_FINISHED"}

            """;
        var handler = new StubHandler((HttpStatusCode.OK, sse, "text/event-stream"));
        using var client = BuildClient(handler, out _);

        var count = 0;
        await foreach (var _ in client.ApproveToolCallAsync(Scope, "a-1", "r-1", approved: true, reason: null, sessionId: null, CancellationToken.None))
            count++;

        Assert.Equal(2, count);
        var request = Assert.Single(handler.Requests);
        Assert.EndsWith(":approve", request.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task FailedRequestThrowsAevatarChatExceptionWithTruncatedBody()
    {
        var longBody = new string('x', 800);
        var handler = new StubHandler((HttpStatusCode.Forbidden, longBody, "text/plain"));
        using var client = BuildClient(handler, out _);

        var ex = await Assert.ThrowsAsync<AevatarChatException>(
            () => client.CreateConversationAsync(Scope, CancellationToken.None));

        Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);
        Assert.Contains("403", ex.Message);
        Assert.Contains("xxx...", ex.Message);
    }

    // ── Plumbing ──

    private static AevatarChatClient BuildClient(StubHandler handler, out HttpClient http)
    {
        http = new HttpClient(handler);
        var tokenProvider = BuildTokenProvider();
        return new AevatarChatClient(BaseUrl, tokenProvider, http);
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
        var authService = new NyxIdAuthService(
            credentialStore: store);
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

    private sealed class StubHandler(params (HttpStatusCode Status, string Body, string ContentType)[] responses) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        private int _index;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(await CloneRequestAsync(request, cancellationToken));
            var idx = Math.Min(_index, responses.Length - 1);
            if (_index < responses.Length - 1) _index++;
            var (status, body, contentType) = responses[idx];
            var response = new HttpResponseMessage(status);
            if (!string.IsNullOrEmpty(body))
                response.Content = new StringContent(body, Encoding.UTF8, contentType);
            else
                response.Content = new StringContent(string.Empty);
            return response;
        }

        private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage source, CancellationToken ct)
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

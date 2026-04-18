using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aexon.Core.Auth;

namespace Aexon.Core.Aevatar;

/// <summary>
/// HTTP client for an aevatar backend's NyxID-chat endpoints.
/// Mirrors the endpoints exposed by Aevatar.GAgents.NyxidChat/NyxIdChatEndpoints:
///   POST   /api/scopes/{scope}/nyxid-chat/conversations
///   GET    /api/scopes/{scope}/nyxid-chat/conversations
///   POST   /api/scopes/{scope}/nyxid-chat/conversations/{actor}:stream
///   DELETE /api/scopes/{scope}/nyxid-chat/conversations/{actor}
///   POST   /api/scopes/{scope}/nyxid-chat/conversations/{actor}:approve
/// Authenticates with the user's NyxID bearer token, resolved through
/// <see cref="NyxIdTokenProvider"/> so refresh is handled transparently.
/// </summary>
public sealed class AevatarChatClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly NyxIdTokenProvider _tokenProvider;
    private readonly bool _ownsHttpClient;

    public AevatarChatClient(string baseUrl, NyxIdTokenProvider tokenProvider, HttpClient? httpClient = null)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new ArgumentException("baseUrl is required", nameof(baseUrl));

        _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
        _ownsHttpClient = httpClient is null;
        _http = httpClient ?? new HttpClient();
        _http.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
    }

    public Uri BaseAddress => _http.BaseAddress!;

    public async Task<string> CreateConversationAsync(string scopeId, CancellationToken ct)
    {
        using var request = await BuildRequestAsync(
            HttpMethod.Post,
            $"api/scopes/{Uri.EscapeDataString(scopeId)}/nyxid-chat/conversations",
            content: null,
            ct);

        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);

        var payload = await response.Content.ReadFromJsonAsync<ActorIdResponse>(JsonOptions, ct);
        if (payload is null || string.IsNullOrWhiteSpace(payload.ActorId))
            throw new InvalidOperationException("aevatar backend returned an empty actorId.");
        return payload.ActorId;
    }

    public async Task<IReadOnlyList<string>> ListConversationsAsync(string scopeId, CancellationToken ct)
    {
        using var request = await BuildRequestAsync(
            HttpMethod.Get,
            $"api/scopes/{Uri.EscapeDataString(scopeId)}/nyxid-chat/conversations",
            content: null,
            ct);

        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);

        var items = await response.Content.ReadFromJsonAsync<ActorIdResponse[]>(JsonOptions, ct);
        return items is null
            ? Array.Empty<string>()
            : items
                .Where(item => !string.IsNullOrWhiteSpace(item.ActorId))
                .Select(item => item.ActorId!)
                .ToArray();
    }

    public async Task DeleteConversationAsync(string scopeId, string actorId, CancellationToken ct)
    {
        using var request = await BuildRequestAsync(
            HttpMethod.Delete,
            $"api/scopes/{Uri.EscapeDataString(scopeId)}/nyxid-chat/conversations/{Uri.EscapeDataString(actorId)}",
            content: null,
            ct);

        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);
    }

    // ── Chat history (actor-backed projection on the aevatar side) ──

    /// <summary>
    /// Lists saved conversations for the scope, newest-updated first.
    /// Backs the console sidebar.
    /// </summary>
    public async Task<IReadOnlyList<AevatarConversationMeta>> ListHistoryAsync(
        string scopeId,
        CancellationToken ct)
    {
        using var request = await BuildRequestAsync(
            HttpMethod.Get,
            $"api/scopes/{Uri.EscapeDataString(scopeId)}/chat-history",
            content: null,
            ct);

        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);

        var payload = await response.Content.ReadFromJsonAsync<HistoryIndexResponse>(JsonOptions, ct);
        return payload?.Conversations
               ?? Array.Empty<AevatarConversationMeta>();
    }

    public async Task<IReadOnlyList<AevatarStoredMessage>> GetHistoryAsync(
        string scopeId,
        string conversationId,
        CancellationToken ct)
    {
        using var request = await BuildRequestAsync(
            HttpMethod.Get,
            $"api/scopes/{Uri.EscapeDataString(scopeId)}/chat-history/conversations/{Uri.EscapeDataString(conversationId)}",
            content: null,
            ct);

        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);

        var messages = await response.Content.ReadFromJsonAsync<AevatarStoredMessage[]>(JsonOptions, ct);
        return messages ?? Array.Empty<AevatarStoredMessage>();
    }

    public async Task SaveHistoryAsync(
        string scopeId,
        string conversationId,
        AevatarConversationMeta meta,
        IReadOnlyList<AevatarStoredMessage> messages,
        CancellationToken ct)
    {
        var body = new SaveHistoryBody(meta, messages);
        using var request = await BuildRequestAsync(
            HttpMethod.Put,
            $"api/scopes/{Uri.EscapeDataString(scopeId)}/chat-history/conversations/{Uri.EscapeDataString(conversationId)}",
            JsonContent.Create(body, options: JsonOptions),
            ct);

        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);
    }

    public async Task DeleteHistoryAsync(string scopeId, string conversationId, CancellationToken ct)
    {
        using var request = await BuildRequestAsync(
            HttpMethod.Delete,
            $"api/scopes/{Uri.EscapeDataString(scopeId)}/chat-history/conversations/{Uri.EscapeDataString(conversationId)}",
            content: null,
            ct);

        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);
    }

    /// <summary>
    /// POSTs the prompt to the <c>:stream</c> endpoint and yields parsed SSE frames
    /// as they arrive. The stream ends when the server closes the connection, typically
    /// after a <c>RUN_FINISHED</c> or <c>RUN_ERROR</c> frame.
    /// </summary>
    public async IAsyncEnumerable<AevatarChatFrame> StreamMessageAsync(
        string scopeId,
        string actorId,
        string prompt,
        string? sessionId = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var body = new StreamRequestBody(prompt, sessionId);
        using var request = await BuildRequestAsync(
            HttpMethod.Post,
            $"api/scopes/{Uri.EscapeDataString(scopeId)}/nyxid-chat/conversations/{Uri.EscapeDataString(actorId)}:stream",
            JsonContent.Create(body, options: JsonOptions),
            ct);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        await EnsureSuccessAsync(response, ct);

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        await foreach (var frame in AevatarSseReader.ReadAsync(stream, ct))
            yield return frame;
    }

    /// <summary>
    /// Sends an approval decision for a tool-call request; the response is the SSE
    /// continuation stream of the follow-up turn.
    /// </summary>
    public async IAsyncEnumerable<AevatarChatFrame> ApproveToolCallAsync(
        string scopeId,
        string actorId,
        string requestId,
        bool approved,
        string? reason = null,
        string? sessionId = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var body = new ApprovalRequestBody(requestId, approved, reason ?? string.Empty, sessionId ?? string.Empty);
        using var request = await BuildRequestAsync(
            HttpMethod.Post,
            $"api/scopes/{Uri.EscapeDataString(scopeId)}/nyxid-chat/conversations/{Uri.EscapeDataString(actorId)}:approve",
            JsonContent.Create(body, options: JsonOptions),
            ct);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        await EnsureSuccessAsync(response, ct);

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        await foreach (var frame in AevatarSseReader.ReadAsync(stream, ct))
            yield return frame;
    }

    private async Task<HttpRequestMessage> BuildRequestAsync(
        HttpMethod method,
        string relativePath,
        HttpContent? content,
        CancellationToken ct)
    {
        var token = await _tokenProvider.GetValidAccessTokenAsync(ct);
        var request = new HttpRequestMessage(method, relativePath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (content is not null)
            request.Content = content;
        return request;
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

        var truncated = body.Length > 500 ? body[..500] + "..." : body;
        var detail = string.IsNullOrWhiteSpace(truncated) ? string.Empty : $" — {truncated}";
        throw new AevatarChatException(
            response.StatusCode,
            $"aevatar request failed: {(int)response.StatusCode} {response.StatusCode}{detail}");
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
            _http.Dispose();
    }

    private sealed record ActorIdResponse(
        [property: JsonPropertyName("actorId")] string? ActorId);

    private sealed record StreamRequestBody(
        [property: JsonPropertyName("prompt")] string Prompt,
        [property: JsonPropertyName("sessionId")] string? SessionId);

    private sealed record ApprovalRequestBody(
        [property: JsonPropertyName("requestId")] string RequestId,
        [property: JsonPropertyName("approved")] bool Approved,
        [property: JsonPropertyName("reason")] string Reason,
        [property: JsonPropertyName("sessionId")] string SessionId);

    private sealed record HistoryIndexResponse(
        [property: JsonPropertyName("conversations")] AevatarConversationMeta[]? Conversations);

    private sealed record SaveHistoryBody(
        [property: JsonPropertyName("meta")] AevatarConversationMeta Meta,
        [property: JsonPropertyName("messages")] IReadOnlyList<AevatarStoredMessage> Messages);
}

/// <summary>
/// Mirrors the frontend <c>ConversationMeta</c> shape (chatTypes.ts:98–109). Used
/// to populate the sidebar and persist LLM overrides with the conversation.
/// </summary>
public sealed record AevatarConversationMeta
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("actorId")]
    public string? ActorId { get; init; }

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("serviceId")]
    public string ServiceId { get; init; } = "nyxid-chat";

    [JsonPropertyName("serviceKind")]
    public string ServiceKind { get; init; } = "nyxid-chat";

    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; init; } = string.Empty;

    [JsonPropertyName("updatedAt")]
    public string UpdatedAt { get; init; } = string.Empty;

    [JsonPropertyName("messageCount")]
    public int MessageCount { get; init; }

    [JsonPropertyName("llmRoute")]
    public string? LlmRoute { get; init; }

    [JsonPropertyName("llmModel")]
    public string? LlmModel { get; init; }
}

/// <summary>
/// Mirrors the frontend <c>StoredChatMessage</c> shape (chatTypes.ts:111–123).
/// Only the fields the CLI actually produces or renders are modelled; extra
/// server-side fields (thinking, attachments, mediaParts) are preserved when
/// present via <see cref="System.Text.Json.Serialization.JsonExtensionDataAttribute"/>.
/// </summary>
public sealed record AevatarStoredMessage
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyName("content")]
    public string Content { get; init; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public long Timestamp { get; init; }

    [JsonPropertyName("status")]
    public string Status { get; init; } = "complete";

    [JsonPropertyName("authorId")]
    public string? AuthorId { get; init; }

    [JsonPropertyName("authorName")]
    public string? AuthorName { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }

    [JsonPropertyName("thinking")]
    public string? Thinking { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extras { get; init; }
}

public sealed class AevatarChatException : Exception
{
    public AevatarChatException(HttpStatusCode statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}

/// <summary>
/// Parsed SSE frame. The nyxid-chat SSE writer emits JSON of shape
/// <c>{ "type": "TEXT_MESSAGE_CONTENT", "textMessageContent": { "delta": "..." }, ... }</c>;
/// we surface <see cref="Type"/> as a strongly-typed enum where possible and keep the
/// raw payload so callers can inspect frame-specific fields (tool call id, actor id, etc.)
/// without having to redefine every shape.
/// </summary>
public sealed record AevatarChatFrame(AevatarChatFrameType Type, string RawType, JsonElement Payload)
{
    public string? TryGetString(params string[] path)
    {
        var node = Payload;
        foreach (var segment in path)
        {
            if (node.ValueKind != JsonValueKind.Object)
                return null;
            if (!node.TryGetProperty(segment, out var next))
                return null;
            node = next;
        }

        return node.ValueKind == JsonValueKind.String ? node.GetString() : null;
    }
}

public enum AevatarChatFrameType
{
    Unknown,
    RunStarted,
    RunFinished,
    RunError,
    TextMessageStart,
    TextMessageContent,
    TextMessageEnd,
    ToolCallStart,
    ToolCallEnd,
    ToolApprovalRequest,
    MediaContent,
    StepStarted,
    StepFinished,
    HumanInputRequest,
}

/// <summary>
/// Minimal SSE reader. Parses the <c>data:</c> lines the aevatar backend emits; ignores
/// comments and keep-alive pings. Assumes UTF-8 (the backend sets charset explicitly).
/// </summary>
internal static class AevatarSseReader
{
    public static async IAsyncEnumerable<AevatarChatFrame> ReadAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        var dataBuffer = new StringBuilder();

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(ct);
            if (line is null)
                break;

            if (line.Length == 0)
            {
                if (dataBuffer.Length == 0)
                    continue;

                var json = dataBuffer.ToString();
                dataBuffer.Clear();

                if (string.Equals(json, "[DONE]", StringComparison.Ordinal))
                    yield break;

                AevatarChatFrame? frame = null;
                try
                {
                    frame = ParseFrame(json);
                }
                catch (JsonException)
                {
                    // Skip malformed frame; server shouldn't emit these but be robust.
                }

                if (frame is not null)
                    yield return frame;

                continue;
            }

            if (line[0] == ':')
                continue; // SSE comment / keep-alive

            const string prefix = "data:";
            if (line.StartsWith(prefix, StringComparison.Ordinal))
            {
                var payload = line[prefix.Length..];
                if (payload.StartsWith(' '))
                    payload = payload[1..];
                if (dataBuffer.Length > 0)
                    dataBuffer.Append('\n');
                dataBuffer.Append(payload);
            }
        }

        if (dataBuffer.Length > 0)
        {
            AevatarChatFrame? tail = null;
            try
            {
                tail = ParseFrame(dataBuffer.ToString());
            }
            catch (JsonException)
            {
            }

            if (tail is not null)
                yield return tail;
        }
    }

    private static AevatarChatFrame ParseFrame(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement.Clone();
        var rawType = root.ValueKind == JsonValueKind.Object &&
                      root.TryGetProperty("type", out var typeProp) &&
                      typeProp.ValueKind == JsonValueKind.String
            ? typeProp.GetString() ?? string.Empty
            : string.Empty;

        var type = rawType switch
        {
            "RUN_STARTED" => AevatarChatFrameType.RunStarted,
            "RUN_FINISHED" => AevatarChatFrameType.RunFinished,
            "RUN_ERROR" => AevatarChatFrameType.RunError,
            "TEXT_MESSAGE_START" => AevatarChatFrameType.TextMessageStart,
            "TEXT_MESSAGE_CONTENT" => AevatarChatFrameType.TextMessageContent,
            "TEXT_MESSAGE_END" => AevatarChatFrameType.TextMessageEnd,
            "TOOL_CALL_START" => AevatarChatFrameType.ToolCallStart,
            "TOOL_CALL_END" => AevatarChatFrameType.ToolCallEnd,
            "TOOL_APPROVAL_REQUEST" => AevatarChatFrameType.ToolApprovalRequest,
            "MEDIA_CONTENT" => AevatarChatFrameType.MediaContent,
            "STEP_STARTED" => AevatarChatFrameType.StepStarted,
            "STEP_FINISHED" => AevatarChatFrameType.StepFinished,
            "HUMAN_INPUT_REQUEST" => AevatarChatFrameType.HumanInputRequest,
            _ => AevatarChatFrameType.Unknown,
        };

        return new AevatarChatFrame(type, rawType, root);
    }
}

using System.Net;
using System.Text;
using System.Text.Json;
using Anthropic;
using ClaudeSharp.Core.Commands;
using ClaudeSharp.Core.Compaction;
using ClaudeSharp.Core.Context;
using ClaudeSharp.Core.Hooks;
using ClaudeSharp.Core.Memory;
using ClaudeSharp.Core.Messages;
using ClaudeSharp.Core.Permissions;
using ClaudeSharp.Core.Query;
using ClaudeSharp.Core.Storage;
using ClaudeSharp.Core.Tools;

namespace ClaudeSharp.Core.Tests.Runtime;

/// <summary>
/// Provides shared helpers for runtime tests.
/// </summary>
internal static class TestSupport
{
    public static JsonElement Json(object value) =>
        JsonSerializer.SerializeToElement(value);

    public static AnthropicClient CreateAnthropicClient(HttpMessageHandler handler)
    {
        return new AnthropicClient
        {
            ApiKey = "test-key",
            HttpClient = new HttpClient(handler, disposeHandler: false),
        };
    }

    public static QueryEngine CreateQueryEngine(
        AnthropicClient client,
        ToolRegistry tools,
        ContextProvider contextProvider,
        IPermissionChecker permissions,
        QueryEngineConfig? config = null,
        IConversationJournal? journal = null,
        IReadOnlyList<ConversationMessage>? initialMessages = null,
        TokenUsage? initialUsage = null,
        ConversationSessionMetadata? initialMetadata = null,
        IToolRuntime? toolRuntime = null,
        IConversationCompactor? compactor = null,
        IMicroCompactor? microCompactor = null,
        ISessionMemoryCompactor? sessionMemoryCompactor = null,
        IContextPressurePipeline? contextPressurePipeline = null,
        IHookRuntime? hooks = null,
        SessionMemoryFile? sessionMemoryFile = null)
    {
        return new QueryEngine(
            client,
            tools,
            permissions,
            config ?? new QueryEngineConfig(),
            contextProvider,
            toolRuntime: toolRuntime,
            compactor: compactor,
            microCompactor: microCompactor,
            sessionMemoryCompactor: sessionMemoryCompactor,
            contextPressurePipeline: contextPressurePipeline,
            hooks: hooks,
            journal: journal,
            sessionMemoryFile: sessionMemoryFile,
            initialMessages: initialMessages,
            initialUsage: initialUsage,
            initialMetadata: initialMetadata);
    }
}

/// <summary>
/// Represents temp directory.
/// </summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory(string? name = null)
    {
        Root = Path.Combine(
            Path.GetTempPath(),
            "claudesharp-tests",
            name ?? Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string FullPath(params string[] parts) =>
        parts.Aggregate(Root, Path.Combine);

    public string WriteFile(string relativePath, string content)
    {
        var path = FullPath(relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public string CreateDirectory(string relativePath)
    {
        var path = FullPath(relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        Directory.CreateDirectory(path);
        return path;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
        catch
        {
            // best effort cleanup for temp test data
        }
    }
}

/// <summary>
/// Provides fake tool.
/// </summary>
internal sealed class FakeTool : ITool
{
    public required string Name { get; init; }
    public string[] Aliases { get; init; } = [];
    public string Description { get; init; } = "fake tool";
    public string PromptText { get; init; } = "fake prompt";
    public JsonElement InputSchema { get; init; } = TestSupport.Json(new { type = "object" });
    public bool Enabled { get; init; } = true;
    public bool ReadOnly { get; init; }
    public bool ConcurrencySafe { get; init; }
    public int MaxResultSize { get; init; } = 100_000;
    public Func<JsonElement, ToolExecutionContext, IProgress<ToolProgress>?, CancellationToken, Task<ToolResult>>? ExecuteHandler { get; init; }
    public Func<JsonElement, ToolExecutionContext, Task<ValidationResult>>? ValidateHandler { get; init; }
    public Func<JsonElement, ToolExecutionContext, Task<PermissionResult>>? PermissionHandler { get; init; }
    public List<JsonElement> ExecutedInputs { get; } = [];

    public Task<string> GetDescriptionAsync() => Task.FromResult(Description);

    public JsonElement GetInputSchema() => InputSchema;

    public Task<string> GetPromptAsync(ToolPromptContext context) => Task.FromResult(PromptText);

    public async Task<ToolResult> ExecuteAsync(
        JsonElement input,
        ToolExecutionContext context,
        IProgress<ToolProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ExecutedInputs.Add(input);
        if (ExecuteHandler != null)
            return await ExecuteHandler(input, context, progress, cancellationToken);

        return ToolResult.Success($"{Name}:ok");
    }

    public Task<ValidationResult> ValidateInputAsync(JsonElement input, ToolExecutionContext context) =>
        ValidateHandler?.Invoke(input, context) ?? Task.FromResult(ValidationResult.Valid());

    public Task<PermissionResult> CheckPermissionsAsync(JsonElement input, ToolExecutionContext context) =>
        PermissionHandler?.Invoke(input, context) ?? Task.FromResult(PermissionResult.Allow());

    public bool IsEnabled() => Enabled;

    public bool IsReadOnly(JsonElement input) => ReadOnly;

    public bool IsConcurrencySafe(JsonElement input) => ConcurrencySafe;

    public int MaxResultSizeChars => MaxResultSize;
}

/// <summary>
/// Provides stub permission checker.
/// </summary>
internal sealed class StubPermissionChecker : IPermissionChecker
{
    public Func<ITool, JsonElement, ToolExecutionContext, Task<PermissionResult>>? Handler { get; init; }

    public Task<PermissionResult> CheckAsync(ITool tool, JsonElement input, ToolExecutionContext context) =>
        Handler?.Invoke(tool, input, context) ?? Task.FromResult(PermissionResult.Allow());
}

/// <summary>
/// Represents recording journal.
/// </summary>
internal sealed class RecordingJournal : IConversationJournal
{
    public string SessionId { get; init; } = "session-1";
    public string TranscriptPath { get; init; } = "/tmp/session/transcript.jsonl";
    public ConversationSessionMetadata Metadata { get; } = new();
    public List<ConversationMessage> AppendedMessages { get; } = [];
    public List<string?> ParentMessageIds { get; } = [];
    public List<TranscriptMetadataEntry> MetadataEntries { get; } = [];
    public List<(string WorkingDirectory, string Model)> SessionUpdates { get; } = [];
    public int ResetHeadCount { get; private set; }
    public int CheckpointCount { get; private set; }
    public int MicrocompactCount { get; private set; }
    public ConversationMessage? LastCheckpointSummary { get; private set; }
    public IReadOnlyList<ConversationMessage>? LastCheckpointActiveMessages { get; private set; }
    public IReadOnlyList<MicrocompactEdit>? LastMicrocompactEdits { get; private set; }

    public Task AppendMessageAsync(
        ConversationMessage message,
        string workingDirectory,
        string model,
        CancellationToken cancellationToken = default)
    {
        AppendedMessages.Add(message);
        ParentMessageIds.Add(AppendedMessages.Count > 1 ? AppendedMessages[^2].Id : null);
        SessionUpdates.Add((workingDirectory, model));
        return Task.CompletedTask;
    }

    public Task UpdateSessionInfoAsync(
        string workingDirectory,
        string model,
        CancellationToken cancellationToken = default)
    {
        SessionUpdates.Add((workingDirectory, model));
        return Task.CompletedTask;
    }

    public Task UpdateMetadataAsync(
        Action<ConversationSessionMetadata> update,
        CancellationToken cancellationToken = default)
    {
        update(Metadata);
        return Task.CompletedTask;
    }

    public Task AppendMetadataEntryAsync(
        TranscriptMetadataEntry entry,
        CancellationToken cancellationToken = default)
    {
        MetadataEntries.Add(entry);
        return Task.CompletedTask;
    }

    public Task SeedAsync(
        IReadOnlyList<ConversationMessage> messages,
        ConversationSessionMetadata metadata,
        string workingDirectory,
        string model,
        CancellationToken cancellationToken = default)
    {
        AppendedMessages.AddRange(messages);
        Metadata.Title = metadata.Title;
        Metadata.Mode = metadata.Mode;
        foreach (var tag in metadata.Tags)
            Metadata.Tags.Add(tag);
        SessionUpdates.Add((workingDirectory, model));
        return Task.CompletedTask;
    }

    public Task RecordConversationCheckpointAsync(
        ConversationMessage summaryMessage,
        IReadOnlyList<ConversationMessage> activeMessages,
        string workingDirectory,
        string model,
        CancellationToken cancellationToken = default)
    {
        CheckpointCount++;
        LastCheckpointSummary = summaryMessage;
        LastCheckpointActiveMessages = activeMessages.ToArray();
        SessionUpdates.Add((workingDirectory, model));
        return Task.CompletedTask;
    }

    public Task RecordMicrocompactAsync(
        IReadOnlyList<MicrocompactEdit> edits,
        string workingDirectory,
        string model,
        CancellationToken cancellationToken = default)
    {
        MicrocompactCount++;
        LastMicrocompactEdits = edits.ToArray();
        SessionUpdates.Add((workingDirectory, model));
        return Task.CompletedTask;
    }

    public Task ResetHeadAsync(CancellationToken cancellationToken = default)
    {
        ResetHeadCount++;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Represents fake anthropic handler.
/// </summary>
internal sealed class FakeAnthropicHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> _actions = new();

    public List<HttpRequestMessage> Requests { get; } = [];
    public List<string> Bodies { get; } = [];

    public void EnqueueResponse(HttpResponseMessage response) =>
        _actions.Enqueue(() => response);

    public void EnqueueException(Exception exception) =>
        _actions.Enqueue(() => throw exception);

    public static HttpResponseMessage CreateMessageResponse(
        string text = "ok",
        int inputTokens = 1,
        int outputTokens = 1,
        int cacheReadInputTokens = 0,
        int cacheCreationInputTokens = 0)
    {
        var payload = JsonSerializer.Serialize(new
        {
            id = "msg-1",
            type = "message",
            role = "assistant",
            model = "claude-sonnet-4-6",
            stop_reason = "end_turn",
            stop_sequence = (string?)null,
            content = new object[]
            {
                new { type = "text", text },
            },
            usage = new
            {
                input_tokens = inputTokens,
                output_tokens = outputTokens,
                cache_read_input_tokens = cacheReadInputTokens,
                cache_creation_input_tokens = cacheCreationInputTokens,
            },
        });

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);
        if (request.Content != null)
            Bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));

        if (_actions.Count > 0)
            return _actions.Dequeue()();

        return CreateMessageResponse();
    }
}

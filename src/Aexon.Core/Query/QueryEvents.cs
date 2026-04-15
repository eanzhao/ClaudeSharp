namespace Aexon.Core.Query;

/// <summary>
/// Represents query event.
/// </summary>
public abstract record QueryEvent;

// Streaming events

/// <summary>
/// Represents text delta event.
/// </summary>
public record TextDeltaEvent(string Text) : QueryEvent;

/// <summary>
/// Represents thinking delta event.
/// </summary>
public record ThinkingDeltaEvent(string Text) : QueryEvent;

/// <summary>
/// Represents message start event.
/// </summary>
public record MessageStartEvent(string MessageId) : QueryEvent;

/// <summary>
/// Represents message end event.
/// </summary>
public record MessageEndEvent(string? StopReason, Messages.TokenUsage? Usage) : QueryEvent;

/// <summary>
/// Represents prompt cache status update.
/// </summary>
public record PromptCacheStatusEvent(Messages.TokenUsage Usage, bool BreakDetected) : QueryEvent;

// Tool events

/// <summary>
/// Represents tool use start event.
/// </summary>
public record ToolUseStartEvent(string ToolUseId, string ToolName, System.Text.Json.JsonElement Input) : QueryEvent;

/// <summary>
/// Represents tool progress event.
/// </summary>
public record ToolProgressEvent(string ToolUseId, string Message) : QueryEvent;

/// <summary>
/// Represents tool result event.
/// </summary>
public record ToolResultEvent(string ToolUseId, string ToolName, string Result, bool IsError) : QueryEvent;

/// <summary>
/// Represents context compaction event.
/// </summary>
public record ContextCompactionEvent : QueryEvent
{
    public required string Mode { get; init; }
    public required bool Automatic { get; init; }
    public required string Reason { get; init; }
    public int ClearedToolResults { get; init; }
    public int ClearedThinkingBlocks { get; init; }
    public int RemovedMessages { get; init; }
    public int PreservedMessages { get; init; }
}

// Permission events

/// <summary>
/// Represents permission request event.
/// </summary>
public record PermissionRequestEvent : QueryEvent
{
    public required string ToolName { get; init; }
    public required string Description { get; init; }
    public required System.Text.Json.JsonElement Input { get; init; }

    private readonly TaskCompletionSource<bool> _tcs = new();
    public void SetResponse(bool approved) => _tcs.TrySetResult(approved);
    public Task<bool> WaitForResponseAsync() => _tcs.Task;
}

// System events

/// <summary>
/// Represents query complete event.
/// </summary>
public record QueryCompleteEvent : QueryEvent
{
    public required bool Success { get; init; }
    public required TimeSpan Duration { get; init; }
    public required int TurnCount { get; init; }
    public Messages.TokenUsage TotalUsage { get; init; } = Messages.TokenUsage.Empty;
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Represents status event.
/// </summary>
public record StatusEvent(string Status) : QueryEvent;

// Attachment events

/// <summary>
/// Raised when an attachment is registered in the session.
/// </summary>
public record AttachmentRegisteredEvent : QueryEvent
{
    public required string AttachmentId { get; init; }
    public required string FileName { get; init; }
    public required string MimeType { get; init; }
    public required long SizeBytes { get; init; }
    public required Messages.AttachmentSource Source { get; init; }
}

/// <summary>
/// Raised when an attachment is removed from the session.
/// </summary>
public record AttachmentRemovedEvent(string AttachmentId) : QueryEvent;

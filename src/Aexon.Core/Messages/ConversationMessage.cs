using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aexon.Core.Messages;

/// <summary>
/// Represents a message stored in the conversation transcript.
/// </summary>
public abstract record ConversationMessage
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public abstract string Type { get; }
}

/// <summary>
/// Represents user message.
/// </summary>
public record UserMessage : ConversationMessage
{
    public override string Type => "user";
    public required IReadOnlyList<ContentBlock> Content { get; init; }

    /// <summary>
    /// Gets the raw tool result text attached to the message.
    /// </summary>
    public string? ToolUseResult { get; init; }

    /// <summary>
    /// Gets a value indicating whether the message was generated for internal bookkeeping.
    /// </summary>
    public bool IsMeta { get; init; }

    /// <summary>
    /// Creates a user message that contains a single text block.
    /// </summary>
    public static UserMessage FromText(string text) =>
        new() { Content = [new TextBlock(text)] };

    /// <summary>
    /// Creates a user message that contains a tool result block.
    /// </summary>
    public static UserMessage FromToolResult(string toolUseId, string content, bool isError = false) =>
        new()
        {
            Content = [new ToolResultBlock(toolUseId, content, isError)],
            ToolUseResult = content,
        };
}

/// <summary>
/// Represents assistant message.
/// </summary>
public record AssistantMessage : ConversationMessage
{
    public override string Type => "assistant";
    public required IReadOnlyList<ContentBlock> Content { get; init; }
    public string? StopReason { get; init; }
    public TokenUsage? Usage { get; init; }

    /// <summary>
    /// Gets the API error captured for this message, if any.
    /// </summary>
    public string? ApiError { get; init; }
}

/// <summary>
/// Represents system message.
/// </summary>
public record SystemMessage : ConversationMessage
{
    public override string Type => "system";
    public required string Content { get; init; }
    public string? Subtype { get; init; }
}

// Content block types

/// <summary>
/// Represents a polymorphic content block for LLM message payloads.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TextBlock), "text")]
[JsonDerivedType(typeof(ToolUseBlock), "tool_use")]
[JsonDerivedType(typeof(ToolResultBlock), "tool_result")]
[JsonDerivedType(typeof(ThinkingBlock), "thinking")]
[JsonDerivedType(typeof(AttachmentBlock), "attachment")]
public abstract record ContentBlock
{
    [JsonPropertyName("type")]
    public abstract string Type { get; }
}

/// <summary>
/// Represents text block.
/// </summary>
public record TextBlock(string Text) : ContentBlock
{
    [JsonPropertyName("type")]
    public override string Type => "text";

    [JsonPropertyName("text")]
    public string Text { get; init; } = Text;
}

/// <summary>
/// Represents tool use block.
/// </summary>
public record ToolUseBlock : ContentBlock
{
    [JsonPropertyName("type")]
    public override string Type => "tool_use";

    [JsonPropertyName("id")]
    public required string ToolUseId { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("input")]
    public required JsonElement Input { get; init; }
}

/// <summary>
/// Represents tool result block.
/// </summary>
public record ToolResultBlock : ContentBlock
{
    [JsonPropertyName("type")]
    public override string Type => "tool_result";

    [JsonPropertyName("tool_use_id")]
    public string ToolUseId { get; init; }

    [JsonPropertyName("content")]
    public string Content { get; init; }

    [JsonPropertyName("is_error")]
    public bool IsError { get; init; }

    public ToolResultBlock(string toolUseId, string content, bool isError = false)
    {
        ToolUseId = toolUseId;
        Content = content;
        IsError = isError;
    }
}

/// <summary>
/// Represents thinking block.
/// </summary>
public record ThinkingBlock(string Text, string? Signature = null) : ContentBlock
{
    [JsonPropertyName("type")]
    public override string Type => "thinking";

    [JsonPropertyName("thinking")]
    public string Text { get; init; } = Text;

    [JsonPropertyName("signature")]
    public string? Signature { get; init; } = Signature;
}

/// <summary>
/// References an attachment registered in the session.
/// </summary>
public record AttachmentBlock : ContentBlock
{
    [JsonPropertyName("type")]
    public override string Type => "attachment";

    [JsonPropertyName("attachment_id")]
    public required string AttachmentId { get; init; }

    [JsonPropertyName("file_name")]
    public required string FileName { get; init; }

    [JsonPropertyName("mime_type")]
    public required string MimeType { get; init; }

    [JsonPropertyName("size_bytes")]
    public required long SizeBytes { get; init; }
}

// ─── Token Usage ──────────────────────────────────────

/// <summary>
/// Represents token usage.
/// </summary>
public record TokenUsage
{
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    public int CacheReadInputTokens { get; init; }
    public int CacheCreationInputTokens { get; init; }

    public int TotalInputTokens => InputTokens + CacheReadInputTokens + CacheCreationInputTokens;

    public double CacheHitRate => TotalInputTokens == 0
        ? 0
        : (double)CacheReadInputTokens / TotalInputTokens;

    public int TotalTokens => InputTokens + OutputTokens + CacheReadInputTokens + CacheCreationInputTokens;

    public static TokenUsage Empty => new();

    public static TokenUsage operator +(TokenUsage a, TokenUsage b) => new()
    {
        InputTokens = a.InputTokens + b.InputTokens,
        OutputTokens = a.OutputTokens + b.OutputTokens,
        CacheReadInputTokens = a.CacheReadInputTokens + b.CacheReadInputTokens,
        CacheCreationInputTokens = a.CacheCreationInputTokens + b.CacheCreationInputTokens,
    };
}

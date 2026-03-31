using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeSharp.Core.Messages;

/// <summary>
/// 消息基类 — 对应 Claude Code 的 Message union type (types/message.ts)
/// Claude Code 使用 TypeScript discriminated union, C# 用 abstract record + 子类
/// </summary>
public abstract record ConversationMessage
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public abstract string Type { get; }
}

/// <summary>
/// 用户消息 — 对应 Claude Code 的 UserMessage
/// </summary>
public record UserMessage : ConversationMessage
{
    public override string Type => "user";
    public required IReadOnlyList<ContentBlock> Content { get; init; }

    /// <summary>
    /// 关联的工具结果 (当此消息是 tool_result 时)
    /// 对应 Claude Code 的 toolUseResult 字段
    /// </summary>
    public string? ToolUseResult { get; init; }

    /// <summary>
    /// 是否为系统注入的元消息 (非用户输入)
    /// 对应 Claude Code 的 isMeta 字段
    /// </summary>
    public bool IsMeta { get; init; }

    /// <summary>便捷构造：从纯文本创建用户消息</summary>
    public static UserMessage FromText(string text) =>
        new() { Content = [new TextBlock(text)] };

    /// <summary>便捷构造：创建 tool_result 消息</summary>
    public static UserMessage FromToolResult(string toolUseId, string content, bool isError = false) =>
        new()
        {
            Content = [new ToolResultBlock(toolUseId, content, isError)],
            ToolUseResult = content,
        };
}

/// <summary>
/// 助手消息 — 对应 Claude Code 的 AssistantMessage
/// </summary>
public record AssistantMessage : ConversationMessage
{
    public override string Type => "assistant";
    public required IReadOnlyList<ContentBlock> Content { get; init; }
    public string? StopReason { get; init; }
    public TokenUsage? Usage { get; init; }

    /// <summary>API 错误标记 (如 max_output_tokens)</summary>
    public string? ApiError { get; init; }
}

/// <summary>
/// 系统消息 — 对应 Claude Code 的 SystemMessage
/// </summary>
public record SystemMessage : ConversationMessage
{
    public override string Type => "system";
    public required string Content { get; init; }
    public string? Subtype { get; init; }
}

// ─── Content Block 类型 ───────────────────────────────

/// <summary>
/// 内容块基类 — 对应 Anthropic API 的 content block 模型
/// Claude Code 中也多处使用 ContentBlockParam
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TextBlock), "text")]
[JsonDerivedType(typeof(ToolUseBlock), "tool_use")]
[JsonDerivedType(typeof(ToolResultBlock), "tool_result")]
[JsonDerivedType(typeof(ThinkingBlock), "thinking")]
public abstract record ContentBlock
{
    [JsonPropertyName("type")]
    public abstract string Type { get; }
}

public record TextBlock(string Text) : ContentBlock
{
    [JsonPropertyName("type")]
    public override string Type => "text";

    [JsonPropertyName("text")]
    public string Text { get; init; } = Text;
}

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

public record ThinkingBlock(string Text, string? Signature = null) : ContentBlock
{
    [JsonPropertyName("type")]
    public override string Type => "thinking";

    [JsonPropertyName("thinking")]
    public string Text { get; init; } = Text;

    [JsonPropertyName("signature")]
    public string? Signature { get; init; } = Signature;
}

// ─── Token Usage ──────────────────────────────────────

/// <summary>
/// Token 使用统计 — 对应 Claude Code 的 cost-tracker.ts
/// </summary>
public record TokenUsage
{
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    public int CacheReadInputTokens { get; init; }
    public int CacheCreationInputTokens { get; init; }

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

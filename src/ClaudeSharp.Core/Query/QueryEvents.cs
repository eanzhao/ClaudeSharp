namespace ClaudeSharp.Core.Query;

/// <summary>
/// 查询事件 — QueryEngine 通过 IAsyncEnumerable 向 UI 层发送的事件流
///
/// 对应 Claude Code 中 query.ts yield 的各种 Message | StreamEvent 类型
/// Claude Code 使用 AsyncGenerator, C# 使用 IAsyncEnumerable
/// </summary>
public abstract record QueryEvent;

// ─── 流式事件 ─────────────────────────────────────────

/// <summary>文本增量（助手正在打字）</summary>
public record TextDeltaEvent(string Text) : QueryEvent;

/// <summary>思考增量（助手正在思考）</summary>
public record ThinkingDeltaEvent(string Text) : QueryEvent;

/// <summary>消息开始（新的 API 响应开始）</summary>
public record MessageStartEvent(string MessageId) : QueryEvent;

/// <summary>消息结束</summary>
public record MessageEndEvent(string? StopReason, Messages.TokenUsage? Usage) : QueryEvent;

// ─── 工具事件 ─────────────────────────────────────────

/// <summary>工具调用开始</summary>
public record ToolUseStartEvent(string ToolUseId, string ToolName, System.Text.Json.JsonElement Input) : QueryEvent;

/// <summary>工具执行进度</summary>
public record ToolProgressEvent(string ToolUseId, string Message) : QueryEvent;

/// <summary>工具结果</summary>
public record ToolResultEvent(string ToolUseId, string ToolName, string Result, bool IsError) : QueryEvent;

// ─── 权限事件 ─────────────────────────────────────────

/// <summary>
/// 权限请求事件 — UI 层需要显示权限对话框并调用 SetResponse
/// 对应 Claude Code 的 useCanUseTool 中用户审批流程
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

// ─── 系统事件 ─────────────────────────────────────────

/// <summary>查询完成</summary>
public record QueryCompleteEvent : QueryEvent
{
    public required bool Success { get; init; }
    public required TimeSpan Duration { get; init; }
    public required int TurnCount { get; init; }
    public Messages.TokenUsage TotalUsage { get; init; } = Messages.TokenUsage.Empty;
    public string? ErrorMessage { get; init; }
}

/// <summary>查询状态变化 (用于 SDK)</summary>
public record StatusEvent(string Status) : QueryEvent;

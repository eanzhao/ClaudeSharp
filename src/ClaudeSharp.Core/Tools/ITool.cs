using System.Text.Json;
using ClaudeSharp.Core.Messages;
using ClaudeSharp.Core.Permissions;

namespace ClaudeSharp.Core.Tools;

/// <summary>
/// 工具接口 — 对应 Claude Code 的 Tool&lt;Input, Output, Progress&gt; 类型 (Tool.ts)
///
/// 设计要点 (从 Claude Code 源码学到的):
/// 1. 每个工具是自包含模块：定义 schema、权限、执行逻辑、UI 渲染
/// 2. inputSchema 用于生成 JSON Schema 发送给 API
/// 3. checkPermissions 在每次工具调用前检查权限
/// 4. prompt() 返回嵌入系统提示的工具说明文字
/// 5. isConcurrencySafe 决定是否可以并行执行
/// </summary>
public interface ITool
{
    /// <summary>工具名称（API tool_use 中的 name 字段）</summary>
    string Name { get; }

    /// <summary>别名（向后兼容用）— 对应 Tool.aliases</summary>
    string[] Aliases => [];

    /// <summary>工具描述（展示给模型看）— 对应 Tool.description()</summary>
    Task<string> GetDescriptionAsync();

    /// <summary>
    /// 获取 JSON Schema（发送给 API 的 tool input schema）
    /// 对应 Tool.inputSchema，Claude Code 用 Zod 自动生成
    /// </summary>
    JsonElement GetInputSchema();

    /// <summary>
    /// 生成系统提示中的工具说明 — 对应 Tool.prompt()
    /// 这是 Claude Code 中非常重要的概念：每个工具负责自己的 prompt
    /// </summary>
    Task<string> GetPromptAsync(ToolPromptContext context);

    /// <summary>
    /// 执行工具 — 对应 Tool.call()
    /// </summary>
    Task<ToolResult> ExecuteAsync(
        JsonElement input,
        ToolExecutionContext context,
        IProgress<ToolProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 输入验证 — 对应 Tool.validateInput()
    /// 在权限检查之前运行
    /// </summary>
    Task<ValidationResult> ValidateInputAsync(JsonElement input, ToolExecutionContext context)
        => Task.FromResult(ValidationResult.Valid());

    /// <summary>
    /// 权限检查 — 对应 Tool.checkPermissions()
    /// 决定是否需要用户批准
    /// </summary>
    Task<PermissionResult> CheckPermissionsAsync(JsonElement input, ToolExecutionContext context)
        => Task.FromResult(PermissionResult.Allow());

    /// <summary>是否启用 — 对应 Tool.isEnabled()</summary>
    bool IsEnabled() => true;

    /// <summary>是否只读操作 — 对应 Tool.isReadOnly()</summary>
    bool IsReadOnly(JsonElement input) => false;

    /// <summary>是否破坏性操作 — 对应 Tool.isDestructive()</summary>
    bool IsDestructive(JsonElement input) => false;

    /// <summary>
    /// 是否可并行执行 — 对应 Tool.isConcurrencySafe()
    /// 默认 false (安全保守)，只读工具应返回 true
    /// </summary>
    bool IsConcurrencySafe(JsonElement input) => false;

    /// <summary>用户可见的工具名 — 对应 Tool.userFacingName()</summary>
    string GetUserFacingName(JsonElement? input = null) => Name;

    /// <summary>活动描述(用于 spinner) — 对应 Tool.getActivityDescription()</summary>
    string? GetActivityDescription(JsonElement? input) => null;

    /// <summary>结果大小上限 — 对应 Tool.maxResultSizeChars</summary>
    int MaxResultSizeChars => 100_000;
}

// ─── 上下文类型 ──────────────────────────────────────

/// <summary>
/// 工具执行上下文 — 对应 Claude Code 的 ToolUseContext (Tool.ts)
/// 传递给每个工具的运行时信息
/// </summary>
public class ToolExecutionContext
{
    /// <summary>当前工作目录</summary>
    public required string WorkingDirectory { get; init; }

    /// <summary>权限上下文</summary>
    public required PermissionContext PermissionContext { get; init; }

    /// <summary>所有已注册的工具</summary>
    public required IReadOnlyList<ITool> Tools { get; init; }

    /// <summary>当前对话中的所有消息</summary>
    public required IReadOnlyList<ConversationMessage> Messages { get; init; }

    /// <summary>中止控制器</summary>
    public required CancellationToken CancellationToken { get; init; }

    /// <summary>是否为非交互式会话 (headless/SDK)</summary>
    public bool IsNonInteractiveSession { get; init; }

    /// <summary>主循环模型名</summary>
    public string MainLoopModel { get; init; } = Query.ClaudeModels.DefaultMainModel;
}

/// <summary>
/// 工具 prompt 生成上下文 — 对应 Tool.prompt() 的参数
/// </summary>
public class ToolPromptContext
{
    public required PermissionContext PermissionContext { get; init; }
    public required IReadOnlyList<ITool> Tools { get; init; }
}

// ─── 结果类型 ────────────────────────────────────────

/// <summary>
/// 工具执行结果 — 对应 Claude Code 的 ToolResult&lt;T&gt;
/// </summary>
public class ToolResult
{
    public required string Data { get; init; }
    public bool IsError { get; init; }
    public IReadOnlyList<ConversationMessage>? NewMessages { get; init; }

    public static ToolResult Success(string data) => new() { Data = data };
    public static ToolResult Error(string message) => new() { Data = message, IsError = true };
}

/// <summary>工具进度报告</summary>
public record ToolProgress(string ToolUseId, string Type, string? Message = null, double? Percentage = null);

/// <summary>
/// 输入验证结果 — 对应 Claude Code 的 ValidationResult
/// </summary>
public record ValidationResult(bool IsValid, string? Message = null)
{
    public static ValidationResult Valid() => new(true);
    public static ValidationResult Invalid(string message) => new(false, message);
}

using System.Text.Json;

namespace ClaudeSharp.Core.Permissions;

/// <summary>
/// 权限模式 — 对应 Claude Code 的 PermissionMode (types/permissions.ts)
///
/// Claude Code 支持多种权限模式，从严格到宽松：
/// - Default: 每个写操作都询问用户
/// - Plan: 进入规划模式, 只读操作自动批准
/// - Auto: 全自动模式 (带安全分类器)
/// - Bypass: 跳过所有权限检查 (危险)
/// </summary>
public enum PermissionMode
{
    /// <summary>默认模式 — 写操作需要用户批准</summary>
    Default,

    /// <summary>规划模式 — 只读操作自动批准</summary>
    Plan,

    /// <summary>自动模式 — 所有操作自动批准（带安全检查）</summary>
    Auto,

    /// <summary>绕过模式 — 跳过所有权限检查</summary>
    Bypass,
}

/// <summary>
/// 权限检查行为 — 对应 Claude Code 的 PermissionResult.behavior
/// </summary>
public enum PermissionBehavior
{
    /// <summary>允许执行</summary>
    Allow,

    /// <summary>拒绝执行</summary>
    Deny,

    /// <summary>需要询问用户</summary>
    Ask,
}

/// <summary>
/// 权限检查结果 — 对应 Claude Code 的 PermissionResult (types/permissions.ts)
/// </summary>
public record PermissionResult
{
    public required PermissionBehavior Behavior { get; init; }
    public JsonElement? UpdatedInput { get; init; }
    public string? Message { get; init; }

    public static PermissionResult Allow(JsonElement? updatedInput = null) =>
        new() { Behavior = PermissionBehavior.Allow, UpdatedInput = updatedInput };

    public static PermissionResult Deny(string? message = null) =>
        new() { Behavior = PermissionBehavior.Deny, Message = message };

    public static PermissionResult Ask(string? message = null) =>
        new() { Behavior = PermissionBehavior.Ask, Message = message };
}

/// <summary>
/// 权限上下文 — 对应 Claude Code 的 ToolPermissionContext (Tool.ts)
///
/// Claude Code 中权限上下文包含:
/// - mode: 当前权限模式
/// - additionalWorkingDirectories: 额外允许的工作目录
/// - alwaysAllowRules / alwaysDenyRules / alwaysAskRules: 规则表
/// </summary>
public class PermissionContext
{
    public PermissionMode Mode { get; set; } = PermissionMode.Default;

    /// <summary>主工作目录</summary>
    public string WorkingDirectory { get; set; } = Environment.CurrentDirectory;

    /// <summary>额外允许的目录</summary>
    public HashSet<string> AdditionalWorkingDirectories { get; } = new();

    /// <summary>始终允许的规则 (如 "Bash(git status)")</summary>
    public HashSet<string> AlwaysAllowRules { get; } = new();

    /// <summary>始终拒绝的规则</summary>
    public HashSet<string> AlwaysDenyRules { get; } = new();
}

/// <summary>
/// 权限检查器接口 — 对应 Claude Code 的 useCanUseTool hook
///
/// Claude Code 中权限检查的流程：
/// 1. 检查 deny 规则 → 直接拒绝
/// 2. 工具自身的 checkPermissions() → 工具特定的逻辑
/// 3. 检查 allow 规则 → 匹配则自动允许
/// 4. 根据权限模式决定默认行为
/// 5. 需要 ask 时，触发 UI 层询问用户
/// </summary>
public interface IPermissionChecker
{
    Task<PermissionResult> CheckAsync(
        Tools.ITool tool,
        JsonElement input,
        Tools.ToolExecutionContext context);
}

/// <summary>
/// 默认权限检查器实现
/// </summary>
public class DefaultPermissionChecker : IPermissionChecker
{
    /// <summary>用户交互回调 (用于 Ask 行为)</summary>
    public Func<Tools.ITool, JsonElement, Task<bool>>? OnPermissionRequest { get; set; }

    public async Task<PermissionResult> CheckAsync(
        Tools.ITool tool,
        JsonElement input,
        Tools.ToolExecutionContext context)
    {
        var permCtx = context.PermissionContext;

        // 1. 检查 deny 规则
        if (permCtx.AlwaysDenyRules.Contains(tool.Name))
            return PermissionResult.Deny($"Tool '{tool.Name}' is denied by policy.");

        // 2. 工具自身的权限检查
        var toolPermission = await tool.CheckPermissionsAsync(input, context);
        if (toolPermission.Behavior == PermissionBehavior.Deny)
            return toolPermission;

        // 3. Bypass 模式 → 全部允许
        if (permCtx.Mode == PermissionMode.Bypass)
            return PermissionResult.Allow(toolPermission.UpdatedInput);

        // 4. 检查 allow 规则
        if (permCtx.AlwaysAllowRules.Contains(tool.Name))
            return PermissionResult.Allow(toolPermission.UpdatedInput);

        // 5. 只读操作在 Plan/Auto 模式下自动允许
        if (tool.IsReadOnly(input) &&
            permCtx.Mode is PermissionMode.Plan or PermissionMode.Auto)
            return PermissionResult.Allow(toolPermission.UpdatedInput);

        // 6. Auto 模式 → 全部允许
        if (permCtx.Mode == PermissionMode.Auto)
            return PermissionResult.Allow(toolPermission.UpdatedInput);

        // 7. 工具自身标记为 Allow → 允许
        if (toolPermission.Behavior == PermissionBehavior.Allow)
            return toolPermission;

        // 8. 需要询问用户
        return PermissionResult.Ask(toolPermission.Message ?? $"Allow {tool.GetUserFacingName(input)}?");
    }
}

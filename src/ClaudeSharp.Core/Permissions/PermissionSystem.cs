using System.Text.Json;

namespace ClaudeSharp.Core.Permissions;

/// <summary>
/// Defines the permission modes supported by the runtime.
/// </summary>
public enum PermissionMode
{
    /// <summary>
    /// The default mode that asks for approval before write operations.
    /// </summary>
    Default,

    /// <summary>
    /// A planning mode that auto-approves read-only operations.
    /// </summary>
    Plan,

    /// <summary>
    /// An automatic mode that allows operations after safety checks.
    /// </summary>
    Auto,

    /// <summary>
    /// A bypass mode that skips permission checks.
    /// </summary>
    Bypass,
}

/// <summary>
/// Defines the possible outcomes of a permission check.
/// </summary>
public enum PermissionBehavior
{
    /// <summary>
    /// Allows the operation to proceed.
    /// </summary>
    Allow,

    /// <summary>
    /// Denies the operation.
    /// </summary>
    Deny,

    /// <summary>
    /// Requires an explicit approval decision.
    /// </summary>
    Ask,
}

/// <summary>
/// Represents the result of a permission check.
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
/// Stores permission mode and rule sets for the current session.
/// </summary>
public class PermissionContext
{
    public PermissionMode Mode { get; set; } = PermissionMode.Default;

    /// <summary>
    /// Gets or sets the primary working directory.
    /// </summary>
    public string WorkingDirectory { get; set; } = Environment.CurrentDirectory;

    /// <summary>
    /// Gets additional working directories that are allowed for the session.
    /// </summary>
    public HashSet<string> AdditionalWorkingDirectories { get; } = new();

    /// <summary>
    /// Gets the legacy always-allow rules.
    /// </summary>
    public HashSet<string> AlwaysAllowRules { get; } = new();

    /// <summary>
    /// Gets the legacy always-ask rules.
    /// </summary>
    public HashSet<string> AlwaysAskRules { get; } = new();

    /// <summary>
    /// Gets the legacy always-deny rules.
    /// </summary>
    public HashSet<string> AlwaysDenyRules { get; } = new();

    /// <summary>
    /// Gets the structured permission rules.
    /// </summary>
    public List<PermissionRule> Rules { get; } = new();

    public void AddRule(PermissionBehavior behavior, string toolName, string? ruleContent = null)
    {
        Rules.Add(PermissionRule.Create(behavior, toolName, ruleContent));
    }

    public IEnumerable<PermissionRule> GetRules(PermissionBehavior behavior)
    {
        foreach (var rule in Rules.Where(rule => rule.Behavior == behavior))
            yield return rule;

        IEnumerable<string> legacyRules = behavior switch
        {
            PermissionBehavior.Allow => AlwaysAllowRules,
            PermissionBehavior.Ask => AlwaysAskRules,
            PermissionBehavior.Deny => AlwaysDenyRules,
            _ => Array.Empty<string>(),
        };

        foreach (var legacy in legacyRules)
            yield return PermissionRule.Parse(behavior, legacy);
    }
}

/// <summary>
/// Defines the contract for permission checkers.
/// </summary>
public interface IPermissionChecker
{
    Task<PermissionResult> CheckAsync(
        Tools.ITool tool,
        JsonElement input,
        Tools.ToolExecutionContext context);
}

/// <summary>
/// Implements the default permission flow.
/// </summary>
public class DefaultPermissionChecker : IPermissionChecker
{
    /// <summary>
    /// Gets or sets the callback used when a tool needs explicit approval.
    /// </summary>
    public Func<Tools.ITool, JsonElement, Task<bool>>? OnPermissionRequest { get; set; }

    public async Task<PermissionResult> CheckAsync(
        Tools.ITool tool,
        JsonElement input,
        Tools.ToolExecutionContext context)
    {
        var permCtx = context.PermissionContext;
        var matchedDenyRule = PermissionRuleMatcher.FindFirstMatch(
            permCtx.GetRules(PermissionBehavior.Deny),
            tool,
            input);

        // 1. Evaluate deny rules first.
        if (matchedDenyRule != null)
            return PermissionResult.Deny($"Denied by rule: {matchedDenyRule.ToExpression()}");

        // 2. Let the tool perform its own permission checks.
        var toolPermission = await tool.CheckPermissionsAsync(input, context);
        if (toolPermission.Behavior == PermissionBehavior.Deny)
            return toolPermission;

        // 3. Bypass mode allows everything.
        if (permCtx.Mode == PermissionMode.Bypass)
            return PermissionResult.Allow(toolPermission.UpdatedInput);

        // 4. Evaluate allow rules.
        var matchedAllowRule = PermissionRuleMatcher.FindFirstMatch(
            permCtx.GetRules(PermissionBehavior.Allow),
            tool,
            input);
        if (matchedAllowRule != null)
            return PermissionResult.Allow(toolPermission.UpdatedInput);

        // 5. Ask rules must run before read-only and auto-mode shortcuts.
        var matchedAskRule = PermissionRuleMatcher.FindFirstMatch(
            permCtx.GetRules(PermissionBehavior.Ask),
            tool,
            input);
        if (matchedAskRule != null)
            return PermissionResult.Ask($"Rule requires confirmation: {matchedAskRule.ToExpression()}");

        // 6. Auto-approve read-only operations in Plan and Auto modes.
        if (tool.IsReadOnly(input) &&
            permCtx.Mode is PermissionMode.Plan or PermissionMode.Auto)
            return PermissionResult.Allow(toolPermission.UpdatedInput);

        // 7. Auto mode allows everything else.
        if (permCtx.Mode == PermissionMode.Auto)
            return PermissionResult.Allow(toolPermission.UpdatedInput);

        // 8. Honor explicit allow results returned by the tool.
        if (toolPermission.Behavior == PermissionBehavior.Allow)
            return toolPermission;

        // 9. Fall back to prompting the user.
        return PermissionResult.Ask(toolPermission.Message ?? $"Allow {tool.GetUserFacingName(input)}?");
    }
}

using System.Text.Json;

namespace ClaudeSharp.Core.Hooks;

/// <summary>
/// Represents pre tool use hook result.
/// </summary>
public sealed record PreToolUseHookResult
{
    public required HookAction Action { get; init; }
    public JsonElement? UpdatedInput { get; init; }
    public string? Message { get; init; }

    public static PreToolUseHookResult Continue(JsonElement? updatedInput = null) =>
        new()
        {
            Action = HookAction.Continue,
            UpdatedInput = updatedInput,
        };

    public static PreToolUseHookResult Block(string? message = null) =>
        new()
        {
            Action = HookAction.Block,
            Message = message,
        };
}

/// <summary>
/// Represents permission request hook result.
/// </summary>
public sealed record PermissionRequestHookResult
{
    public bool? Approved { get; init; }
    public string? Message { get; init; }

    public bool HasDecision => Approved.HasValue;

    public static PermissionRequestHookResult NoDecision() => new();

    public static PermissionRequestHookResult Allow(string? message = null) =>
        new()
        {
            Approved = true,
            Message = message,
        };

    public static PermissionRequestHookResult Deny(string? message = null) =>
        new()
        {
            Approved = false,
            Message = message,
        };
}


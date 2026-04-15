namespace Aexon.Core.Hooks;

/// <summary>
/// Defines hook event kind values.
/// </summary>
public enum HookEventKind
{
    PreToolUse,
    PostToolUse,
    PostToolUseFailure,
    PermissionRequest,
    SessionStart,
    SessionEnd,
    Stop,
    StopFailure,
    PreCompact,
    PostCompact,
}

/// <summary>
/// Defines hook action values.
/// </summary>
public enum HookAction
{
    Continue,
    Block,
}


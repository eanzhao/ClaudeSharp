namespace ClaudeSharp.Core.Hooks;

/// <summary>
/// Provides hook observer.
/// </summary>
public abstract class HookObserver
{
    public virtual ValueTask<PreToolUseHookResult> OnPreToolUseAsync(
        PreToolUseHookContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(PreToolUseHookResult.Continue());

    public virtual ValueTask OnPostToolUseAsync(
        PostToolUseHookContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    public virtual ValueTask OnPostToolUseFailureAsync(
        PostToolUseHookContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    public virtual ValueTask<PermissionRequestHookResult> OnPermissionRequestAsync(
        PermissionRequestHookContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(PermissionRequestHookResult.NoDecision());

    public virtual ValueTask OnSessionStartAsync(
        SessionHookContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    public virtual ValueTask OnSessionEndAsync(
        SessionEndHookContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    public virtual ValueTask OnPreCompactAsync(
        CompactHookContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    public virtual ValueTask OnPostCompactAsync(
        CompactHookContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    public virtual ValueTask OnStopAsync(
        StopHookContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    public virtual ValueTask OnStopFailureAsync(
        StopHookContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;
}


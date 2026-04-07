using System.Text.Json;

namespace ClaudeSharp.Core.Hooks;

/// <summary>
/// Defines the contract for hook runtime.
/// </summary>
public interface IHookRuntime
{
    ValueTask<PreToolUseHookResult> RunPreToolUseAsync(
        PreToolUseHookContext context,
        CancellationToken cancellationToken = default);

    ValueTask OnPostToolUseAsync(
        PostToolUseHookContext context,
        CancellationToken cancellationToken = default);

    ValueTask OnPostToolUseFailureAsync(
        PostToolUseHookContext context,
        CancellationToken cancellationToken = default);

    ValueTask<PermissionRequestHookResult> RunPermissionRequestAsync(
        PermissionRequestHookContext context,
        CancellationToken cancellationToken = default);

    ValueTask OnSessionStartAsync(
        SessionHookContext context,
        CancellationToken cancellationToken = default);

    ValueTask OnSessionEndAsync(
        SessionEndHookContext context,
        CancellationToken cancellationToken = default);

    ValueTask OnPreCompactAsync(
        CompactHookContext context,
        CancellationToken cancellationToken = default);

    ValueTask OnPostCompactAsync(
        CompactHookContext context,
        CancellationToken cancellationToken = default);

    ValueTask OnStopAsync(
        StopHookContext context,
        CancellationToken cancellationToken = default);

    ValueTask OnStopFailureAsync(
        StopHookContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Provides hook runtime.
/// </summary>
public sealed class HookRuntime : IHookRuntime
{
    public static HookRuntime Empty { get; } = new();

    private readonly List<HookObserver> _observers = [];

    public HookRuntime(IEnumerable<HookObserver>? observers = null)
    {
        if (observers != null)
            _observers.AddRange(observers);
    }

    public void Register(HookObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        _observers.Add(observer);
    }

    public async ValueTask<PreToolUseHookResult> RunPreToolUseAsync(
        PreToolUseHookContext context,
        CancellationToken cancellationToken = default)
    {
        var currentInput = context.Input;

        foreach (var observer in _observers)
        {
            context.Input = currentInput;
            var result = await observer.OnPreToolUseAsync(context, cancellationToken);
            if (result.UpdatedInput.HasValue)
                currentInput = result.UpdatedInput.Value;

            if (result.Action == HookAction.Block)
                return PreToolUseHookResult.Block(result.Message) with
                {
                    UpdatedInput = currentInput,
                };
        }

        return PreToolUseHookResult.Continue(currentInput);
    }

    public async ValueTask<PermissionRequestHookResult> RunPermissionRequestAsync(
        PermissionRequestHookContext context,
        CancellationToken cancellationToken = default)
    {
        foreach (var observer in _observers)
        {
            var result = await observer.OnPermissionRequestAsync(context, cancellationToken);
            if (result.HasDecision)
                return result;
        }

        return PermissionRequestHookResult.NoDecision();
    }

    public async ValueTask OnPostToolUseAsync(
        PostToolUseHookContext context,
        CancellationToken cancellationToken = default)
    {
        foreach (var observer in _observers)
            await observer.OnPostToolUseAsync(context, cancellationToken);
    }

    public async ValueTask OnPostToolUseFailureAsync(
        PostToolUseHookContext context,
        CancellationToken cancellationToken = default)
    {
        foreach (var observer in _observers)
            await observer.OnPostToolUseFailureAsync(context, cancellationToken);
    }

    public async ValueTask OnSessionStartAsync(
        SessionHookContext context,
        CancellationToken cancellationToken = default)
    {
        foreach (var observer in _observers)
            await observer.OnSessionStartAsync(context, cancellationToken);
    }

    public async ValueTask OnSessionEndAsync(
        SessionEndHookContext context,
        CancellationToken cancellationToken = default)
    {
        foreach (var observer in _observers)
            await observer.OnSessionEndAsync(context, cancellationToken);
    }

    public async ValueTask OnPreCompactAsync(
        CompactHookContext context,
        CancellationToken cancellationToken = default)
    {
        foreach (var observer in _observers)
            await observer.OnPreCompactAsync(context, cancellationToken);
    }

    public async ValueTask OnPostCompactAsync(
        CompactHookContext context,
        CancellationToken cancellationToken = default)
    {
        foreach (var observer in _observers)
            await observer.OnPostCompactAsync(context, cancellationToken);
    }

    public async ValueTask OnStopAsync(
        StopHookContext context,
        CancellationToken cancellationToken = default)
    {
        foreach (var observer in _observers)
            await observer.OnStopAsync(context, cancellationToken);
    }

    public async ValueTask OnStopFailureAsync(
        StopHookContext context,
        CancellationToken cancellationToken = default)
    {
        foreach (var observer in _observers)
            await observer.OnStopFailureAsync(context, cancellationToken);
    }
}


using Aexon.Core.Memory;

namespace Aexon.Core.Hooks;

/// <summary>
/// Consolidates session memory into team/project memory when a session ends.
/// </summary>
public sealed class SessionMemoryConsolidationHookObserver : HookObserver
{
    private readonly MemoryConsolidationService _service;

    public SessionMemoryConsolidationHookObserver(MemoryConsolidationService service)
    {
        _service = service;
    }

    public override async ValueTask OnSessionEndAsync(
        SessionEndHookContext context,
        CancellationToken cancellationToken = default)
    {
        if (context.DueToClear || string.IsNullOrWhiteSpace(context.SessionId))
            return;

        await _service.ConsolidateSessionAsync(context.SessionId, cancellationToken);
    }
}

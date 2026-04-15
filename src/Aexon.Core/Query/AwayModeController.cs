using Aexon.Core.Messages;

namespace Aexon.Core.Query;

/// <summary>
/// Controls away-mode transitions for the current session.
/// </summary>
public interface IAwayModeController
{
    bool IsAwayModeActive { get; }

    DateTimeOffset? AwayEnteredAt { get; }

    string? AwayTriggerReason { get; }

    Task<bool> EnterAwayModeAsync(string triggerReason, CancellationToken ct = default);

    Task<SystemAwaySummaryMessage?> ExitAwayModeAsync(CancellationToken ct = default);
}

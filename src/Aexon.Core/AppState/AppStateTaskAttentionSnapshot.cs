namespace Aexon.Core.AppState;

/// <summary>
/// Represents structured attention counts for agent work items.
/// </summary>
public sealed record AppStateTaskAttentionSnapshot
{
    public int AwaitingApprovalCount { get; init; }
    public int AwaitingResumeCount { get; init; }
}

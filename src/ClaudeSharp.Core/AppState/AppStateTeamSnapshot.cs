namespace ClaudeSharp.Core.AppState;

/// <summary>
/// Represents a team snapshot projected into app state.
/// </summary>
public sealed record AppStateTeamSnapshot
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? LeadName { get; init; }
    public IReadOnlyList<string> Members { get; init; } = [];
}

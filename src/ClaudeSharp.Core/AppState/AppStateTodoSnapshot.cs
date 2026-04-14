namespace ClaudeSharp.Core.AppState;

/// <summary>
/// Represents a todo snapshot projected into app state.
/// </summary>
public sealed record AppStateTodoSnapshot
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Status { get; init; }
    public string? Description { get; init; }
}

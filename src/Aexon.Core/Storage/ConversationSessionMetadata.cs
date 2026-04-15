using Aexon.Core.Permissions;

namespace Aexon.Core.Storage;

/// <summary>
/// Represents conversation session metadata.
/// </summary>
public sealed class ConversationSessionMetadata
{
    public string? Title { get; set; }

    public HashSet<string> Tags { get; } = new(StringComparer.OrdinalIgnoreCase);

    public PermissionMode? Mode { get; set; }

    public DateTimeOffset? AwayEnteredAt { get; set; }

    public string? AwayTriggerReason { get; set; }

    public ConversationSessionMetadata Clone()
    {
        var clone = new ConversationSessionMetadata
        {
            Title = Title,
            Mode = Mode,
            AwayEnteredAt = AwayEnteredAt,
            AwayTriggerReason = AwayTriggerReason,
        };

        foreach (var tag in Tags)
            clone.Tags.Add(tag);

        return clone;
    }
}

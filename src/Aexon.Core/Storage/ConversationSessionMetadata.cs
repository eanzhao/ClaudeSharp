using Aexon.Core.Messages;
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

    public Query.QueryEffortLevel? Effort { get; set; }

    public DateTimeOffset? AwayEnteredAt { get; set; }

    public string? AwayTriggerReason { get; set; }

    public Dictionary<string, Attachment> Attachments { get; } = new(StringComparer.Ordinal);

    public ConversationSessionMetadata Clone()
    {
        var clone = new ConversationSessionMetadata
        {
            Title = Title,
            Mode = Mode,
            Effort = Effort,
            AwayEnteredAt = AwayEnteredAt,
            AwayTriggerReason = AwayTriggerReason,
        };

        foreach (var tag in Tags)
            clone.Tags.Add(tag);

        foreach (var (id, attachment) in Attachments)
            clone.Attachments[id] = attachment;

        return clone;
    }
}

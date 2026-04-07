using ClaudeSharp.Core.Permissions;

namespace ClaudeSharp.Core.Storage;

public sealed class ConversationSessionMetadata
{
    public string? Title { get; set; }

    public HashSet<string> Tags { get; } = new(StringComparer.OrdinalIgnoreCase);

    public PermissionMode? Mode { get; set; }

    public ConversationSessionMetadata Clone()
    {
        var clone = new ConversationSessionMetadata
        {
            Title = Title,
            Mode = Mode,
        };

        foreach (var tag in Tags)
            clone.Tags.Add(tag);

        return clone;
    }
}

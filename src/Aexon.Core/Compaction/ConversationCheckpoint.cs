using System.Text.Json;
using System.Text.Json.Serialization;
using Aexon.Core.Storage;

namespace Aexon.Core.Compaction;

/// <summary>
/// Represents conversation checkpoint.
/// </summary>
public sealed class ConversationCheckpoint
{
    public required IReadOnlyList<string> ActiveMessageIds { get; init; }

    public string? LeafMessageId =>
        ActiveMessageIds.Count == 0 ? null : ActiveMessageIds[^1];

    public JsonElement ToMetadataPayload() =>
        JsonSerializer.SerializeToElement(new MetadataPayload
        {
            MessageIds = ActiveMessageIds.ToArray(),
        });

    public static bool TryParse(
        TranscriptMetadataEntry entry,
        out ConversationCheckpoint? checkpoint)
    {
        checkpoint = null;
        if (!string.Equals(entry.EventType, "conversation-checkpoint", StringComparison.OrdinalIgnoreCase) ||
            entry.Payload is not JsonElement payload ||
            payload.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!payload.TryGetProperty("message_ids", out var idsElement) &&
            !payload.TryGetProperty("MessageIds", out idsElement))
        {
            return false;
        }

        if (idsElement.ValueKind != JsonValueKind.Array)
            return false;

        var ids = new List<string>();
        foreach (var item in idsElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                continue;

            var id = item.GetString();
            if (!string.IsNullOrWhiteSpace(id))
                ids.Add(id);
        }

        if (ids.Count == 0)
            return false;

        checkpoint = new ConversationCheckpoint
        {
            ActiveMessageIds = ids,
        };

        return true;
    }

    private sealed class MetadataPayload
    {
        [JsonPropertyName("message_ids")]
        public required string[] MessageIds { get; init; }
    }
}

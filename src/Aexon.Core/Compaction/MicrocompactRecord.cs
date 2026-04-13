using System.Text.Json;
using System.Text.Json.Serialization;
using Aexon.Core.Storage;

namespace Aexon.Core.Compaction;

/// <summary>
/// Represents microcompact record.
/// </summary>
public sealed class MicrocompactRecord
{
    public const string EventType = "microcompact";

    public required IReadOnlyList<MicrocompactEdit> Edits { get; init; }

    public JsonElement ToMetadataPayload() =>
        JsonSerializer.SerializeToElement(new MetadataPayload
        {
            Edits = Edits
                .Select(edit => new MetadataEdit
                {
                    MessageId = edit.MessageId,
                    ClearThinking = edit.ClearThinking,
                    ClearToolResult = edit.ClearToolResult,
                })
                .ToArray(),
        });

    public static bool TryParse(
        TranscriptMetadataEntry entry,
        out MicrocompactRecord? record)
    {
        record = null;
        if (!string.Equals(entry.EventType, EventType, StringComparison.OrdinalIgnoreCase) ||
            entry.Payload is not JsonElement payload ||
            payload.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!payload.TryGetProperty("edits", out var editsElement) &&
            !payload.TryGetProperty("Edits", out editsElement))
        {
            return false;
        }

        if (editsElement.ValueKind != JsonValueKind.Array)
            return false;

        var edits = new List<MicrocompactEdit>();
        foreach (var item in editsElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            var messageId = TryReadString(item, "message_id") ?? TryReadString(item, "MessageId");
            if (string.IsNullOrWhiteSpace(messageId))
                continue;

            edits.Add(new MicrocompactEdit
            {
                MessageId = messageId,
                ClearToolResult = TryReadBool(item, "clear_tool_result") ?? TryReadBool(item, "ClearToolResult") ?? false,
                ClearThinking = TryReadBool(item, "clear_thinking") ?? TryReadBool(item, "ClearThinking") ?? false,
            });
        }

        if (edits.Count == 0)
            return false;

        record = new MicrocompactRecord
        {
            Edits = edits,
        };
        return true;
    }

    private static string? TryReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return property.GetString();
    }

    private static bool? TryReadBool(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
        {
            return null;
        }

        return property.GetBoolean();
    }

    private sealed class MetadataPayload
    {
        [JsonPropertyName("edits")]
        public required MetadataEdit[] Edits { get; init; }
    }

    private sealed class MetadataEdit
    {
        [JsonPropertyName("message_id")]
        public required string MessageId { get; init; }

        [JsonPropertyName("clear_tool_result")]
        public required bool ClearToolResult { get; init; }

        [JsonPropertyName("clear_thinking")]
        public required bool ClearThinking { get; init; }
    }
}

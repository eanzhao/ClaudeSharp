using System.Text.Json;
using Aexon.Core.Storage;

namespace Aexon.Core.Agents;

/// <summary>
/// Represents a restored mailbox snapshot.
/// </summary>
public sealed class AgentMessageStateSnapshot
{
    public IReadOnlyList<AgentMessage> Messages { get; init; } = [];
}

/// <summary>
/// Serializes mailbox state into transcript metadata and restores it on resume.
/// </summary>
public static class AgentMessagePersistence
{
    public const string MessageEventType = "agent-message";

    public static TranscriptMetadataEntry CreateMessageEntry(
        AgentMessage message,
        DateTimeOffset? recordedAt = null) =>
        new(
            MessageEventType,
            JsonSerializer.SerializeToElement(message.Clone()),
            recordedAt ?? message.UpdatedAt);

    public static AgentMessageStateSnapshot Restore(
        IReadOnlyList<TranscriptMetadataEntry> metadataEntries)
    {
        var messages = new Dictionary<string, AgentMessage>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in metadataEntries)
        {
            if (!string.Equals(entry.EventType, MessageEventType, StringComparison.OrdinalIgnoreCase))
                continue;

            if (TryReadMessage(entry.Payload, out var message) &&
                message != null &&
                !string.IsNullOrWhiteSpace(message.Id))
            {
                messages[message.Id] = message;
            }
        }

        return new AgentMessageStateSnapshot
        {
            Messages = messages.Values
                .OrderByDescending(message => message.CreatedAt)
                .ThenByDescending(message => message.Id, StringComparer.OrdinalIgnoreCase)
                .Select(message => message.Clone())
                .ToArray(),
        };
    }

    private static bool TryReadMessage(
        JsonElement? payload,
        out AgentMessage? message)
    {
        message = default;
        if (payload is not JsonElement element ||
            element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return false;
        }

        try
        {
            message = element.Deserialize<AgentMessage>();
            return message != null;
        }
        catch
        {
            return false;
        }
    }
}

using System.Text.Json;
using Aexon.Core.Storage;

namespace Aexon.Core.Agents;

public static class AgentRemoteTriggerPersistence
{
    public const string TriggerEventType = "agent-remote-trigger";
    public const string TriggerDeletedEventType = "agent-remote-trigger-deleted";

    public static TranscriptMetadataEntry CreateTriggerEntry(
        AgentRemoteTrigger trigger,
        DateTimeOffset? recordedAt = null) =>
        new(
            TriggerEventType,
            JsonSerializer.SerializeToElement(new TriggerPayload
            {
                Id = trigger.Id,
                WorkItemId = trigger.WorkItemId,
                Kind = trigger.Kind,
                Description = trigger.Description,
                Schedule = trigger.Schedule,
                Secret = trigger.Secret,
                Enabled = trigger.Enabled,
                CreatedAt = trigger.CreatedAt,
                UpdatedAt = trigger.UpdatedAt,
                LastTriggeredAt = trigger.LastTriggeredAt,
                NextTriggerAt = trigger.NextTriggerAt,
            }),
            recordedAt ?? trigger.UpdatedAt);

    public static TranscriptMetadataEntry CreateTriggerDeletedEntry(
        string id,
        DateTimeOffset? recordedAt = null) =>
        new(
            TriggerDeletedEventType,
            JsonSerializer.SerializeToElement(new DeletedPayload
            {
                Id = id,
            }),
            recordedAt ?? DateTimeOffset.UtcNow);

    public static IReadOnlyList<AgentRemoteTrigger> Restore(
        IReadOnlyList<TranscriptMetadataEntry> metadataEntries)
    {
        var triggers = new Dictionary<string, AgentRemoteTrigger>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in metadataEntries)
        {
            switch (entry.EventType)
            {
                case TriggerEventType:
                    if (TryReadPayload(entry.Payload, out TriggerPayload? payload) &&
                        payload != null &&
                        !string.IsNullOrWhiteSpace(payload.Id))
                    {
                        triggers[payload.Id] = new AgentRemoteTrigger
                        {
                            Id = payload.Id,
                            WorkItemId = payload.WorkItemId,
                            Kind = payload.Kind,
                            Description = payload.Description,
                            Schedule = payload.Schedule,
                            Secret = payload.Secret,
                            Enabled = payload.Enabled,
                            CreatedAt = payload.CreatedAt,
                            UpdatedAt = payload.UpdatedAt,
                            LastTriggeredAt = payload.LastTriggeredAt,
                            NextTriggerAt = payload.NextTriggerAt,
                        };
                    }

                    break;

                case TriggerDeletedEventType:
                    if (TryReadPayload(entry.Payload, out DeletedPayload? deleted) &&
                        deleted != null &&
                        !string.IsNullOrWhiteSpace(deleted.Id))
                    {
                        triggers.Remove(deleted.Id);
                    }

                    break;
            }
        }

        return triggers.Values
            .OrderByDescending(trigger => trigger.CreatedAt)
            .ThenBy(trigger => trigger.Id, StringComparer.OrdinalIgnoreCase)
            .Select(trigger => trigger.Clone())
            .ToArray();
    }

    private static bool TryReadPayload<T>(JsonElement? payload, out T? value)
    {
        value = default;
        if (payload is not JsonElement element ||
            element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return false;
        }

        try
        {
            value = element.Deserialize<T>();
            return value != null;
        }
        catch
        {
            return false;
        }
    }

    private sealed class TriggerPayload
    {
        public required string Id { get; init; }
        public required string WorkItemId { get; init; }
        public required AgentRemoteTriggerKind Kind { get; init; }
        public string? Description { get; init; }
        public string? Schedule { get; init; }
        public string? Secret { get; init; }
        public required bool Enabled { get; init; }
        public required DateTimeOffset CreatedAt { get; init; }
        public required DateTimeOffset UpdatedAt { get; init; }
        public DateTimeOffset? LastTriggeredAt { get; init; }
        public DateTimeOffset? NextTriggerAt { get; init; }
    }

    private sealed class DeletedPayload
    {
        public required string Id { get; init; }
    }
}

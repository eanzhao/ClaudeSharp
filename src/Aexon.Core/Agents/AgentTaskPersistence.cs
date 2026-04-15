using System.Text.Json;
using Aexon.Core.Storage;

namespace Aexon.Core.Agents;

/// <summary>
/// Represents a restored snapshot of agent task state.
/// </summary>
public sealed class AgentTaskStateSnapshot
{
    public IReadOnlyList<AgentWorkItem> WorkItems { get; init; } = [];
    public IReadOnlyList<AgentBackgroundRun> BackgroundRuns { get; init; } = [];
    public IReadOnlyList<AgentTerminationEvent> TerminationEvents { get; init; } = [];
}

/// <summary>
/// Serializes agent task runtime state into transcript metadata and restores it on resume.
/// </summary>
public static class AgentTaskPersistence
{
    public const string WorkItemEventType = "agent-work-item";
    public const string BackgroundRunEventType = "agent-background-run";
    public const string BackgroundOutputEventType = "agent-background-output";
    public const string WorkItemDeletedEventType = "agent-work-item-deleted";
    public const string BackgroundRunDeletedEventType = "agent-background-run-deleted";
    public const string TerminationEventType = "agent-termination";

    public static TranscriptMetadataEntry CreateWorkItemEntry(
        AgentWorkItem item,
        DateTimeOffset? recordedAt = null) =>
        new(
            WorkItemEventType,
            JsonSerializer.SerializeToElement(new WorkItemPayload
            {
                Id = item.Id,
                SubagentId = item.SubagentId,
                Title = item.Title,
                Description = item.Description,
                Owner = item.Owner,
                SourceKind = item.SourceKind,
                SourceId = item.SourceId,
                SourceThreadId = item.SourceThreadId,
                ApprovalRequestId = item.ApprovalRequestId,
                ApprovalThreadId = item.ApprovalThreadId,
                Status = item.Status,
                CreatedAt = item.CreatedAt,
                UpdatedAt = item.UpdatedAt,
                Blocks = item.Blocks.ToArray(),
                BlockedBy = item.BlockedBy.ToArray(),
            }),
            recordedAt ?? item.UpdatedAt);

    public static TranscriptMetadataEntry CreateBackgroundRunEntry(
        AgentBackgroundRun run,
        DateTimeOffset? recordedAt = null) =>
        new(
            BackgroundRunEventType,
            JsonSerializer.SerializeToElement(new BackgroundRunPayload
            {
                Id = run.Id,
                SubagentId = run.SubagentId,
                Name = run.Name,
                Owner = run.Owner,
                WorkItemId = run.WorkItemId,
                Status = run.Status,
                StopReason = run.StopReason,
                TerminationKind = run.TerminationInfo?.Kind,
                TerminationSource = run.TerminationInfo?.Source,
                TerminationOccurredAt = run.TerminationInfo?.OccurredAt,
                StartedAt = run.StartedAt,
                UpdatedAt = run.UpdatedAt,
                StoppedAt = run.StoppedAt,
            }),
            recordedAt ?? run.UpdatedAt);

    public static TranscriptMetadataEntry CreateBackgroundOutputEntry(
        string backgroundRunId,
        string chunk,
        DateTimeOffset? recordedAt = null) =>
        new(
            BackgroundOutputEventType,
            JsonSerializer.SerializeToElement(new BackgroundOutputPayload
            {
                Id = backgroundRunId,
                Chunk = chunk,
            }),
            recordedAt ?? DateTimeOffset.UtcNow);

    public static TranscriptMetadataEntry CreateWorkItemDeletedEntry(
        string workItemId,
        DateTimeOffset? recordedAt = null) =>
        new(
            WorkItemDeletedEventType,
            JsonSerializer.SerializeToElement(new DeletedEntityPayload
            {
                Id = workItemId,
            }),
            recordedAt ?? DateTimeOffset.UtcNow);

    public static TranscriptMetadataEntry CreateBackgroundRunDeletedEntry(
        string backgroundRunId,
        DateTimeOffset? recordedAt = null) =>
        new(
            BackgroundRunDeletedEventType,
            JsonSerializer.SerializeToElement(new DeletedEntityPayload
            {
                Id = backgroundRunId,
            }),
            recordedAt ?? DateTimeOffset.UtcNow);

    public static TranscriptMetadataEntry CreateTerminationEventEntry(
        AgentTerminationEvent terminationEvent,
        DateTimeOffset? recordedAt = null) =>
        new(
            TerminationEventType,
            JsonSerializer.SerializeToElement(new TerminationEventPayload
            {
                SubagentId = terminationEvent.SubagentId,
                BackgroundRunId = terminationEvent.BackgroundRunId,
                WorkItemId = terminationEvent.WorkItemId,
                Kind = terminationEvent.Kind,
                Source = terminationEvent.Source,
                Reason = terminationEvent.Reason,
                OccurredAt = terminationEvent.OccurredAt,
            }),
            recordedAt ?? terminationEvent.OccurredAt);

    public static AgentTaskStateSnapshot Restore(
        IReadOnlyList<TranscriptMetadataEntry> metadataEntries)
    {
        var workItems = new Dictionary<string, AgentWorkItem>(StringComparer.OrdinalIgnoreCase);
        var backgroundRuns = new Dictionary<string, AgentBackgroundRun>(StringComparer.OrdinalIgnoreCase);
        var terminationEvents = new Dictionary<string, AgentTerminationEvent>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in metadataEntries)
        {
            switch (entry.EventType)
            {
                case WorkItemEventType:
                    if (TryReadPayload(entry.Payload, out WorkItemPayload? workItemPayload) &&
                        workItemPayload != null &&
                        !string.IsNullOrWhiteSpace(workItemPayload.Id))
                    {
                        workItems[workItemPayload.Id] = ToWorkItem(workItemPayload);
                    }

                    break;

                case BackgroundRunEventType:
                    if (TryReadPayload(entry.Payload, out BackgroundRunPayload? backgroundRunPayload) &&
                        backgroundRunPayload != null &&
                        !string.IsNullOrWhiteSpace(backgroundRunPayload.Id))
                    {
                        var restoredRun = ToBackgroundRun(backgroundRunPayload);
                        if (backgroundRuns.TryGetValue(backgroundRunPayload.Id, out var existingRun))
                        {
                            foreach (var chunk in existingRun.Output)
                                restoredRun.AppendOutput(chunk);

                            restoredRun.UpdatedAt = backgroundRunPayload.UpdatedAt;
                            restoredRun.StoppedAt = backgroundRunPayload.StoppedAt;
                        }

                        backgroundRuns[backgroundRunPayload.Id] = restoredRun;
                    }

                    break;

                case BackgroundOutputEventType:
                    if (TryReadPayload(entry.Payload, out BackgroundOutputPayload? outputPayload) &&
                        outputPayload != null &&
                        !string.IsNullOrWhiteSpace(outputPayload.Id) &&
                        backgroundRuns.TryGetValue(outputPayload.Id, out var run) &&
                        !string.IsNullOrWhiteSpace(outputPayload.Chunk))
                    {
                        run.AppendOutput(outputPayload.Chunk);
                        if (entry.RecordedAt is { } recordedAt)
                            run.UpdatedAt = recordedAt;
                    }

                    break;

                case WorkItemDeletedEventType:
                    if (TryReadPayload(entry.Payload, out DeletedEntityPayload? deletedWorkItem) &&
                        deletedWorkItem != null &&
                        !string.IsNullOrWhiteSpace(deletedWorkItem.Id))
                    {
                        workItems.Remove(deletedWorkItem.Id);
                    }

                    break;

                case BackgroundRunDeletedEventType:
                    if (TryReadPayload(entry.Payload, out DeletedEntityPayload? deletedBackgroundRun) &&
                        deletedBackgroundRun != null &&
                        !string.IsNullOrWhiteSpace(deletedBackgroundRun.Id))
                    {
                        backgroundRuns.Remove(deletedBackgroundRun.Id);
                        terminationEvents.Remove(deletedBackgroundRun.Id);
                    }

                    break;

                case TerminationEventType:
                    if (TryReadPayload(entry.Payload, out TerminationEventPayload? terminationPayload) &&
                        terminationPayload != null &&
                        !string.IsNullOrWhiteSpace(terminationPayload.BackgroundRunId))
                    {
                        terminationEvents[terminationPayload.BackgroundRunId] = new AgentTerminationEvent
                        {
                            SubagentId = terminationPayload.SubagentId,
                            BackgroundRunId = terminationPayload.BackgroundRunId,
                            WorkItemId = terminationPayload.WorkItemId,
                            Kind = terminationPayload.Kind,
                            Source = terminationPayload.Source,
                            Reason = terminationPayload.Reason,
                            OccurredAt = terminationPayload.OccurredAt,
                        };
                    }

                    break;
            }
        }

        return new AgentTaskStateSnapshot
        {
            WorkItems = workItems.Values
                .OrderBy(item => item.CreatedAt)
                .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            BackgroundRuns = backgroundRuns.Values
                .OrderBy(run => run.StartedAt)
                .ThenBy(run => run.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            TerminationEvents = terminationEvents.Values
                .OrderBy(evt => evt.OccurredAt)
                .ThenBy(evt => evt.BackgroundRunId, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
        };
    }

    private static AgentWorkItem ToWorkItem(WorkItemPayload payload)
    {
        var item = new AgentWorkItem
        {
            Id = payload.Id,
            SubagentId = payload.SubagentId,
            Title = payload.Title,
            Description = payload.Description,
            Owner = payload.Owner,
            SourceKind = payload.SourceKind,
            SourceId = payload.SourceId,
            SourceThreadId = payload.SourceThreadId,
            ApprovalRequestId = payload.ApprovalRequestId,
            ApprovalThreadId = payload.ApprovalThreadId,
            Status = payload.Status,
            CreatedAt = payload.CreatedAt,
            UpdatedAt = payload.UpdatedAt,
        };

        foreach (var taskId in payload.Blocks ?? [])
            item.AddBlock(taskId);
        foreach (var taskId in payload.BlockedBy ?? [])
            item.AddBlockedBy(taskId);

        return item;
    }

    private static AgentBackgroundRun ToBackgroundRun(BackgroundRunPayload payload) =>
        new()
        {
            Id = payload.Id,
            SubagentId = payload.SubagentId,
            Name = payload.Name,
            Owner = payload.Owner,
            WorkItemId = payload.WorkItemId,
            Status = payload.Status,
            StopReason = payload.StopReason,
            TerminationInfo = payload.TerminationKind is { } kind
                ? new AgentTerminationInfo
                {
                    Kind = kind,
                    Source = payload.TerminationSource ?? AgentTerminationSource.Agent,
                    Reason = payload.StopReason,
                    OccurredAt = payload.TerminationOccurredAt ?? payload.StoppedAt ?? payload.UpdatedAt,
                }
                : null,
            StartedAt = payload.StartedAt,
            UpdatedAt = payload.UpdatedAt,
            StoppedAt = payload.StoppedAt,
        };

    private static bool TryReadPayload<T>(
        JsonElement? payload,
        out T? value)
    {
        value = default;
        if (payload is not JsonElement element ||
            element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return false;

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

    private sealed class WorkItemPayload
    {
        public required string Id { get; init; }
        public string? SubagentId { get; init; }
        public required string Title { get; init; }
        public string? Description { get; init; }
        public string? Owner { get; init; }
        public string? SourceKind { get; init; }
        public string? SourceId { get; init; }
        public string? SourceThreadId { get; init; }
        public string? ApprovalRequestId { get; init; }
        public string? ApprovalThreadId { get; init; }
        public AgentWorkItemStatus Status { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }
        public string[]? Blocks { get; init; }
        public string[]? BlockedBy { get; init; }
    }

    private sealed class BackgroundRunPayload
    {
        public required string Id { get; init; }
        public string? SubagentId { get; init; }
        public required string Name { get; init; }
        public string? Owner { get; init; }
        public string? WorkItemId { get; init; }
        public AgentBackgroundRunStatus Status { get; init; }
        public string? StopReason { get; init; }
        public AgentTerminationKind? TerminationKind { get; init; }
        public AgentTerminationSource? TerminationSource { get; init; }
        public DateTimeOffset? TerminationOccurredAt { get; init; }
        public DateTimeOffset StartedAt { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }
        public DateTimeOffset? StoppedAt { get; init; }
    }

    private sealed class BackgroundOutputPayload
    {
        public required string Id { get; init; }
        public required string Chunk { get; init; }
    }

    private sealed class DeletedEntityPayload
    {
        public required string Id { get; init; }
    }

    private sealed class TerminationEventPayload
    {
        public required string SubagentId { get; init; }
        public required string BackgroundRunId { get; init; }
        public string? WorkItemId { get; init; }
        public AgentTerminationKind Kind { get; init; }
        public AgentTerminationSource Source { get; init; }
        public string? Reason { get; init; }
        public DateTimeOffset OccurredAt { get; init; }
    }
}

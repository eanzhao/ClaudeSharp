using System.Text.Json;
using Aexon.Core.Storage;

namespace Aexon.Core.Agents;

public static class AgentManagedWorktreePersistence
{
    public const string WorktreeEventType = "agent-worktree";
    public const string WorktreeDeletedEventType = "agent-worktree-deleted";

    public static TranscriptMetadataEntry CreateWorktreeEntry(
        AgentManagedWorktree worktree,
        DateTimeOffset? recordedAt = null) =>
        new(
            WorktreeEventType,
            JsonSerializer.SerializeToElement(new WorktreePayload
            {
                Id = worktree.Id,
                Name = worktree.Name,
                SourceWorkingDirectory = worktree.SourceWorkingDirectory,
                WorkingDirectory = worktree.WorkingDirectory,
                RootDirectory = worktree.RootDirectory,
                RepositoryRoot = worktree.RepositoryRoot,
                Description = worktree.Description,
                CreatedAt = worktree.CreatedAt,
                UpdatedAt = worktree.UpdatedAt,
            }),
            recordedAt ?? worktree.UpdatedAt);

    public static TranscriptMetadataEntry CreateWorktreeDeletedEntry(
        string id,
        DateTimeOffset? recordedAt = null) =>
        new(
            WorktreeDeletedEventType,
            JsonSerializer.SerializeToElement(new DeletedPayload
            {
                Id = id,
            }),
            recordedAt ?? DateTimeOffset.UtcNow);

    public static IReadOnlyList<AgentManagedWorktree> Restore(
        IReadOnlyList<TranscriptMetadataEntry> metadataEntries)
    {
        var worktrees = new Dictionary<string, AgentManagedWorktree>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in metadataEntries)
        {
            switch (entry.EventType)
            {
                case WorktreeEventType:
                    if (TryReadPayload(entry.Payload, out WorktreePayload? payload) &&
                        payload != null &&
                        !string.IsNullOrWhiteSpace(payload.Id))
                    {
                        worktrees[payload.Id] = new AgentManagedWorktree
                        {
                            Id = payload.Id,
                            Name = payload.Name,
                            SourceWorkingDirectory = payload.SourceWorkingDirectory,
                            WorkingDirectory = payload.WorkingDirectory,
                            RootDirectory = payload.RootDirectory,
                            RepositoryRoot = payload.RepositoryRoot,
                            Description = payload.Description,
                            CreatedAt = payload.CreatedAt,
                            UpdatedAt = payload.UpdatedAt,
                        };
                    }

                    break;

                case WorktreeDeletedEventType:
                    if (TryReadPayload(entry.Payload, out DeletedPayload? deleted) &&
                        deleted != null &&
                        !string.IsNullOrWhiteSpace(deleted.Id))
                    {
                        worktrees.Remove(deleted.Id);
                    }

                    break;
            }
        }

        return worktrees.Values
            .OrderByDescending(worktree => worktree.CreatedAt)
            .ThenBy(worktree => worktree.Id, StringComparer.OrdinalIgnoreCase)
            .Select(worktree => worktree.Clone())
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

    private sealed class WorktreePayload
    {
        public required string Id { get; init; }
        public string? Name { get; init; }
        public required string SourceWorkingDirectory { get; init; }
        public required string WorkingDirectory { get; init; }
        public required string RootDirectory { get; init; }
        public required string RepositoryRoot { get; init; }
        public string? Description { get; init; }
        public required DateTimeOffset CreatedAt { get; init; }
        public required DateTimeOffset UpdatedAt { get; init; }
    }

    private sealed class DeletedPayload
    {
        public required string Id { get; init; }
    }
}

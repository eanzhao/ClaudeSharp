using System.Text.Json;
using Aexon.Core.Storage;

namespace Aexon.Core.Agents;

/// <summary>
/// Represents a restored snapshot of team runtime state.
/// </summary>
public sealed class AgentTeamStateSnapshot
{
    public IReadOnlyList<AgentTeam> Teams { get; init; } = [];
}

/// <summary>
/// Serializes team runtime state into transcript metadata and restores it on resume.
/// </summary>
public static class AgentTeamPersistence
{
    public const string TeamEventType = "agent-team";
    public const string TeamDeletedEventType = "agent-team-deleted";

    public static TranscriptMetadataEntry CreateTeamEntry(
        AgentTeam team,
        DateTimeOffset? recordedAt = null) =>
        new(
            TeamEventType,
            JsonSerializer.SerializeToElement(team.Clone()),
            recordedAt ?? team.UpdatedAt);

    public static TranscriptMetadataEntry CreateTeamDeletedEntry(
        string teamId,
        DateTimeOffset? recordedAt = null) =>
        new(
            TeamDeletedEventType,
            JsonSerializer.SerializeToElement(new DeletedTeamPayload
            {
                Id = teamId,
            }),
            recordedAt ?? DateTimeOffset.UtcNow);

    public static AgentTeamStateSnapshot Restore(
        IReadOnlyList<TranscriptMetadataEntry> metadataEntries)
    {
        var teams = new Dictionary<string, AgentTeam>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in metadataEntries)
        {
            switch (entry.EventType)
            {
                case TeamEventType:
                    if (TryReadTeam(entry.Payload, out var team) &&
                        team != null &&
                        !string.IsNullOrWhiteSpace(team.Id))
                    {
                        teams[team.Id] = team;
                    }

                    break;

                case TeamDeletedEventType:
                    if (TryReadPayload(entry.Payload, out DeletedTeamPayload? deletedTeam) &&
                        deletedTeam != null &&
                        !string.IsNullOrWhiteSpace(deletedTeam.Id))
                    {
                        teams.Remove(deletedTeam.Id);
                    }

                    break;
            }
        }

        return new AgentTeamStateSnapshot
        {
            Teams = teams.Values
                .OrderBy(team => team.CreatedAt)
                .ThenBy(team => team.Id, StringComparer.OrdinalIgnoreCase)
                .Select(team => team.Clone())
                .ToArray(),
        };
    }

    private static bool TryReadTeam(
        JsonElement? payload,
        out AgentTeam? team)
    {
        team = default;
        if (payload is not JsonElement element ||
            element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return false;
        }

        try
        {
            team = element.Deserialize<AgentTeam>();
            return team != null;
        }
        catch
        {
            return false;
        }
    }

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

    private sealed class DeletedTeamPayload
    {
        public required string Id { get; init; }
    }
}

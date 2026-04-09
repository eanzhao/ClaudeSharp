using ClaudeSharp.Core.Storage;

namespace ClaudeSharp.Core.Agents;

/// <summary>
/// Persists team runtime state into the conversation transcript.
/// </summary>
public sealed class PersistentAgentTeamRuntime : IAgentTeamRuntime
{
    private readonly InMemoryAgentTeamRuntime _inner;
    private readonly IConversationJournal _journal;

    private PersistentAgentTeamRuntime(
        InMemoryAgentTeamRuntime inner,
        IConversationJournal journal)
    {
        _inner = inner;
        _journal = journal;
    }

    public static Task<PersistentAgentTeamRuntime> CreateAsync(
        IConversationJournal journal,
        IReadOnlyList<TranscriptMetadataEntry>? metadataEntries = null,
        CancellationToken cancellationToken = default)
    {
        var restored = AgentTeamPersistence.Restore(metadataEntries ?? []);
        var runtime = new PersistentAgentTeamRuntime(
            new InMemoryAgentTeamRuntime(restored.Teams),
            journal);

        return Task.FromResult(runtime);
    }

    public AgentTeam CreateTeam(
        string name,
        string? description = null,
        string? leadName = null)
    {
        var team = _inner.CreateTeam(name, description, leadName);
        Persist(AgentTeamPersistence.CreateTeamEntry(team));
        return team;
    }

    public AgentTeam? GetTeam(string teamId) => _inner.GetTeam(teamId);

    public AgentTeam? FindTeamByName(string name) => _inner.FindTeamByName(name);

    public IReadOnlyList<AgentTeam> ListTeams() => _inner.ListTeams();

    public bool RenameTeam(string teamId, string name)
    {
        var updated = _inner.RenameTeam(teamId, name);
        if (updated && _inner.GetTeam(teamId) is { } team)
            Persist(AgentTeamPersistence.CreateTeamEntry(team));

        return updated;
    }

    public bool SetTeamDescription(string teamId, string? description)
    {
        var updated = _inner.SetTeamDescription(teamId, description);
        if (updated && _inner.GetTeam(teamId) is { } team)
            Persist(AgentTeamPersistence.CreateTeamEntry(team));

        return updated;
    }

    public AgentTeamMember? AddMember(
        string teamId,
        string name,
        AgentTeamMemberRole role = AgentTeamMemberRole.Member)
    {
        var member = _inner.AddMember(teamId, name, role);
        if (member != null && _inner.GetTeam(teamId) is { } team)
            Persist(AgentTeamPersistence.CreateTeamEntry(team));

        return member;
    }

    public bool SetLead(string teamId, string memberId)
    {
        var updated = _inner.SetLead(teamId, memberId);
        if (updated && _inner.GetTeam(teamId) is { } team)
            Persist(AgentTeamPersistence.CreateTeamEntry(team));

        return updated;
    }

    public bool RemoveMember(string teamId, string memberId)
    {
        var updated = _inner.RemoveMember(teamId, memberId);
        if (updated && _inner.GetTeam(teamId) is { } team)
            Persist(AgentTeamPersistence.CreateTeamEntry(team));

        return updated;
    }

    public bool DeleteTeam(string teamId)
    {
        var deleted = _inner.DeleteTeam(teamId);
        if (deleted)
            Persist(AgentTeamPersistence.CreateTeamDeletedEntry(teamId));

        return deleted;
    }

    private void Persist(TranscriptMetadataEntry entry)
    {
        try
        {
            _journal.AppendMetadataEntryAsync(entry).GetAwaiter().GetResult();
        }
        catch
        {
            // Team runtime persistence is best-effort; in-memory state still updates.
        }
    }
}

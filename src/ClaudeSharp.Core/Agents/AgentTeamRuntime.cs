namespace ClaudeSharp.Core.Agents;

/// <summary>
/// Defines the contract for team runtime.
/// </summary>
public interface IAgentTeamRuntime
{
    AgentTeam CreateTeam(
        string name,
        string? description = null,
        string? leadName = null);

    AgentTeam? GetTeam(string teamId);

    AgentTeam? FindTeamByName(string name);

    IReadOnlyList<AgentTeam> ListTeams();

    bool RenameTeam(string teamId, string name);

    bool SetTeamDescription(string teamId, string? description);

    AgentTeamMember? AddMember(
        string teamId,
        string name,
        AgentTeamMemberRole role = AgentTeamMemberRole.Member);

    bool SetLead(string teamId, string memberId);

    bool RemoveMember(string teamId, string memberId);

    bool DeleteTeam(string teamId);
}

/// <summary>
/// Provides an in-memory team registry.
/// </summary>
public sealed class InMemoryAgentTeamRuntime : IAgentTeamRuntime
{
    private readonly object _gate = new();
    private readonly Dictionary<string, AgentTeam> _teams =
        new(StringComparer.OrdinalIgnoreCase);
    private int _teamSequence;
    private int _memberSequence;

    public InMemoryAgentTeamRuntime(IEnumerable<AgentTeam>? teams = null)
    {
        if (teams == null)
            return;

        foreach (var team in teams)
        {
            var clone = team.Clone();
            _teams[clone.Id] = clone;
            _teamSequence = Math.Max(_teamSequence, ParseSequence(clone.Id, "team-"));
            foreach (var member in clone.Members)
                _memberSequence = Math.Max(_memberSequence, ParseSequence(member.Id, "team-member-"));
        }
    }

    public AgentTeam CreateTeam(
        string name,
        string? description = null,
        string? leadName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        lock (_gate)
        {
            var normalizedName = NormalizeName(name);
            if (_teams.Values.Any(team =>
                    string.Equals(team.Name, normalizedName, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"Team '{normalizedName}' already exists.");
            }

            var team = new AgentTeam
            {
                Id = $"team-{Interlocked.Increment(ref _teamSequence)}",
                Name = normalizedName,
                Description = NormalizeOptional(description),
            };

            if (!string.IsNullOrWhiteSpace(leadName))
                AddMemberCore(team, leadName, AgentTeamMemberRole.Lead);

            _teams[team.Id] = team;
            return team.Clone();
        }
    }

    public AgentTeam? GetTeam(string teamId)
    {
        lock (_gate)
        {
            return _teams.TryGetValue(teamId, out var team)
                ? team.Clone()
                : null;
        }
    }

    public AgentTeam? FindTeamByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        lock (_gate)
        {
            var team = _teams.Values.FirstOrDefault(
                candidate => string.Equals(candidate.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
            return team?.Clone();
        }
    }

    public IReadOnlyList<AgentTeam> ListTeams()
    {
        lock (_gate)
        {
            return _teams.Values
                .OrderBy(team => team.CreatedAt)
                .ThenBy(team => team.Id, StringComparer.OrdinalIgnoreCase)
                .Select(team => team.Clone())
                .ToArray();
        }
    }

    public bool RenameTeam(string teamId, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        lock (_gate)
        {
            if (!_teams.TryGetValue(teamId, out var team))
                return false;

            var normalized = NormalizeName(name);
            if (_teams.Values.Any(candidate =>
                    !string.Equals(candidate.Id, teamId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(candidate.Name, normalized, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            if (string.Equals(team.Name, normalized, StringComparison.Ordinal))
                return false;

            team.Name = normalized;
            team.UpdatedAt = DateTimeOffset.UtcNow;
            return true;
        }
    }

    public bool SetTeamDescription(string teamId, string? description)
    {
        lock (_gate)
        {
            if (!_teams.TryGetValue(teamId, out var team))
                return false;

            var normalized = NormalizeOptional(description);
            if (string.Equals(team.Description, normalized, StringComparison.Ordinal))
                return false;

            team.Description = normalized;
            team.UpdatedAt = DateTimeOffset.UtcNow;
            return true;
        }
    }

    public AgentTeamMember? AddMember(
        string teamId,
        string name,
        AgentTeamMemberRole role = AgentTeamMemberRole.Member)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        lock (_gate)
        {
            if (!_teams.TryGetValue(teamId, out var team))
                return null;

            return AddMemberCore(team, name, role).Clone();
        }
    }

    public bool SetLead(string teamId, string memberId)
    {
        lock (_gate)
        {
            if (!_teams.TryGetValue(teamId, out var team))
                return false;

            var member = team.GetMember(memberId);
            if (member == null)
                return false;

            if (!string.IsNullOrWhiteSpace(team.LeadMemberId))
            {
                var previousLead = team.GetMember(team.LeadMemberId!);
                if (previousLead != null && !string.Equals(previousLead.Id, member.Id, StringComparison.OrdinalIgnoreCase))
                {
                    previousLead.Role = AgentTeamMemberRole.Member;
                    previousLead.UpdatedAt = DateTimeOffset.UtcNow;
                }
            }

            member.Role = AgentTeamMemberRole.Lead;
            member.Status = AgentTeamMemberStatus.Active;
            member.UpdatedAt = DateTimeOffset.UtcNow;
            team.LeadMemberId = member.Id;
            team.UpdatedAt = DateTimeOffset.UtcNow;
            return true;
        }
    }

    public bool RemoveMember(string teamId, string memberId)
    {
        lock (_gate)
        {
            if (!_teams.TryGetValue(teamId, out var team))
                return false;

            var removed = team.RemoveMember(memberId);
            if (removed)
                team.UpdatedAt = DateTimeOffset.UtcNow;

            return removed;
        }
    }

    public bool DeleteTeam(string teamId)
    {
        lock (_gate)
            return _teams.Remove(teamId);
    }

    private AgentTeamMember AddMemberCore(
        AgentTeam team,
        string name,
        AgentTeamMemberRole role)
    {
        var normalized = NormalizeName(name);
        var existing = team.GetMemberByName(normalized);
        if (existing != null)
        {
            existing.Name = normalized;
            if (role == AgentTeamMemberRole.Lead ||
                existing.Role != AgentTeamMemberRole.Lead)
            {
                existing.Role = role;
            }

            existing.Status = AgentTeamMemberStatus.Active;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            if (role == AgentTeamMemberRole.Lead)
                team.LeadMemberId = existing.Id;
            team.UpdatedAt = DateTimeOffset.UtcNow;
            return existing;
        }

        var member = new AgentTeamMember
        {
            Id = $"team-member-{Interlocked.Increment(ref _memberSequence)}",
            Name = normalized,
            Role = role,
        };

        team.Members.Add(member);
        if (role == AgentTeamMemberRole.Lead)
            team.LeadMemberId = member.Id;

        team.UpdatedAt = DateTimeOffset.UtcNow;
        return member;
    }

    private static string NormalizeName(string value) =>
        value.Trim();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int ParseSequence(string value, string prefix)
    {
        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return 0;

        return int.TryParse(value[prefix.Length..], out var parsed)
            ? parsed
            : 0;
    }
}

namespace ClaudeSharp.Core.Agents;

/// <summary>
/// Defines team member role values.
/// </summary>
public enum AgentTeamMemberRole
{
    Member,
    Lead,
}

/// <summary>
/// Defines team member status values.
/// </summary>
public enum AgentTeamMemberStatus
{
    Active,
    Removed,
}

/// <summary>
/// Represents a team member.
/// </summary>
public sealed class AgentTeamMember
{
    public required string Id { get; init; }
    public required string Name { get; set; }
    public AgentTeamMemberRole Role { get; set; } = AgentTeamMemberRole.Member;
    public AgentTeamMemberStatus Status { get; set; } = AgentTeamMemberStatus.Active;
    public DateTimeOffset JoinedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public AgentTeamMember Clone() =>
        new()
        {
            Id = Id,
            Name = Name,
            Role = Role,
            Status = Status,
            JoinedAt = JoinedAt,
            UpdatedAt = UpdatedAt,
        };
}

/// <summary>
/// Represents a team.
/// </summary>
public sealed class AgentTeam
{
    public required string Id { get; init; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? LeadMemberId { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<AgentTeamMember> Members { get; set; } = [];

    public AgentTeamMember? GetMember(string memberId) =>
        Members.FirstOrDefault(member => string.Equals(member.Id, memberId, StringComparison.OrdinalIgnoreCase));

    public AgentTeamMember? GetMemberByName(string name) =>
        Members.FirstOrDefault(member => string.Equals(member.Name, name, StringComparison.OrdinalIgnoreCase));

    public bool RemoveMember(string memberId)
    {
        var member = GetMember(memberId);
        if (member == null)
            return false;

        member.Status = AgentTeamMemberStatus.Removed;
        member.UpdatedAt = DateTimeOffset.UtcNow;
        Members.Remove(member);

        if (string.Equals(LeadMemberId, member.Id, StringComparison.OrdinalIgnoreCase))
            LeadMemberId = null;

        UpdatedAt = DateTimeOffset.UtcNow;
        return true;
    }

    public AgentTeam Clone() =>
        new()
        {
            Id = Id,
            Name = Name,
            Description = Description,
            LeadMemberId = LeadMemberId,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            Members = Members.Select(member => member.Clone()).ToList(),
        };
}

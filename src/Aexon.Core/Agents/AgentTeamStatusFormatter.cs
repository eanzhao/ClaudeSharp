using System.Text;

namespace Aexon.Core.Agents;

/// <summary>
/// Formats team runtime state for terminal output.
/// </summary>
public static class AgentTeamStatusFormatter
{
    public static string FormatOverview(IReadOnlyList<AgentTeam> teams)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Teams:");

        if (teams.Count == 0)
        {
            builder.AppendLine("  (none)");
            return builder.ToString().TrimEnd();
        }

        foreach (var team in teams)
        {
            builder.AppendLine(
                $"  - {team.Id} | {team.Name} | members={team.Members.Count} | lead={FormatLead(team)}");
        }

        return builder.ToString().TrimEnd();
    }

    public static string FormatDetails(AgentTeam team)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Team: {team.Name} ({team.Id})");
        if (!string.IsNullOrWhiteSpace(team.Description))
            builder.AppendLine($"Description: {team.Description}");
        builder.AppendLine($"Lead: {FormatLead(team)}");
        builder.AppendLine($"Members: {team.Members.Count}");

        foreach (var member in team.Members
                     .OrderBy(member => member.Role == AgentTeamMemberRole.Lead ? 0 : 1)
                     .ThenBy(member => member.Name, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine(
                $"  - {member.Id} | {member.Name} | {member.Role} | {member.Status}");
        }

        return builder.ToString().TrimEnd();
    }

    public static bool TryFormatDetails(
        IReadOnlyList<AgentTeam> teams,
        string teamIdOrName,
        out string details)
    {
        var team = teams.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, teamIdOrName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(candidate.Name, teamIdOrName, StringComparison.OrdinalIgnoreCase));

        if (team == null)
        {
            details = $"Team '{teamIdOrName}' was not found.";
            return false;
        }

        details = FormatDetails(team);
        return true;
    }

    private static string FormatLead(AgentTeam team)
    {
        if (string.IsNullOrWhiteSpace(team.LeadMemberId))
            return "(none)";

        var lead = team.GetMember(team.LeadMemberId!);
        if (lead == null)
            return team.LeadMemberId!;

        return $"{lead.Name} ({lead.Id})";
    }
}

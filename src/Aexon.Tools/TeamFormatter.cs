using Aexon.Core.Agents;
using System.Text;

namespace Aexon.Tools;

/// <summary>
/// Formats team runtime state for CLI and tools.
/// </summary>
public static class TeamFormatter
{
    public static string FormatOverview(IAgentTeamRuntime runtime) =>
        AgentTeamStatusFormatter.FormatOverview(runtime.ListTeams());

    public static string FormatSummary(AgentTeam team)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Team: {team.Name} ({team.Id})");
        if (!string.IsNullOrWhiteSpace(team.Description))
            builder.AppendLine($"Description: {team.Description}");
        builder.AppendLine($"Lead: {FormatLead(team)}");
        builder.AppendLine($"Members: {team.Members.Count}");
        return builder.ToString().TrimEnd();
    }

    public static string FormatDetails(AgentTeam team) =>
        AgentTeamStatusFormatter.FormatDetails(team);

    public static string FormatCreateResult(AgentTeam team) =>
        $"Team created: {team.Id}\n{FormatDetails(team)}";

    public static string FormatDissolveResult(AgentTeam team, string? reason = null)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Team dissolved: {team.Id}");
        builder.AppendLine(FormatSummary(team));
        if (!string.IsNullOrWhiteSpace(reason))
            builder.AppendLine($"Reason: {reason.Trim()}");

        return builder.ToString().TrimEnd();
    }

    private static string FormatLead(AgentTeam team)
    {
        if (string.IsNullOrWhiteSpace(team.LeadMemberId))
            return "(none)";

        var lead = team.GetMember(team.LeadMemberId!);
        return lead == null ? team.LeadMemberId! : lead.Name;
    }
}

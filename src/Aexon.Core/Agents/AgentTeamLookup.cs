namespace Aexon.Core.Agents;

/// <summary>
/// Provides common team lookup helpers.
/// </summary>
public static class AgentTeamLookup
{
    public static AgentTeam? ResolveTeam(
        IAgentTeamRuntime runtime,
        string teamIdOrName)
    {
        if (string.IsNullOrWhiteSpace(teamIdOrName))
            return null;

        return runtime.GetTeam(teamIdOrName.Trim()) ??
               runtime.FindTeamByName(teamIdOrName.Trim());
    }

    public static AgentTeamMember? ResolveMember(
        AgentTeam team,
        string memberIdOrName)
    {
        if (string.IsNullOrWhiteSpace(memberIdOrName))
            return null;

        var value = memberIdOrName.Trim();
        return team.GetMember(value) ?? team.GetMemberByName(value);
    }
}

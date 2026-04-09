using ClaudeSharp.Core.Agents;

namespace ClaudeSharp.Core.Tests.Agents;

/// <summary>
/// Contains tests for the agent team runtime.
/// </summary>
public sealed class AgentTeamRuntimeTests
{
    [Fact]
    public void InMemoryRuntime_CreatesUpdatesAndFormatsTeams()
    {
        var runtime = new InMemoryAgentTeamRuntime();

        var team = runtime.CreateTeam(
            "platform",
            description: "Core runtime team",
            leadName: "Alice");

        Assert.Equal("team-1", team.Id);
        Assert.Equal("platform", team.Name);
        Assert.Equal("Core runtime team", team.Description);
        Assert.Single(team.Members);
        Assert.Equal("Alice", team.Members[0].Name);
        Assert.Equal(AgentTeamMemberRole.Lead, team.Members[0].Role);

        var bob = runtime.AddMember(team.Id, "Bob");
        Assert.NotNull(bob);
        Assert.Equal(AgentTeamMemberRole.Member, bob!.Role);

        Assert.True(runtime.RenameTeam(team.Id, "platform-core"));
        Assert.True(runtime.SetTeamDescription(team.Id, "Runtime and team orchestration"));
        Assert.True(runtime.SetLead(team.Id, bob.Id));

        var snapshot = Assert.Single(runtime.ListTeams());
        Assert.Equal("platform-core", snapshot.Name);
        Assert.Equal("Runtime and team orchestration", snapshot.Description);
        Assert.Equal(bob.Id, snapshot.LeadMemberId);
        Assert.Equal(2, snapshot.Members.Count);

        Assert.True(runtime.RemoveMember(team.Id, "team-member-1"));
        snapshot = Assert.Single(runtime.ListTeams());
        Assert.Single(snapshot.Members);
        Assert.Equal(bob.Id, snapshot.LeadMemberId);

        var overview = AgentTeamStatusFormatter.FormatOverview(runtime.ListTeams());
        var details = AgentTeamStatusFormatter.FormatDetails(snapshot);

        Assert.Contains("platform-core", overview, StringComparison.Ordinal);
        Assert.Contains("members=1", overview, StringComparison.Ordinal);
        Assert.Contains("Lead: Bob", details, StringComparison.Ordinal);
        Assert.Contains("team-member-2", details, StringComparison.Ordinal);
    }

    [Fact]
    public void InMemoryRuntime_FindsTeamsByName()
    {
        var runtime = new InMemoryAgentTeamRuntime();
        runtime.CreateTeam("alpha");
        runtime.CreateTeam("beta", leadName: "Lead");

        var found = runtime.FindTeamByName("beta");

        Assert.NotNull(found);
        Assert.Equal("beta", found!.Name);
        Assert.Equal("Lead", Assert.Single(found.Members).Name);
        Assert.Equal(found.Id, runtime.GetTeam(found.Id)?.Id);
    }
}

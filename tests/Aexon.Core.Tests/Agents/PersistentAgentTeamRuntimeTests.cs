using Aexon.Core.Agents;
using Aexon.Core.Tests.Runtime;

namespace Aexon.Core.Tests.Agents;

/// <summary>
/// Contains tests for persistent agent team runtime.
/// </summary>
public sealed class PersistentAgentTeamRuntimeTests
{
    [Fact]
    public async Task CreateAsync_PersistsSnapshotsAndRestoresLatestState()
    {
        var journal = new RecordingJournal();
        var runtime = await PersistentAgentTeamRuntime.CreateAsync(journal);

        var team = runtime.CreateTeam("platform", description: "Core runtime team", leadName: "Alice");
        var bob = runtime.AddMember(team.Id, "Bob");
        Assert.NotNull(bob);
        Assert.True(runtime.SetLead(team.Id, bob!.Id));
        Assert.True(runtime.RenameTeam(team.Id, "platform-core"));

        Assert.Equal(4, journal.MetadataEntries.Count);
        Assert.All(
            journal.MetadataEntries,
            entry => Assert.Equal(AgentTeamPersistence.TeamEventType, entry.EventType));

        var restored = await PersistentAgentTeamRuntime.CreateAsync(
            new RecordingJournal(),
            journal.MetadataEntries);

        var snapshot = Assert.Single(restored.ListTeams());
        Assert.Equal("platform-core", snapshot.Name);
        Assert.Equal("Core runtime team", snapshot.Description);
        Assert.Equal(bob.Id, snapshot.LeadMemberId);
        Assert.Equal(2, snapshot.Members.Count);

        var lead = Assert.Single(snapshot.Members, member => member.Role == AgentTeamMemberRole.Lead);
        Assert.Equal("Bob", lead.Name);
        var alice = Assert.Single(snapshot.Members, member => member.Name == "Alice");
        Assert.Equal(AgentTeamMemberRole.Member, alice.Role);
    }

    [Fact]
    public async Task DeleteTeam_PersistsDeletionAndRestoresAsEmpty()
    {
        var journal = new RecordingJournal();
        var runtime = await PersistentAgentTeamRuntime.CreateAsync(journal);

        var team = runtime.CreateTeam("platform", leadName: "Alice");
        Assert.True(runtime.DeleteTeam(team.Id));

        Assert.Equal(2, journal.MetadataEntries.Count);
        Assert.Equal(AgentTeamPersistence.TeamDeletedEventType, journal.MetadataEntries[^1].EventType);

        var restored = await PersistentAgentTeamRuntime.CreateAsync(
            new RecordingJournal(),
            journal.MetadataEntries);

        Assert.Empty(restored.ListTeams());
    }
}

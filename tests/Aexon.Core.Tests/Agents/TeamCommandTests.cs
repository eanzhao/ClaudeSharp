using Aexon.Commands;
using Aexon.Core.Agents;
using Aexon.Core.Commands;
using Aexon.Core.Permissions;
using Aexon.Core.Tools;

namespace Aexon.Core.Tests.Agents;

/// <summary>
/// Contains tests for the team slash command.
/// </summary>
public sealed class TeamCommandTests
{
    [Fact]
    public async Task ExecuteAsync_CanListCreateInspectAndDissolveTeams()
    {
        var runtime = new InMemoryAgentTeamRuntime();
        var lines = new List<string>();
        var command = new TeamCommand(runtime);

        await command.ExecuteAsync(string.Empty, CreateContext(lines));
        await command.ExecuteAsync(
            "create Platform --lead Ada --member Bob --description Core platform team",
            CreateContext(lines));

        var created = AgentTeamLookup.ResolveTeam(runtime, "Platform");
        Assert.NotNull(created);

        await command.ExecuteAsync("show Platform", CreateContext(lines));
        await command.ExecuteAsync("dissolve Platform retired", CreateContext(lines));
        await command.ExecuteAsync("status", CreateContext(lines));

        var output = string.Join(Environment.NewLine, lines);
        Assert.Contains("Teams:", output, StringComparison.Ordinal);
        Assert.Contains("(none)", output, StringComparison.Ordinal);
        Assert.Contains("Team created: team-1", output, StringComparison.Ordinal);
        Assert.Contains("Team: Platform (team-1)", output, StringComparison.Ordinal);
        Assert.Contains("Lead: Ada", output, StringComparison.Ordinal);
        Assert.Contains("Members: 2", output, StringComparison.Ordinal);
        Assert.Contains("Team dissolved: team-1", output, StringComparison.Ordinal);
        Assert.Contains("Reason: retired", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_ReportsUsageAndUnknownTeams()
    {
        var runtime = new InMemoryAgentTeamRuntime();
        var lines = new List<string>();
        var command = new TeamCommand(runtime);

        await command.ExecuteAsync("create", CreateContext(lines));
        await command.ExecuteAsync("show missing-team", CreateContext(lines));
        await command.ExecuteAsync("dissolve", CreateContext(lines));
        await command.ExecuteAsync("missing-team", CreateContext(lines));

        var output = string.Join(Environment.NewLine, lines);
        Assert.Contains("team name is required.", output, StringComparison.Ordinal);
        Assert.Contains("No team matched 'missing-team'.", output, StringComparison.Ordinal);
        Assert.Contains("team id or name is required.", output, StringComparison.Ordinal);
        Assert.Contains("Usage: /team", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_CanInspectBareTeamName()
    {
        var runtime = new InMemoryAgentTeamRuntime();
        var team = runtime.CreateTeam("Ops", leadName: "Mia");
        runtime.AddMember(team.Id, "Ben");
        var lines = new List<string>();
        var command = new TeamCommand(runtime);

        await command.ExecuteAsync("Ops", CreateContext(lines));

        var output = string.Join(Environment.NewLine, lines);
        Assert.Contains("Team: Ops (team-1)", output, StringComparison.Ordinal);
        Assert.Contains("Lead: Mia", output, StringComparison.Ordinal);
        Assert.Contains("Members: 2", output, StringComparison.Ordinal);
    }

    private static CommandContext CreateContext(List<string> lines) =>
        new()
        {
            WriteLine = lines.Add,
            Tools = new ToolRegistry(),
            QueryEngine = null!,
            PermissionContext = new PermissionContext(),
            AgentTaskRuntime = new InMemoryAgentTaskRuntime(),
            AgentTeamRuntime = null,
            Commands = [],
            CancellationToken = CancellationToken.None,
        };
}

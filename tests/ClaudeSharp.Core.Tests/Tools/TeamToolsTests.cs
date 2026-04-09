using System.Text.Json;
using ClaudeSharp.Core.Agents;
using ClaudeSharp.Core.Messages;
using ClaudeSharp.Core.Permissions;
using ClaudeSharp.Core.Tools;
using ClaudeSharp.Tools;

namespace ClaudeSharp.Core.Tests.Tools;

/// <summary>
/// Contains tests for team tools.
/// </summary>
public sealed class TeamToolsTests
{
    [Fact]
    public async Task TeamCreateTool_ValidateAndExecute_CreatesTeamsWithRoster()
    {
        var runtime = new InMemoryAgentTeamRuntime();
        var tool = new TeamCreateTool(runtime);
        var context = CreateContext();

        var invalid = await tool.ValidateInputAsync(Json(new { }), context);
        var result = await tool.ExecuteAsync(
            Json(new
            {
                name = "Platform",
                lead = "Ada",
                description = "Core platform team",
                members = new[] { "Bob", "Cara" },
            }),
            context);

        Assert.False(invalid.IsValid);
        Assert.Equal("name is required.", invalid.Message);
        Assert.False(result.IsError);
        Assert.Contains("Team created: team-1", result.Data, StringComparison.Ordinal);
        Assert.Contains("Team: Platform (team-1)", result.Data, StringComparison.Ordinal);
        Assert.Contains("Lead: Ada", result.Data, StringComparison.Ordinal);
        Assert.Contains("Members: 3", result.Data, StringComparison.Ordinal);
        Assert.Contains("Bob", result.Data, StringComparison.Ordinal);
        Assert.Contains("Cara", result.Data, StringComparison.Ordinal);
        Assert.False(tool.IsConcurrencySafe(default));
        Assert.False(tool.IsReadOnly(default));
        Assert.Equal("Create team", tool.GetUserFacingName());
        Assert.Equal("Creating team", tool.GetActivityDescription(null));
    }

    [Fact]
    public async Task TeamStatusTool_ExecuteAsync_RendersOverviewAndDetails()
    {
        var runtime = new InMemoryAgentTeamRuntime();
        var platform = runtime.CreateTeam("Platform", leadName: "Ada");
        runtime.AddMember(platform.Id, "Bob");
        var ops = runtime.CreateTeam("Ops");
        runtime.AddMember(ops.Id, "Mia");

        var tool = new TeamStatusTool(runtime);
        var context = CreateContext();

        var overview = await tool.ExecuteAsync(Json(new { }), context);
        var details = await tool.ExecuteAsync(Json(new { id = "Platform" }), context);
        var summaryOnly = await tool.ExecuteAsync(Json(new { id = "Platform", include_members = false }), context);
        var missing = await tool.ExecuteAsync(Json(new { id = "missing" }), context);

        Assert.False(overview.IsError);
        Assert.Contains("Teams:", overview.Data, StringComparison.Ordinal);
        Assert.Contains("Platform", overview.Data, StringComparison.Ordinal);
        Assert.Contains("Ops", overview.Data, StringComparison.Ordinal);
        Assert.False(details.IsError);
        Assert.Contains("Team: Platform (team-1)", details.Data, StringComparison.Ordinal);
        Assert.Contains("Lead: Ada", details.Data, StringComparison.Ordinal);
        Assert.Contains("Members: 2", details.Data, StringComparison.Ordinal);
        Assert.False(summaryOnly.IsError);
        Assert.DoesNotContain("team-member-", summaryOnly.Data, StringComparison.Ordinal);
        Assert.True(missing.IsError);
        Assert.Contains("No team matched 'missing'.", missing.Data, StringComparison.Ordinal);
        Assert.True(tool.IsReadOnly(default));
        Assert.True(tool.IsConcurrencySafe(default));
        Assert.Equal("Team status", tool.GetUserFacingName());
        Assert.Equal("Inspecting team", tool.GetActivityDescription(null));
    }

    [Fact]
    public async Task TeamDissolveTool_ExecuteAsync_DissolvesTeamsAndRejectsMissingTargets()
    {
        var runtime = new InMemoryAgentTeamRuntime();
        var team = runtime.CreateTeam("Ops");
        runtime.AddMember(team.Id, "Mia");
        var tool = new TeamDissolveTool(runtime);
        var context = CreateContext();

        var invalid = await tool.ValidateInputAsync(Json(new { }), context);
        var result = await tool.ExecuteAsync(Json(new { id = team.Id, reason = "retired" }), context);
        var missing = await tool.ExecuteAsync(Json(new { id = "missing" }), context);

        Assert.False(invalid.IsValid);
        Assert.Equal("id is required.", invalid.Message);
        Assert.False(result.IsError);
        Assert.Contains("Team dissolved: team-1", result.Data, StringComparison.Ordinal);
        Assert.Contains("Team: Ops (team-1)", result.Data, StringComparison.Ordinal);
        Assert.Contains("Reason: retired", result.Data, StringComparison.Ordinal);
        Assert.Null(runtime.GetTeam(team.Id));
        Assert.True(missing.IsError);
        Assert.Contains("No team matched 'missing'.", missing.Data, StringComparison.Ordinal);
        Assert.False(tool.IsReadOnly(default));
        Assert.False(tool.IsConcurrencySafe(default));
        Assert.Equal("Dissolve team", tool.GetUserFacingName());
        Assert.Equal("Dissolving team", tool.GetActivityDescription(null));
    }

    private static JsonElement Json(object value) =>
        JsonSerializer.SerializeToElement(value);

    private static ToolExecutionContext CreateContext() =>
        new()
        {
            WorkingDirectory = "/tmp",
            PermissionContext = new PermissionContext(),
            Tools = [],
            Messages = [],
            CancellationToken = CancellationToken.None,
        };
}

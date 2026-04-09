using ClaudeSharp.Core.Agents;
using ClaudeSharp.Core.AppState;
using ClaudeSharp.Core.Configuration;
using ClaudeSharp.Core.Mcp;
using ClaudeSharp.Core.Permissions;

namespace ClaudeSharp.Core.Tests.AppState;

/// <summary>
/// Contains tests for app-state projection.
/// </summary>
public sealed class AppStateProjectorTests
{
    [Fact]
    public void CreateSnapshot_ProjectsMcpConnectionsAgentWorkItemsAndTeams()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var pending = runtime.CreateWorkItem("pending");
        var active = runtime.CreateWorkItem("active");
        runtime.UpdateWorkItem(active.Id, item => item.Status = AgentWorkItemStatus.InProgress);
        var teamRuntime = new InMemoryAgentTeamRuntime();
        var team = teamRuntime.CreateTeam("Platform", leadName: "Ada");
        teamRuntime.AddMember(team.Id, "Bob");

        var mcp = new McpConnectionManager();
        mcp.Register(new McpConnection("alpha"));
        mcp.Register(new McpConnection("beta"));
        mcp.UpdateState("alpha", McpConnectionState.Connected);
        mcp.UpdateState("beta", McpConnectionState.Failed);

        var projector = new AppStateProjector();
        var snapshot = projector.CreateSnapshot(
            "/workspace",
            PermissionMode.Plan,
            sessionId: "session-1",
            memoryRootDirectory: "/memory/project",
            managedSettings: new ManagedSettingsSnapshot
            {
                OrganizationPolicy = new OrganizationPolicySnapshot
                {
                    OrganizationId = "org-1",
                },
            },
            activeTokenSource: new AnthropicTokenSourceSnapshot
            {
                Id = "environment",
                Kind = AnthropicTokenSourceKind.EnvironmentVariable,
                IsActive = true,
            },
            mcpConnectionManager: mcp,
            agentTaskRuntime: runtime,
            agentTeamRuntime: teamRuntime);

        Assert.Equal("session-1", snapshot.SessionId);
        Assert.Equal("/workspace", snapshot.WorkingDirectory);
        Assert.Equal(PermissionMode.Plan, snapshot.PermissionMode);
        Assert.Equal("/memory/project", snapshot.MemoryRootDirectory);
        Assert.Equal(active.Id, snapshot.ActiveTaskId);
        Assert.Equal("org-1", snapshot.ManagedSettings.OrganizationPolicy.OrganizationId);
        Assert.Equal("environment", snapshot.ActiveTokenSource?.Id);
        Assert.Equal(McpConnectionState.Connected, snapshot.McpConnections["alpha"]);
        Assert.Equal(McpConnectionState.Failed, snapshot.McpConnections["beta"]);
        Assert.Equal(AgentWorkItemStatus.Pending, snapshot.WorkItems[pending.Id]);
        Assert.Equal(AgentWorkItemStatus.InProgress, snapshot.WorkItems[active.Id]);
        var projectedTeam = Assert.Single(snapshot.Teams);
        Assert.Equal("Platform", projectedTeam.Name);
        Assert.Equal("Ada", projectedTeam.LeadName);
        Assert.Equal(["Ada", "Bob"], projectedTeam.Members);
    }
}

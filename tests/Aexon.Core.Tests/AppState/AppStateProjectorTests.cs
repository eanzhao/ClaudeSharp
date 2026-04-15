using Aexon.Core.Agents;
using Aexon.Core.AppState;
using Aexon.Core.Channels;
using Aexon.Core.Configuration;
using Aexon.Core.Mcp;
using Aexon.Core.Permissions;
using Aexon.Core.Todos;

namespace Aexon.Core.Tests.AppState;

/// <summary>
/// Contains tests for app-state projection.
/// </summary>
public sealed class AppStateProjectorTests
{
    [Fact]
    public void CreateSnapshot_ProjectsMcpConnectionsAgentWorkItemsTeamsAndMailboxes()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var pending = runtime.CreateWorkItem("pending");
        var active = runtime.CreateWorkItem("active");
        runtime.UpdateWorkItem(active.Id, item => item.Status = AgentWorkItemStatus.InProgress);
        var messageRuntime = new InMemoryAgentMessageRuntime();
        messageRuntime.SendMessage("lead", "Ada", AgentMessageKind.PlanApprovalRequest, "Approve launch", subject: "Launch");
        messageRuntime.SendMessage(
            "lead",
            "Ada",
            "Please confirm the rollout",
            AgentMessageKind.Note,
            subject: "Launch",
            protocol: new AgentMessageProtocol
            {
                ActionName = "follow-up-request",
                RequiresResponse = true,
            });
        var teamRuntime = new InMemoryAgentTeamRuntime();
        var team = teamRuntime.CreateTeam("Platform", leadName: "Ada");
        teamRuntime.AddMember(team.Id, "Bob");
        var todoRuntime = new InMemoryTodoRuntime();
        todoRuntime.CreateTodo(
            "issue-4",
            "Implement TodoWrite",
            TodoStatus.InProgress,
            "Persist todos into session state");

        var mcp = new McpConnectionManager();
        mcp.Register(new McpConnection("alpha"));
        mcp.Register(new McpConnection("beta"));
        mcp.UpdateState("alpha", McpConnectionState.Connected);
        mcp.UpdateState("beta", McpConnectionState.Failed);

        var projector = new AppStateProjector();
        var snapshot = projector.CreateSnapshot(
            "/workspace",
            PermissionMode.Plan,
            AgentAutoResumeMode.Latest,
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
            agentTeamRuntime: teamRuntime,
            agentMessageRuntime: messageRuntime,
            todoRuntime: todoRuntime);

        Assert.Equal("session-1", snapshot.SessionId);
        Assert.Equal("/workspace", snapshot.WorkingDirectory);
        Assert.Equal(PermissionMode.Plan, snapshot.PermissionMode);
        Assert.Equal(AgentAutoResumeMode.Latest, snapshot.AutoResumeMode);
        Assert.Equal("/memory/project", snapshot.MemoryRootDirectory);
        Assert.Equal(active.Id, snapshot.ActiveTaskId);
        Assert.Equal("org-1", snapshot.ManagedSettings.OrganizationPolicy.OrganizationId);
        Assert.Equal("environment", snapshot.ActiveTokenSource?.Id);
        Assert.Equal(McpConnectionState.Connected, snapshot.McpConnections["alpha"]);
        Assert.Equal(McpConnectionState.Failed, snapshot.McpConnections["beta"]);
        Assert.Equal(AgentWorkItemStatus.Pending, snapshot.WorkItems[pending.Id]);
        Assert.Equal(AgentWorkItemStatus.InProgress, snapshot.WorkItems[active.Id]);
        Assert.Equal(0, snapshot.TaskAttention.AwaitingApprovalCount);
        Assert.Equal(0, snapshot.TaskAttention.AwaitingResumeCount);
        var projectedTeam = Assert.Single(snapshot.Teams);
        Assert.Equal("Platform", projectedTeam.Name);
        Assert.Equal("Ada", projectedTeam.LeadName);
        Assert.Equal(["Ada", "Bob"], projectedTeam.Members);
        var adaMailbox = Assert.Single(snapshot.Mailboxes, mailbox =>
            string.Equals(mailbox.Participant, "Ada", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, adaMailbox.PendingActionCount);
        Assert.Equal(1, adaMailbox.PendingPlanApprovalCount);
        Assert.Equal(2, adaMailbox.InboxCount);
        Assert.Equal(2, adaMailbox.UnreadCount);
        var projectedTodo = Assert.Single(snapshot.Todos);
        Assert.Equal("issue-4", projectedTodo.Id);
        Assert.Equal("Implement TodoWrite", projectedTodo.Title);
        Assert.Equal("in_progress", projectedTodo.Status);
        Assert.Equal("Persist todos into session state", projectedTodo.Description);
    }

    [Fact]
    public void CreateSnapshot_SelectsAwaitingApprovalWhenNoTaskIsRunning()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var pending = runtime.CreateWorkItem("pending");
        var awaiting = runtime.CreateWorkItem("awaiting");
        runtime.UpdateWorkItem(awaiting.Id, item =>
        {
            item.Status = AgentWorkItemStatus.AwaitingApproval;
            item.ApprovalRequestId = "agent-message-1";
        });

        var projector = new AppStateProjector();
        var snapshot = projector.CreateSnapshot(
            "/workspace",
            PermissionMode.Default,
            agentTaskRuntime: runtime);

        Assert.Equal(awaiting.Id, snapshot.ActiveTaskId);
        Assert.Equal(AgentAutoResumeMode.Queue, snapshot.AutoResumeMode);
        Assert.Equal(AgentWorkItemStatus.Pending, snapshot.WorkItems[pending.Id]);
        Assert.Equal(AgentWorkItemStatus.AwaitingApproval, snapshot.WorkItems[awaiting.Id]);
        Assert.Equal(1, snapshot.TaskAttention.AwaitingApprovalCount);
        Assert.Equal(0, snapshot.TaskAttention.AwaitingResumeCount);
    }

    [Fact]
    public void CreateSnapshot_PrefersAwaitingResumeOverAwaitingApprovalWhenIdle()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var awaitingApproval = runtime.CreateWorkItem("awaiting approval");
        runtime.UpdateWorkItem(awaitingApproval.Id, item =>
        {
            item.Status = AgentWorkItemStatus.AwaitingApproval;
            item.ApprovalRequestId = "agent-message-1";
        });
        var awaitingResume = runtime.CreateWorkItem("awaiting resume");
        runtime.UpdateWorkItem(awaitingResume.Id, item =>
        {
            item.Status = AgentWorkItemStatus.AwaitingResume;
            item.Owner = "subagent";
        });

        var projector = new AppStateProjector();
        var snapshot = projector.CreateSnapshot(
            "/workspace",
            PermissionMode.Default,
            agentTaskRuntime: runtime);

        Assert.Equal(awaitingResume.Id, snapshot.ActiveTaskId);
        Assert.Equal(AgentAutoResumeMode.Queue, snapshot.AutoResumeMode);
        Assert.Equal(AgentWorkItemStatus.AwaitingApproval, snapshot.WorkItems[awaitingApproval.Id]);
        Assert.Equal(AgentWorkItemStatus.AwaitingResume, snapshot.WorkItems[awaitingResume.Id]);
        Assert.Equal(1, snapshot.TaskAttention.AwaitingApprovalCount);
        Assert.Equal(1, snapshot.TaskAttention.AwaitingResumeCount);
    }

    [Fact]
    public void CreateSnapshot_PrefersRuntimeOptionsForAutoResumeMode()
    {
        var projector = new AppStateProjector();
        var runtimeOptions = new AgentRuntimeOptions
        {
            AutoResumeMode = AgentAutoResumeMode.Disabled,
        };

        var snapshot = projector.CreateSnapshot(
            "/workspace",
            PermissionMode.Default,
            AgentAutoResumeMode.Latest,
            agentRuntimeOptions: runtimeOptions);

        Assert.Equal(AgentAutoResumeMode.Disabled, snapshot.AutoResumeMode);
    }

    [Fact]
    public void CreateSnapshot_ProjectsChannelConnections()
    {
        var channelManager = new ChannelConnectionManager();
        channelManager.Register("remote-1", ChannelKind.Bridge, "bridge:remote-1");
        channelManager.UpdateState("remote-1", ChannelConnectionState.Connected);
        channelManager.Register("/tmp/agent.sock", ChannelKind.Uds, "uds:/tmp/agent.sock");
        channelManager.UpdateState("/tmp/agent.sock", ChannelConnectionState.Failed, "Socket not found");

        var projector = new AppStateProjector();
        var snapshot = projector.CreateSnapshot(
            "/workspace",
            PermissionMode.Default,
            channelConnectionManager: channelManager);

        Assert.Equal(2, snapshot.ChannelConnections.Count);
        var bridge = Assert.Single(snapshot.ChannelConnections, c => c.Kind == ChannelKind.Bridge);
        Assert.Equal("remote-1", bridge.ChannelId);
        Assert.Equal(ChannelConnectionState.Connected, bridge.State);
        Assert.Null(bridge.ErrorMessage);
        var uds = Assert.Single(snapshot.ChannelConnections, c => c.Kind == ChannelKind.Uds);
        Assert.Equal("/tmp/agent.sock", uds.ChannelId);
        Assert.Equal(ChannelConnectionState.Failed, uds.State);
        Assert.Equal("Socket not found", uds.ErrorMessage);
    }

    [Fact]
    public void CreateSnapshot_ReturnsEmptyChannelConnectionsWhenManagerIsNull()
    {
        var projector = new AppStateProjector();
        var snapshot = projector.CreateSnapshot(
            "/workspace",
            PermissionMode.Default);

        Assert.Empty(snapshot.ChannelConnections);
    }
}

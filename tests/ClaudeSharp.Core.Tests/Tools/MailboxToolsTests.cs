using System.Text.Json;
using ClaudeSharp.Core.Agents;
using ClaudeSharp.Core.Permissions;
using ClaudeSharp.Core.Tools;
using ClaudeSharp.Tools;

namespace ClaudeSharp.Core.Tests.Tools;

/// <summary>
/// Contains tests for mailbox tools.
/// </summary>
public sealed class MailboxToolsTests
{
    [Fact]
    public async Task SendMessageTool_ExecuteAsync_DeliversDirectAndBroadcastMessages()
    {
        var messages = new InMemoryAgentMessageRuntime();
        var teams = new InMemoryAgentTeamRuntime();
        var team = teams.CreateTeam("Platform", leadName: "Ada");
        teams.AddMember(team.Id, "Bob");

        var tool = new SendMessageTool(messages, teams);
        var context = CreateContext();

        var direct = await tool.ExecuteAsync(
            Json(new
            {
                request = new
                {
                    to = "Bob",
                    from = "Ada",
                    team_name = "Platform",
                    message = "Please inspect the runtime",
                },
            }),
            context);
        var broadcast = await tool.ExecuteAsync(
            Json(new
            {
                request = new
                {
                    to = "*",
                    from = "Ada",
                    team_name = "Platform",
                    message = new
                    {
                        kind = "ShutdownRequest",
                        body = "Pause work",
                    },
                },
            }),
            context);

        Assert.False(direct.IsError);
        Assert.False(broadcast.IsError);
        Assert.Contains("Delivered 1 message(s).", direct.Data, StringComparison.Ordinal);
        Assert.Contains("Delivered 2 message(s).", broadcast.Data, StringComparison.Ordinal);

        var delivered = messages.ListMessages();
        Assert.Equal(3, delivered.Count);
        Assert.Contains(delivered, message => message.To == "Platform/Bob");
        Assert.Contains(delivered, message => message.To == "Platform/Ada");
        Assert.Contains(delivered, message => message.Kind == AgentMessageKind.ShutdownRequest);
        Assert.False(tool.IsReadOnly(default));
    }

    [Fact]
    public async Task SendMessageTool_ExecuteAsync_ReportsReactivatedRecipients()
    {
        var messages = new InMemoryAgentMessageRuntime();
        var activations = new InMemoryAgentMessageActivationRuntime();
        activations.RegisterOwner(
            "Platform/Ada",
            (request, _) =>
            {
                Assert.Equal("resume-review", request.Message.Protocol?.ActionName);
                Assert.Equal("Need follow-up on the current thread", request.ResumeReason);
                return Task.FromResult(AgentMessageActivationResult.Reactivated(
                    "Platform/Ada",
                    "background-run-7",
                    "work-item-9",
                    $"Triggered by {request.Message.Id} in {request.Message.ThreadId}."));
            });

        var tool = new SendMessageTool(messages, activationRuntime: activations);

        var result = await tool.ExecuteAsync(
            Json(new
            {
                request = new
                {
                    to = "Platform/Ada",
                    from = "main",
                    message = new
                    {
                        kind = "Note",
                        body = "Please resume investigation",
                        action = "resume-review",
                        requires_response = true,
                        resume_reason = "Need follow-up on the current thread",
                    },
                },
            }),
            CreateContext());

        Assert.False(result.IsError);
        Assert.Contains("Reactivated Platform/Ada as background-run-7", result.Data, StringComparison.Ordinal);
        Assert.Contains("Triggered by agent-message-1 in thread-1.", result.Data, StringComparison.Ordinal);
        var delivered = Assert.Single(messages.ListMessages());
        Assert.Equal("resume-review", delivered.Protocol?.ActionName);
        Assert.True(delivered.Protocol?.RequiresResponse);
        Assert.Equal("Need follow-up on the current thread", delivered.Protocol?.ResumeReason);
    }

    [Fact]
    public async Task SendMessageTool_ExecuteAsync_SynchronizesPlanApprovalTodo()
    {
        var messages = new InMemoryAgentMessageRuntime();
        var tasks = new InMemoryAgentTaskRuntime();
        var tool = new SendMessageTool(messages, taskRuntime: tasks);

        var result = await tool.ExecuteAsync(
            Json(new
            {
                request = new
                {
                    to = "Platform/Ada",
                    from = "lead",
                    message = new
                    {
                        kind = "PlanApprovalRequest",
                        body = "Approve this launch plan",
                        subject = "Launch",
                    },
                },
            }),
            CreateContext());

        Assert.False(result.IsError);
        var workItem = Assert.Single(tasks.ListWorkItems());
        Assert.Equal("Approval: Launch", workItem.Title);
        Assert.Equal(AgentWorkItemStatus.Pending, workItem.Status);
        Assert.Equal("Platform/Ada", workItem.Owner);
        Assert.Equal(AgentWorkItemSourceKinds.MailboxPlanApproval, workItem.SourceKind);
    }

    [Fact]
    public async Task MailboxStatusTool_ExecuteAsync_RendersOverviewFiltersAndDetails()
    {
        var runtime = new InMemoryAgentMessageRuntime();
        var first = runtime.SendMessage("main", "Platform/Ada", "Check build");
        runtime.SendMessage("Platform/Ada", "main", AgentMessageKind.Note, "Build passed", relatedMessageId: first.Id);

        var tool = new MailboxStatusTool(runtime);
        var context = CreateContext();

        var overview = await tool.ExecuteAsync(Json(new { request = new { } }), context);
        var participant = await tool.ExecuteAsync(
            Json(new { request = new { participant = "main", unread_only = true } }),
            context);
        var details = await tool.ExecuteAsync(
            Json(new { request = new { message_id = first.Id, mark_as_read = true } }),
            context);

        Assert.False(overview.IsError);
        Assert.Contains("Mailbox:", overview.Data, StringComparison.Ordinal);
        Assert.False(participant.IsError);
        Assert.Contains("Platform/Ada -> main", participant.Data, StringComparison.Ordinal);
        Assert.False(details.IsError);
        Assert.Contains($"Message: {first.Id}", details.Data, StringComparison.Ordinal);
        Assert.Contains("Status: Read", details.Data, StringComparison.Ordinal);
        Assert.False(tool.IsReadOnly(Json(new { request = new { message_id = first.Id, mark_as_read = true } })));
    }

    [Fact]
    public async Task MailboxStatusTool_ExecuteAsync_RendersInboxOutboxAndThreadViews()
    {
        var runtime = new InMemoryAgentMessageRuntime();
        var first = runtime.SendMessage("main", "Platform/Ada", "Check build");
        runtime.SendMessage("Platform/Ada", "main", AgentMessageKind.Note, "Build passed", relatedMessageId: first.Id);
        runtime.SendMessage("main", "Platform/Ada", AgentMessageKind.PlanApprovalRequest, "Please confirm deploy", subject: "Deploy");

        var tool = new MailboxStatusTool(runtime);
        var context = CreateContext();

        var inbox = await tool.ExecuteAsync(
            Json(new { request = new { view = "inbox", participant = "Platform/Ada" } }),
            context);
        var outbox = await tool.ExecuteAsync(
            Json(new { request = new { view = "outbox", participant = "main" } }),
            context);
        var thread = await tool.ExecuteAsync(
            Json(new { request = new { view = "thread", thread_id = first.ThreadId, participant = "Platform/Ada", mark_as_read = true } }),
            context);

        Assert.False(inbox.IsError);
        Assert.Contains("Mailbox inbox: Platform/Ada", inbox.Data, StringComparison.Ordinal);
        Assert.Contains("main -> Platform/Ada", inbox.Data, StringComparison.Ordinal);

        Assert.False(outbox.IsError);
        Assert.Contains("Mailbox outbox: main", outbox.Data, StringComparison.Ordinal);
        Assert.Contains("main -> Platform/Ada", outbox.Data, StringComparison.Ordinal);

        Assert.False(thread.IsError);
        Assert.Contains($"Mailbox thread: {first.ThreadId}", thread.Data, StringComparison.Ordinal);
        Assert.Contains("Timeline:", thread.Data, StringComparison.Ordinal);
        Assert.Equal(AgentMessageStatus.Read, runtime.GetMessage(first.Id)?.Status);
    }

    [Fact]
    public async Task MailboxStatusTool_ExecuteAsync_ShowsStructuredProtocolFields()
    {
        var runtime = new InMemoryAgentMessageRuntime();
        var message = runtime.SendMessage(
            "main",
            "Platform/Ada",
            AgentMessageKind.Note,
            "Please continue review",
            subject: "Review",
            protocol: new AgentMessageProtocol
            {
                ActionName = "resume-review",
                RequiresResponse = true,
                ResumeReason = "The thread has new work",
            });
        var tool = new MailboxStatusTool(runtime);

        var details = await tool.ExecuteAsync(
            Json(new { request = new { message_id = message.Id } }),
            CreateContext());
        var thread = await tool.ExecuteAsync(
            Json(new { request = new { view = "thread", thread_id = message.ThreadId } }),
            CreateContext());

        Assert.False(details.IsError);
        Assert.Contains("Action: resume-review", details.Data, StringComparison.Ordinal);
        Assert.Contains("Requires response: true", details.Data, StringComparison.Ordinal);
        Assert.Contains("Resume reason: The thread has new work", details.Data, StringComparison.Ordinal);

        Assert.False(thread.IsError);
        Assert.Contains("Action: resume-review", thread.Data, StringComparison.Ordinal);
        Assert.Contains("Resume reason: The thread has new work", thread.Data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MailboxStatusTool_ExecuteAsync_RendersPendingActions()
    {
        var runtime = new InMemoryAgentMessageRuntime();
        runtime.SendMessage("lead", "Platform/Ada", AgentMessageKind.PlanApprovalRequest, "Approve this plan", subject: "Plan");
        runtime.SendMessage(
            "lead",
            "Platform/Ada",
            AgentMessageKind.Note,
            "Need a follow-up",
            protocol: new AgentMessageProtocol
            {
                RequiresResponse = true,
            });
        var tool = new MailboxStatusTool(runtime);

        var result = await tool.ExecuteAsync(
            Json(new { request = new { view = "pending", participant = "Platform/Ada" } }),
            CreateContext());

        Assert.False(result.IsError);
        Assert.Contains("Mailbox pending actions: Platform/Ada", result.Data, StringComparison.Ordinal);
        Assert.Contains("PlanApproval", result.Data, StringComparison.Ordinal);
        Assert.Contains("FollowUp", result.Data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MailboxRespondTool_ExecuteAsync_SendsResponseAndMarksOriginalRead()
    {
        var runtime = new InMemoryAgentMessageRuntime();
        var tasks = new InMemoryAgentTaskRuntime();
        var trigger = runtime.SendMessage("lead", "Platform/Ada", AgentMessageKind.PlanApprovalRequest, "Approve this plan", subject: "Plan");
        AgentMailboxTaskProjector.Synchronize(runtime, tasks);
        var tool = new MailboxRespondTool(runtime, taskRuntime: tasks);

        var result = await tool.ExecuteAsync(
            Json(new
            {
                request = new
                {
                    message_id = trigger.Id,
                    decision = "approve",
                    note = "Looks good",
                },
            }),
            CreateContext());

        Assert.False(result.IsError);
        Assert.Contains($"Responded to {trigger.Id}", result.Data, StringComparison.Ordinal);
        var all = runtime.ListThread(trigger.ThreadId);
        Assert.Equal(2, all.Count);
        Assert.Equal(AgentMessageKind.PlanApprovalResponse, all[0].Kind == AgentMessageKind.PlanApprovalRequest ? all[1].Kind : all[0].Kind);
        Assert.Equal(AgentMessageStatus.Read, runtime.GetMessage(trigger.Id)?.Status);
        var workItem = Assert.Single(tasks.ListWorkItems());
        Assert.Equal(AgentWorkItemStatus.Completed, workItem.Status);
        Assert.Contains("Decision: approved.", workItem.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MailboxStatusTool_ValidateInputAsync_RequiresThreadIdForThreadView()
    {
        var tool = new MailboxStatusTool(new InMemoryAgentMessageRuntime());

        var result = await tool.ValidateInputAsync(
            Json(new { request = new { view = "thread" } }),
            CreateContext());

        Assert.False(result.IsValid);
        Assert.Contains("thread_id", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MailboxStatusTool_ValidateInputAsync_RequiresParticipantForPendingView()
    {
        var tool = new MailboxStatusTool(new InMemoryAgentMessageRuntime());

        var result = await tool.ValidateInputAsync(
            Json(new { request = new { view = "pending" } }),
            CreateContext());

        Assert.False(result.IsValid);
        Assert.Contains("participant", result.Message, StringComparison.Ordinal);
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

using ClaudeSharp.Commands;
using ClaudeSharp.Core.Agents;
using ClaudeSharp.Core.Commands;
using ClaudeSharp.Core.Permissions;
using ClaudeSharp.Core.Tools;

namespace ClaudeSharp.Core.Tests.Commands;

/// <summary>
/// Contains tests for the mailbox slash command.
/// </summary>
public sealed class MailboxCommandTests
{
    [Fact]
    public async Task ExecuteAsync_CanShowReadAndFilterMailboxMessages()
    {
        var runtime = new InMemoryAgentMessageRuntime();
        var first = runtime.SendMessage("main", "Platform/Ada", "Inspect runtime");
        runtime.SendMessage("Platform/Ada", "main", AgentMessageKind.Note, "Done", relatedMessageId: first.Id);
        var lines = new List<string>();
        var command = new MailboxCommand(runtime);

        await command.ExecuteAsync("", CreateContext(lines));
        await command.ExecuteAsync($"show {first.Id}", CreateContext(lines));
        await command.ExecuteAsync($"read {first.Id}", CreateContext(lines));
        await command.ExecuteAsync("for main", CreateContext(lines));

        var output = string.Join(Environment.NewLine, lines);
        Assert.Contains("Mailbox:", output, StringComparison.Ordinal);
        Assert.Contains($"Message: {first.Id}", output, StringComparison.Ordinal);
        Assert.Contains("Status: Read", output, StringComparison.Ordinal);
        Assert.Contains("Platform/Ada -> main", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_CanRenderInboxOutboxAndThreadViews()
    {
        var runtime = new InMemoryAgentMessageRuntime();
        var first = runtime.SendMessage("main", "Platform/Ada", "Inspect runtime");
        runtime.SendMessage("Platform/Ada", "main", AgentMessageKind.Note, "Done", relatedMessageId: first.Id);
        var lines = new List<string>();
        var command = new MailboxCommand(runtime);

        await command.ExecuteAsync("inbox Platform/Ada", CreateContext(lines));
        await command.ExecuteAsync("outbox main", CreateContext(lines));
        await command.ExecuteAsync($"thread {first.ThreadId}", CreateContext(lines));

        var output = string.Join(Environment.NewLine, lines);
        Assert.Contains("Mailbox inbox: Platform/Ada", output, StringComparison.Ordinal);
        Assert.Contains("Mailbox outbox: main", output, StringComparison.Ordinal);
        Assert.Contains($"Mailbox thread: {first.ThreadId}", output, StringComparison.Ordinal);
        Assert.Contains("Timeline:", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_CanRenderPendingActionsAndRespond()
    {
        var runtime = new InMemoryAgentMessageRuntime();
        var taskRuntime = new InMemoryAgentTaskRuntime();
        var trigger = runtime.SendMessage("lead", "Platform/Ada", AgentMessageKind.PlanApprovalRequest, "Approve this plan", subject: "Plan");
        AgentMailboxTaskProjector.Synchronize(runtime, taskRuntime);
        var lines = new List<string>();
        var command = new MailboxCommand(runtime);

        await command.ExecuteAsync("pending Platform/Ada", CreateContext(lines, taskRuntime));
        await command.ExecuteAsync($"respond {trigger.Id} approve Looks good", CreateContext(lines, taskRuntime));

        var output = string.Join(Environment.NewLine, lines);
        Assert.Contains("Mailbox pending actions: Platform/Ada", output, StringComparison.Ordinal);
        Assert.Contains($"Responded to {trigger.Id}", output, StringComparison.Ordinal);
        Assert.Equal(AgentMessageStatus.Read, runtime.GetMessage(trigger.Id)?.Status);
        Assert.Contains(runtime.ListThread(trigger.ThreadId), message => message.Kind == AgentMessageKind.PlanApprovalResponse);
        Assert.Equal(AgentWorkItemStatus.Completed, Assert.Single(taskRuntime.ListWorkItems()).Status);
    }

    private static CommandContext CreateContext(
        List<string> lines,
        IAgentTaskRuntime? taskRuntime = null) =>
        new()
        {
            WriteLine = lines.Add,
            Tools = new ToolRegistry(),
            QueryEngine = null!,
            PermissionContext = new PermissionContext(),
            AgentTaskRuntime = taskRuntime ?? new InMemoryAgentTaskRuntime(),
            AgentMessageRuntime = null,
            Commands = [],
            CancellationToken = CancellationToken.None,
        };
}

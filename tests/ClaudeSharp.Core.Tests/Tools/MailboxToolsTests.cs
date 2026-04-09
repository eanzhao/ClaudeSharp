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

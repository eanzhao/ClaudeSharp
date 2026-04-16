using System.Text.Json;
using Aexon.Core.Agents;
using Aexon.Core.Permissions;
using Aexon.Core.Tests.Runtime;
using Aexon.Core.Tools;
using Aexon.Tools;

namespace Aexon.Core.Tests.Tools;

public sealed class RemoteTriggerAndMonitorToolsTests
{
    [Fact]
    public async Task RemoteTriggerTool_CanCreateListFireAndDeleteTriggers()
    {
        var tasks = new InMemoryAgentTaskRuntime();
        var task = tasks.CreateWorkItem("Handle webhook", owner: "subagent");
        var runtime = new InMemoryAgentRemoteTriggerRuntime(tasks);
        var tool = new RemoteTriggerTool(runtime);
        var context = CreateContext();

        var createdWebhook = await tool.ExecuteAsync(
            Json(new
            {
                action = "create",
                task_id = task.Id,
                kind = "webhook",
                description = "hook",
                secret = "secret-1",
            }),
            context);
        var createdSchedule = await tool.ExecuteAsync(
            Json(new
            {
                action = "create",
                task_id = task.Id,
                kind = "schedule",
                schedule = "*/5 * * * *",
            }),
            context);

        var webhook = runtime.ListTriggers().Single(trigger => trigger.Kind == AgentRemoteTriggerKind.Webhook);
        var wrongSecret = await tool.ExecuteAsync(
            Json(new
            {
                action = "fire",
                id = webhook.Id,
                secret = "bad",
            }),
            context);
        var fired = await tool.ExecuteAsync(
            Json(new
            {
                action = "fire",
                id = webhook.Id,
                secret = "secret-1",
                payload = "payload line",
            }),
            context);
        var listed = await tool.ExecuteAsync(
            Json(new { action = "list", task_id = task.Id }),
            context);
        var deleted = await tool.ExecuteAsync(
            Json(new { action = "delete", id = webhook.Id }),
            context);

        Assert.False(createdWebhook.IsError);
        Assert.False(createdSchedule.IsError);
        Assert.True(wrongSecret.IsError);
        Assert.False(fired.IsError);
        Assert.Contains("recorded output", fired.Data, StringComparison.Ordinal);
        Assert.False(listed.IsError);
        Assert.Contains(task.Id, listed.Data, StringComparison.Ordinal);
        Assert.False(deleted.IsError);
        Assert.Single(runtime.ListTriggers());

        var backgroundRun = Assert.Single(tasks.ListBackgroundRuns());
        Assert.Contains(backgroundRun.Output, line => line.Contains("payload line", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MonitorTool_FollowsTaskOutputLineByLine()
    {
        var tasks = new InMemoryAgentTaskRuntime();
        var task = tasks.CreateWorkItem("Stream output", owner: "subagent");
        var run = tasks.StartBackgroundRun(
            "Stream output",
            owner: "subagent",
            workItemId: task.Id,
            initialStatus: AgentBackgroundRunStatus.Running);
        var monitor = new MonitorTool(tasks);
        var progressMessages = new List<string>();
        var progress = new CollectingProgress(progressMessages);

        _ = Task.Run(async () =>
        {
            await Task.Delay(25);
            tasks.AppendBackgroundRunOutput(run.Id, "alpha\nbeta");
            await Task.Delay(25);
            tasks.AppendBackgroundRunOutput(run.Id, "gamma");
            tasks.StopBackgroundRun(run.Id, "done");
        });

        var result = await monitor.ExecuteAsync(
            Json(new
            {
                task_id = task.Id,
                follow = true,
                poll_ms = 5,
                timeout_ms = 1000,
            }),
            CreateContext(),
            progress);

        Assert.False(result.IsError);
        Assert.Contains("Monitor finished", result.Data, StringComparison.Ordinal);
        Assert.Contains("alpha", progressMessages, StringComparer.Ordinal);
        Assert.Contains("beta", progressMessages, StringComparer.Ordinal);
        Assert.Contains("gamma", progressMessages, StringComparer.Ordinal);
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

    private sealed class CollectingProgress(List<string> messages) : IProgress<ToolProgress>
    {
        public void Report(ToolProgress value)
        {
            if (!string.IsNullOrWhiteSpace(value.Message))
                messages.Add(value.Message);
        }
    }
}

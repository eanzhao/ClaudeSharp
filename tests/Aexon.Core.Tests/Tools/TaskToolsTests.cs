using System.Text.Json;
using Aexon.Core.Agents;
using Aexon.Core.Permissions;
using Aexon.Core.Tools;
using Aexon.Tools;

namespace Aexon.Core.Tests.Tools;

public sealed class TaskToolsTests
{
    [Fact]
    public async Task TaskTools_CanCreateListUpdateStopAndReadOutput()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var createTool = new TaskCreateTool(runtime);
        var getTool = new TaskGetTool(runtime);
        var updateTool = new TaskUpdateTool(runtime);
        var listTool = new TaskListTool(runtime);
        var stopTool = new TaskStopTool(runtime);
        var outputTool = new TaskOutputTool(runtime);
        var context = CreateContext();

        var created = await createTool.ExecuteAsync(
            Json(new
            {
                title = "Investigate background task",
                description = "Trace the runtime",
                owner = "subagent",
                status = "running",
            }),
            context);

        var task = Assert.Single(runtime.ListWorkItems());
        var backgroundRun = runtime.StartBackgroundRun(
            "Investigate background task",
            owner: "subagent",
            workItemId: task.Id,
            initialStatus: AgentBackgroundRunStatus.Running);
        runtime.AppendBackgroundRunOutput(backgroundRun.Id, "line 1");
        runtime.AppendBackgroundRunOutput(backgroundRun.Id, "line 2");
        var cancelled = false;
        runtime.RegisterBackgroundRunCancellation(backgroundRun.Id, () => cancelled = true);

        Assert.Equal(AgentWorkItemStatus.InProgress, runtime.GetWorkItem(task.Id)!.Status);

        var details = await getTool.ExecuteAsync(Json(new { id = task.Id }), context);
        var listing = await listTool.ExecuteAsync(Json(new { status = "running" }), context);
        var output = await outputTool.ExecuteAsync(Json(new { id = task.Id, offset = 1, limit = 1 }), context);
        var stopped = await stopTool.ExecuteAsync(Json(new { id = task.Id, reason = "done" }), context);

        Assert.False(created.IsError);
        Assert.Contains("Created task", created.Data, StringComparison.Ordinal);
        Assert.False(details.IsError);
        Assert.Contains($"Task: {task.Id}", details.Data, StringComparison.Ordinal);
        Assert.Contains("Status: running", details.Data, StringComparison.Ordinal);
        Assert.False(listing.IsError);
        Assert.Contains(task.Id, listing.Data, StringComparison.Ordinal);
        Assert.False(output.IsError);
        Assert.Contains("Selected run:", output.Data, StringComparison.Ordinal);
        Assert.DoesNotContain("line 1", output.Data, StringComparison.Ordinal);
        Assert.Contains("line 2", output.Data, StringComparison.Ordinal);
        Assert.False(stopped.IsError);
        Assert.Contains(backgroundRun.Id, stopped.Data, StringComparison.Ordinal);
        Assert.True(cancelled);
        Assert.Equal(AgentWorkItemStatus.Cancelled, runtime.GetWorkItem(task.Id)!.Status);

        var updated = await updateTool.ExecuteAsync(
            Json(new
            {
                id = task.Id,
                status = "failed",
                description = "Updated description",
            }),
            context);

        Assert.False(updated.IsError);
        Assert.Equal(AgentWorkItemStatus.Blocked, runtime.GetWorkItem(task.Id)!.Status);
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

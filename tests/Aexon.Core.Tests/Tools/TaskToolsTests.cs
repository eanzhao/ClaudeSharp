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

    [Fact]
    public async Task TaskCreateTool_ValidateInputAsync_RejectsMissingTitleAndInvalidStatus()
    {
        var tool = new TaskCreateTool(new InMemoryAgentTaskRuntime());
        var context = CreateContext();

        var missingTitle = await tool.ValidateInputAsync(Json(new { }), context);
        var invalidStatus = await tool.ValidateInputAsync(
            Json(new
            {
                title = "Review logs",
                status = "paused",
            }),
            context);

        Assert.False(missingTitle.IsValid);
        Assert.Equal("title is required.", missingTitle.Message);
        Assert.False(invalidStatus.IsValid);
        Assert.Equal("status must be pending, running, completed, failed, or cancelled.", invalidStatus.Message);
    }

    [Fact]
    public async Task TaskListTool_SupportsPaginationAndFilters()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var tool = new TaskListTool(runtime);
        var context = CreateContext();

        var first = runtime.CreateWorkItem("First task", owner: "alpha");
        await Task.Delay(20);
        var second = runtime.CreateWorkItem("Second task", owner: "beta");
        runtime.UpdateWorkItem(second.Id, item => item.Status = AgentWorkItemStatus.Completed);
        await Task.Delay(20);
        var third = runtime.CreateWorkItem("Third task", owner: "alpha");
        runtime.UpdateWorkItem(third.Id, item => item.Status = AgentWorkItemStatus.Blocked);

        var paged = await tool.ExecuteAsync(
            Json(new { offset = 1, limit = 1 }),
            context);
        var completedOnly = await tool.ExecuteAsync(
            Json(new { status = "completed" }),
            context);
        var alphaOnly = await tool.ExecuteAsync(
            Json(new { owner = "ALPHA" }),
            context);

        Assert.False(paged.IsError);
        Assert.Contains("Showing tasks 2-2 of 3.", paged.Data, StringComparison.Ordinal);
        Assert.Contains(second.Id, paged.Data, StringComparison.Ordinal);
        Assert.DoesNotContain(first.Id, paged.Data, StringComparison.Ordinal);
        Assert.DoesNotContain(third.Id, paged.Data, StringComparison.Ordinal);

        Assert.False(completedOnly.IsError);
        Assert.Contains(second.Id, completedOnly.Data, StringComparison.Ordinal);
        Assert.DoesNotContain(first.Id, completedOnly.Data, StringComparison.Ordinal);
        Assert.DoesNotContain(third.Id, completedOnly.Data, StringComparison.Ordinal);

        Assert.False(alphaOnly.IsError);
        Assert.Contains(first.Id, alphaOnly.Data, StringComparison.Ordinal);
        Assert.Contains(third.Id, alphaOnly.Data, StringComparison.Ordinal);
        Assert.DoesNotContain(second.Id, alphaOnly.Data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TaskListTool_EmptyState_ShowsNoMatches()
    {
        var tool = new TaskListTool(new InMemoryAgentTaskRuntime());

        var result = await tool.ExecuteAsync(
            Json(new { owner = "nobody" }),
            CreateContext());

        Assert.False(result.IsError);
        Assert.Contains("No tasks matched the requested filters.", result.Data, StringComparison.Ordinal);
        Assert.Contains("(none)", result.Data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TaskGetTool_ValidateInputAsync_RejectsBlankId()
    {
        var tool = new TaskGetTool(new InMemoryAgentTaskRuntime());

        var validation = await tool.ValidateInputAsync(
            Json(new { id = "   " }),
            CreateContext());

        Assert.False(validation.IsValid);
        Assert.Equal("id is required.", validation.Message);
    }

    [Fact]
    public async Task TaskGetTool_ExecuteAsync_UnknownId_ReturnsError()
    {
        var tool = new TaskGetTool(new InMemoryAgentTaskRuntime());

        var result = await tool.ExecuteAsync(
            Json(new { id = "work-item-404" }),
            CreateContext());

        Assert.True(result.IsError);
        Assert.Contains("No task matched id 'work-item-404'.", result.Data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TaskUpdateTool_UnknownId_ReturnsError()
    {
        var tool = new TaskUpdateTool(new InMemoryAgentTaskRuntime());

        var result = await tool.ExecuteAsync(
            Json(new
            {
                id = "work-item-404",
                title = "Updated",
            }),
            CreateContext());

        Assert.True(result.IsError);
        Assert.Contains("No task matched id 'work-item-404'.", result.Data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TaskUpdateTool_CanChangeOwnerDescriptionAndTitle()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var tool = new TaskUpdateTool(runtime);
        var task = runtime.CreateWorkItem("Old title", description: "Old description", owner: "old-owner");

        var result = await tool.ExecuteAsync(
            Json(new
            {
                id = task.Id,
                title = "New title",
                description = "New description",
                owner = "new-owner",
            }),
            CreateContext());

        var updated = runtime.GetWorkItem(task.Id);

        Assert.False(result.IsError);
        Assert.NotNull(updated);
        Assert.Equal("New title", updated!.Title);
        Assert.Equal("New description", updated.Description);
        Assert.Equal("new-owner", updated.Owner);
        Assert.Contains("Updated task", result.Data, StringComparison.Ordinal);
        Assert.Contains("Title: New title", result.Data, StringComparison.Ordinal);
        Assert.Contains("Description: New description", result.Data, StringComparison.Ordinal);
        Assert.Contains("Owner: new-owner", result.Data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TaskStopTool_WithoutActiveBackgroundRun_ReturnsCancellationMessage()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var tool = new TaskStopTool(runtime);
        var task = runtime.CreateWorkItem("Stop me");

        var result = await tool.ExecuteAsync(
            Json(new { id = task.Id, reason = "done" }),
            CreateContext());

        Assert.False(result.IsError);
        Assert.Contains("did not have an active background run", result.Data, StringComparison.Ordinal);
        Assert.Equal(AgentWorkItemStatus.Cancelled, runtime.GetWorkItem(task.Id)!.Status);
    }

    [Fact]
    public async Task TaskOutputTool_ValidateInputAsync_RejectsInvalidOffsetAndLimit()
    {
        var tool = new TaskOutputTool(new InMemoryAgentTaskRuntime());
        var context = CreateContext();

        var invalidOffset = await tool.ValidateInputAsync(
            Json(new { id = "work-item-1", offset = -1 }),
            context);
        var invalidLimit = await tool.ValidateInputAsync(
            Json(new { id = "work-item-1", limit = 0 }),
            context);

        Assert.False(invalidOffset.IsValid);
        Assert.Equal("offset must be 0 or greater.", invalidOffset.Message);
        Assert.False(invalidLimit.IsValid);
        Assert.Equal("limit must be greater than 0.", invalidLimit.Message);
    }

    [Fact]
    public async Task TaskOutputTool_UsesLatestRunWhenRunIdIsOmitted()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var tool = new TaskOutputTool(runtime);
        var task = runtime.CreateWorkItem("Read output");
        var firstRun = runtime.StartBackgroundRun(
            "first run",
            workItemId: task.Id,
            initialStatus: AgentBackgroundRunStatus.Running);
        runtime.AppendBackgroundRunOutput(firstRun.Id, "first output");
        runtime.StopBackgroundRun(firstRun.Id, "done");
        await Task.Delay(20);
        var secondRun = runtime.StartBackgroundRun(
            "second run",
            workItemId: task.Id,
            initialStatus: AgentBackgroundRunStatus.Running);
        runtime.AppendBackgroundRunOutput(secondRun.Id, "second output");
        runtime.StopBackgroundRun(secondRun.Id, "done");

        var result = await tool.ExecuteAsync(
            Json(new { id = task.Id }),
            CreateContext());

        Assert.False(result.IsError);
        Assert.Contains($"Selected run: {secondRun.Id} (latest run)", result.Data, StringComparison.Ordinal);
        Assert.Contains("second output", result.Data, StringComparison.Ordinal);
        Assert.DoesNotContain("first output", result.Data, StringComparison.Ordinal);
    }

    [Fact]
    public void TaskTools_ReadOnlyAndConcurrencyFlags_MatchOperationType()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        ITool createTool = new TaskCreateTool(runtime);
        ITool getTool = new TaskGetTool(runtime);
        ITool updateTool = new TaskUpdateTool(runtime);
        ITool listTool = new TaskListTool(runtime);
        ITool stopTool = new TaskStopTool(runtime);
        ITool outputTool = new TaskOutputTool(runtime);

        Assert.False(createTool.IsReadOnly(Json(new { title = "Task" })));
        Assert.False(createTool.IsConcurrencySafe(Json(new { title = "Task" })));
        Assert.True(getTool.IsReadOnly(Json(new { id = "work-item-1" })));
        Assert.True(getTool.IsConcurrencySafe(Json(new { id = "work-item-1" })));
        Assert.False(updateTool.IsReadOnly(Json(new { id = "work-item-1" })));
        Assert.False(updateTool.IsConcurrencySafe(Json(new { id = "work-item-1" })));
        Assert.True(listTool.IsReadOnly(Json(new { })));
        Assert.True(listTool.IsConcurrencySafe(Json(new { })));
        Assert.False(stopTool.IsReadOnly(Json(new { id = "work-item-1" })));
        Assert.False(stopTool.IsConcurrencySafe(Json(new { id = "work-item-1" })));
        Assert.True(outputTool.IsReadOnly(Json(new { id = "work-item-1" })));
        Assert.True(outputTool.IsConcurrencySafe(Json(new { id = "work-item-1" })));
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

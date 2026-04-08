using System.Text.Json;
using ClaudeSharp.Core.Agents;
using ClaudeSharp.Core.Permissions;
using ClaudeSharp.Core.Tools;
using ClaudeSharp.Tools;

namespace ClaudeSharp.Core.Tests.Tools;

/// <summary>
/// Contains tests for agent runtime tools.
/// </summary>
public sealed class AgentRuntimeToolsTests
{
    [Fact]
    public async Task AgentStatusTool_ValidateInput_RejectsInvalidArguments()
    {
        var tool = new AgentStatusTool(new InMemoryAgentTaskRuntime());
        var context = CreateContext();

        var invalidView = await tool.ValidateInputAsync(Json(new { view = "detail" }), context);
        var invalidKind = await tool.ValidateInputAsync(Json(new { kind = "queues" }), context);
        var invalidOffset = await tool.ValidateInputAsync(Json(new { offset = -1 }), context);
        var invalidLimit = await tool.ValidateInputAsync(Json(new { limit = 0 }), context);
        var invalidRecent = await tool.ValidateInputAsync(Json(new { recent_limit = 0 }), context);
        var invalidOutputOffset = await tool.ValidateInputAsync(Json(new { output_offset = -1 }), context);
        var invalidOutputLimit = await tool.ValidateInputAsync(Json(new { output_limit = 0 }), context);

        Assert.Equal("view must be overview or summary.", invalidView.Message);
        Assert.Equal("kind must be all, work_items, or background_runs.", invalidKind.Message);
        Assert.Equal("offset must be 0 or greater.", invalidOffset.Message);
        Assert.Equal("limit must be greater than 0.", invalidLimit.Message);
        Assert.Equal("recent_limit must be greater than 0.", invalidRecent.Message);
        Assert.Equal("output_offset must be 0 or greater.", invalidOutputOffset.Message);
        Assert.Equal("output_limit must be greater than 0.", invalidOutputLimit.Message);
    }

    [Fact]
    public async Task AgentStatusTool_ExecuteAsync_RendersOverviewSummaryAndDetails()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var completedItem = runtime.CreateWorkItem("Done", owner: "agent-a");
        runtime.UpdateWorkItem(completedItem.Id, item => item.Status = AgentWorkItemStatus.Completed);
        var blockedItem = runtime.CreateWorkItem("Blocked", owner: "agent-a");
        runtime.UpdateWorkItem(blockedItem.Id, item => item.Status = AgentWorkItemStatus.Blocked);

        var queuedRun = runtime.StartBackgroundRun(
            "Queued run",
            owner: "agent-a",
            workItemId: blockedItem.Id,
            initialStatus: AgentBackgroundRunStatus.Queued);
        var stoppedRun = runtime.StartBackgroundRun(
            "Stopped run",
            owner: "agent-a",
            workItemId: completedItem.Id,
            initialStatus: AgentBackgroundRunStatus.Stopped);
        runtime.AppendBackgroundRunOutput(stoppedRun.Id, "line 1");
        runtime.AppendBackgroundRunOutput(stoppedRun.Id, "line 2");

        var tool = new AgentStatusTool(runtime);

        var overview = await tool.ExecuteAsync(
            Json(new
            {
                kind = "background_runs",
                status = "queued",
                owner = "agent-a",
                offset = 0,
                limit = 1,
            }),
            CreateContext());
        var summary = await tool.ExecuteAsync(
            Json(new
            {
                view = "summary",
                owner = "agent-a",
                recent_limit = 2,
            }),
            CreateContext());
        var details = await tool.ExecuteAsync(
            Json(new
            {
                id = stoppedRun.Id,
                include_output = true,
                output_offset = 1,
                output_limit = 1,
            }),
            CreateContext());
        var missing = await tool.ExecuteAsync(
            Json(new { id = "missing-run" }),
            CreateContext());

        Assert.False(overview.IsError);
        Assert.Contains("Background runs:", overview.Data, StringComparison.Ordinal);
        Assert.Contains(queuedRun.Id, overview.Data, StringComparison.Ordinal);
        Assert.False(summary.IsError);
        Assert.Contains("Agent summary (owner: agent-a):", summary.Data, StringComparison.Ordinal);
        Assert.Contains("Background runs: 2", summary.Data, StringComparison.Ordinal);
        Assert.False(details.IsError);
        Assert.Contains($"Background run: {stoppedRun.Id}", details.Data, StringComparison.Ordinal);
        Assert.Contains("Showing output entries 2-2 of 2.", details.Data, StringComparison.Ordinal);
        Assert.DoesNotContain("line 1", details.Data, StringComparison.Ordinal);
        Assert.Contains("line 2", details.Data, StringComparison.Ordinal);
        Assert.True(missing.IsError);
        Assert.Contains("No agent work item or background run matched", missing.Data, StringComparison.Ordinal);
        Assert.True(tool.IsReadOnly(default));
        Assert.True(tool.IsConcurrencySafe(default));
        Assert.Equal("Agent status", tool.GetUserFacingName());
        Assert.Equal("Checking subagent status", tool.GetActivityDescription(null));
    }

    [Fact]
    public async Task AgentWaitTool_ValidateAndExecute_CoverSuccessTimeoutAndMissingRuns()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var completedRun = runtime.StartBackgroundRun(
            "Completed",
            initialStatus: AgentBackgroundRunStatus.Stopped);
        runtime.AppendBackgroundRunOutput(completedRun.Id, "done");
        var pendingRun = runtime.StartBackgroundRun(
            "Pending",
            initialStatus: AgentBackgroundRunStatus.Running);

        var tool = new AgentWaitTool(runtime);
        var context = CreateContext();

        var invalidId = await tool.ValidateInputAsync(Json(new { id = "" }), context);
        var invalidPoll = await tool.ValidateInputAsync(Json(new { id = completedRun.Id, poll_ms = 0 }), context);
        var invalidTimeout = await tool.ValidateInputAsync(Json(new { id = completedRun.Id, timeout_ms = 0 }), context);
        var success = await tool.ExecuteAsync(
            Json(new { id = completedRun.Id, include_output = true }),
            context);
        var timedOut = await tool.ExecuteAsync(
            Json(new { id = pendingRun.Id, poll_ms = 1, timeout_ms = 5 }),
            context);
        var missing = await tool.ExecuteAsync(
            Json(new { id = "missing-run" }),
            context);

        Assert.Equal("id or ids is required.", invalidId.Message);
        Assert.Equal("poll_ms must be greater than 0.", invalidPoll.Message);
        Assert.Equal("timeout_ms must be greater than 0.", invalidTimeout.Message);
        Assert.False(success.IsError);
        Assert.Contains("Wait finished after", success.Data, StringComparison.Ordinal);
        Assert.Contains("All 1 background run(s) reached terminal states.", success.Data, StringComparison.Ordinal);
        Assert.Contains("Background run:", success.Data, StringComparison.Ordinal);
        Assert.Contains("done", success.Data, StringComparison.Ordinal);
        Assert.True(timedOut.IsError);
        Assert.Contains("Timed out after", timedOut.Data, StringComparison.Ordinal);
        Assert.Contains("all requested runs", timedOut.Data, StringComparison.Ordinal);
        Assert.True(missing.IsError);
        Assert.Contains("No background run matched id", missing.Data, StringComparison.Ordinal);
        var permission = await tool.CheckPermissionsAsync(Json(new { id = completedRun.Id }), context);
        Assert.Equal(PermissionBehavior.Allow, permission.Behavior);
        Assert.True(tool.IsReadOnly(default));
        Assert.True(tool.IsConcurrencySafe(default));
        Assert.Equal("Wait for subagent", tool.GetUserFacingName());
        Assert.Equal("Waiting for subagent", tool.GetActivityDescription(null));
    }

    [Fact]
    public async Task AgentStopTool_ValidateAndExecute_CoverRequestedAndErrorStates()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var running = runtime.StartBackgroundRun(
            "Running",
            initialStatus: AgentBackgroundRunStatus.Running);
        var completed = runtime.StartBackgroundRun(
            "Done",
            initialStatus: AgentBackgroundRunStatus.Stopped);
        var cancelInvoked = false;
        runtime.RegisterBackgroundRunCancellation(running.Id, () => cancelInvoked = true);

        var tool = new AgentStopTool(runtime);
        var context = CreateContext();

        var invalid = await tool.ValidateInputAsync(Json(new { id = "" }), context);
        var requested = await tool.ExecuteAsync(
            Json(new { id = running.Id, reason = "done" }),
            context);
        var alreadyRequested = await tool.ExecuteAsync(
            Json(new { id = running.Id }),
            context);
        var alreadyCompleted = await tool.ExecuteAsync(
            Json(new { id = completed.Id }),
            context);
        var missing = await tool.ExecuteAsync(
            Json(new { id = "missing-run" }),
            context);

        Assert.Equal("id is required.", invalid.Message);
        Assert.False(requested.IsError);
        Assert.Contains("Cancellation requested", requested.Data, StringComparison.Ordinal);
        Assert.True(cancelInvoked);
        Assert.False(alreadyRequested.IsError);
        Assert.Contains("already requested", alreadyRequested.Data, StringComparison.OrdinalIgnoreCase);
        Assert.True(alreadyCompleted.IsError);
        Assert.Contains("already finished", alreadyCompleted.Data, StringComparison.Ordinal);
        Assert.True(missing.IsError);
        Assert.Contains("No background run matched id", missing.Data, StringComparison.Ordinal);
        var permission = await tool.CheckPermissionsAsync(Json(new { id = running.Id }), context);
        Assert.Equal(PermissionBehavior.Allow, permission.Behavior);
        Assert.True(tool.IsConcurrencySafe(default));
        Assert.Equal("Stop subagent", tool.GetUserFacingName());
        Assert.Equal("Stopping subagent", tool.GetActivityDescription(null));
        Assert.Contains(
            runtime.GetBackgroundRun(running.Id)!.Output,
            line => line.Contains("[status] Cancellation requested", StringComparison.Ordinal));
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

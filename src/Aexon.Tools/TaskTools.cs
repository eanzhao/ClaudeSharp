using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aexon.Core.Agents;
using Aexon.Core.Tools;

namespace Aexon.Tools;

public sealed class TaskCreateToolInput
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("owner")]
    public string? Owner { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }
}

public sealed class TaskGetToolInput
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }
}

public sealed class TaskUpdateToolInput
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("owner")]
    public string? Owner { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }
}

public sealed class TaskListToolInput
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("owner")]
    public string? Owner { get; set; }

    [JsonPropertyName("offset")]
    public int? Offset { get; set; }

    [JsonPropertyName("limit")]
    public int? Limit { get; set; }
}

public sealed class TaskStopToolInput
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}

public sealed class TaskOutputToolInput
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("run_id")]
    public string? RunId { get; set; }

    [JsonPropertyName("offset")]
    public int? Offset { get; set; }

    [JsonPropertyName("limit")]
    public int? Limit { get; set; }
}

public sealed class TaskCreateTool : ITool
{
    private readonly IAgentTaskRuntime _runtime;

    public TaskCreateTool(IAgentTaskRuntime runtime)
    {
        _runtime = runtime;
    }

    public string Name => "TaskCreate";

    public string[] Aliases => ["TaskCreateTool"];

    public Task<string> GetDescriptionAsync() =>
        Task.FromResult("Create a task entry backed by the agent work-item runtime.");

    public JsonElement GetInputSchema() => JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "title": { "type": "string" },
            "description": { "type": "string" },
            "owner": { "type": "string" },
            "status": { "type": "string", "enum": ["pending", "running", "completed", "failed", "cancelled"] }
          },
          "required": ["title"],
          "additionalProperties": false
        }
        """).RootElement.Clone();

    public Task<string> GetPromptAsync(ToolPromptContext context) =>
        Task.FromResult("Create a task when you need an explicit model-visible task record in the shared agent runtime.");

    public Task<ValidationResult> ValidateInputAsync(JsonElement input, ToolExecutionContext context)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<TaskCreateToolInput>(input);
            if (parsed == null || string.IsNullOrWhiteSpace(parsed.Title))
                return Task.FromResult(ValidationResult.Invalid("title is required."));
            if (!TaskToolSupport.TryParseStatus(parsed.Status, out _, out var error))
                return Task.FromResult(ValidationResult.Invalid(error!));
            return Task.FromResult(ValidationResult.Valid());
        }
        catch (JsonException ex)
        {
            return Task.FromResult(ValidationResult.Invalid(ex.Message));
        }
    }

    public Task<ToolResult> ExecuteAsync(
        JsonElement input,
        ToolExecutionContext context,
        IProgress<ToolProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var parsed = JsonSerializer.Deserialize<TaskCreateToolInput>(input);
        if (parsed == null || string.IsNullOrWhiteSpace(parsed.Title))
            return Task.FromResult(ToolResult.Error("title is required."));
        if (!TaskToolSupport.TryParseStatus(parsed.Status, out var status, out var error))
            return Task.FromResult(ToolResult.Error(error!));

        var item = _runtime.CreateWorkItem(
            parsed.Title.Trim(),
            description: parsed.Description,
            owner: parsed.Owner);
        _runtime.UpdateWorkItem(item.Id, workItem =>
            workItem.Status = AgentTaskViewProjector.ToWorkItemStatus(status));

        return Task.FromResult(ToolResult.Success(
            TaskToolSupport.FormatTaskDetails(
                _runtime,
                _runtime.GetWorkItem(item.Id)!,
                header: "Created task")));
    }

    public string GetUserFacingName(JsonElement? input = null) => "Task create";

    public string? GetActivityDescription(JsonElement? input) => "Creating task";
}

public sealed class TaskGetTool : ITool
{
    private readonly IAgentTaskRuntime _runtime;

    public TaskGetTool(IAgentTaskRuntime runtime)
    {
        _runtime = runtime;
    }

    public string Name => "TaskGet";

    public string[] Aliases => ["TaskGetTool"];

    public Task<string> GetDescriptionAsync() =>
        Task.FromResult("Read task details, projected status, and related background runs.");

    public JsonElement GetInputSchema() => JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "id": { "type": "string" }
          },
          "required": ["id"],
          "additionalProperties": false
        }
        """).RootElement.Clone();

    public Task<string> GetPromptAsync(ToolPromptContext context) =>
        Task.FromResult("Use TaskGet to inspect a task id from TaskCreate, Agent, or TaskList.");

    public Task<ValidationResult> ValidateInputAsync(JsonElement input, ToolExecutionContext context)
    {
        var parsed = JsonSerializer.Deserialize<TaskGetToolInput>(input);
        return Task.FromResult(string.IsNullOrWhiteSpace(parsed?.Id)
            ? ValidationResult.Invalid("id is required.")
            : ValidationResult.Valid());
    }

    public bool IsReadOnly(JsonElement input) => true;

    public bool IsConcurrencySafe(JsonElement input) => true;

    public Task<ToolResult> ExecuteAsync(
        JsonElement input,
        ToolExecutionContext context,
        IProgress<ToolProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var parsed = JsonSerializer.Deserialize<TaskGetToolInput>(input);
        if (string.IsNullOrWhiteSpace(parsed?.Id))
            return Task.FromResult(ToolResult.Error("id is required."));

        var task = _runtime.GetWorkItem(parsed.Id.Trim());
        return Task.FromResult(task == null
            ? ToolResult.Error($"No task matched id '{parsed.Id.Trim()}'.")
            : ToolResult.Success(TaskToolSupport.FormatTaskDetails(_runtime, task)));
    }

    public string GetUserFacingName(JsonElement? input = null) => "Task get";

    public string? GetActivityDescription(JsonElement? input) => "Reading task";
}

public sealed class TaskUpdateTool : ITool
{
    private readonly IAgentTaskRuntime _runtime;

    public TaskUpdateTool(IAgentTaskRuntime runtime)
    {
        _runtime = runtime;
    }

    public string Name => "TaskUpdate";

    public string[] Aliases => ["TaskUpdateTool"];

    public Task<string> GetDescriptionAsync() =>
        Task.FromResult("Update task metadata or projected task status.");

    public JsonElement GetInputSchema() => JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "id": { "type": "string" },
            "title": { "type": "string" },
            "description": { "type": "string" },
            "owner": { "type": "string" },
            "status": { "type": "string", "enum": ["pending", "running", "completed", "failed", "cancelled"] }
          },
          "required": ["id"],
          "additionalProperties": false
        }
        """).RootElement.Clone();

    public Task<string> GetPromptAsync(ToolPromptContext context) =>
        Task.FromResult("Use TaskUpdate to change task title, description, owner, or status.");

    public Task<ValidationResult> ValidateInputAsync(JsonElement input, ToolExecutionContext context)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<TaskUpdateToolInput>(input);
            if (string.IsNullOrWhiteSpace(parsed?.Id))
                return Task.FromResult(ValidationResult.Invalid("id is required."));
            if (!TaskToolSupport.TryParseStatus(parsed.Status, out _, out var error))
                return Task.FromResult(ValidationResult.Invalid(error!));
            return Task.FromResult(ValidationResult.Valid());
        }
        catch (JsonException ex)
        {
            return Task.FromResult(ValidationResult.Invalid(ex.Message));
        }
    }

    public Task<ToolResult> ExecuteAsync(
        JsonElement input,
        ToolExecutionContext context,
        IProgress<ToolProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var parsed = JsonSerializer.Deserialize<TaskUpdateToolInput>(input);
        if (string.IsNullOrWhiteSpace(parsed?.Id))
            return Task.FromResult(ToolResult.Error("id is required."));
        if (!TaskToolSupport.TryParseStatus(parsed.Status, out var status, out var error))
            return Task.FromResult(ToolResult.Error(error!));

        var updated = _runtime.UpdateWorkItem(parsed.Id.Trim(), workItem =>
        {
            if (!string.IsNullOrWhiteSpace(parsed.Title))
                workItem.Title = parsed.Title.Trim();
            if (parsed.Description != null)
                workItem.Description = string.IsNullOrWhiteSpace(parsed.Description) ? null : parsed.Description.Trim();
            if (parsed.Owner != null)
                workItem.Owner = string.IsNullOrWhiteSpace(parsed.Owner) ? null : parsed.Owner.Trim();
            if (!string.IsNullOrWhiteSpace(parsed.Status))
                workItem.Status = AgentTaskViewProjector.ToWorkItemStatus(status);
        });

        if (!updated)
            return Task.FromResult(ToolResult.Error($"No task matched id '{parsed.Id.Trim()}'."));

        return Task.FromResult(ToolResult.Success(
            TaskToolSupport.FormatTaskDetails(
                _runtime,
                _runtime.GetWorkItem(parsed.Id.Trim())!,
                header: "Updated task")));
    }

    public string GetUserFacingName(JsonElement? input = null) => "Task update";

    public string? GetActivityDescription(JsonElement? input) => "Updating task";
}

public sealed class TaskListTool : ITool
{
    private readonly IAgentTaskRuntime _runtime;

    public TaskListTool(IAgentTaskRuntime runtime)
    {
        _runtime = runtime;
    }

    public string Name => "TaskList";

    public string[] Aliases => ["TaskListTool"];

    public Task<string> GetDescriptionAsync() =>
        Task.FromResult("List tasks with projected pending/running/completed/failed/cancelled states.");

    public JsonElement GetInputSchema() => JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "status": { "type": "string", "enum": ["pending", "running", "completed", "failed", "cancelled"] },
            "owner": { "type": "string" },
            "offset": { "type": "integer" },
            "limit": { "type": "integer" }
          },
          "additionalProperties": false
        }
        """).RootElement.Clone();

    public Task<string> GetPromptAsync(ToolPromptContext context) =>
        Task.FromResult("Use TaskList to discover task ids before calling TaskGet, TaskStop, TaskOutput, or RemoteTrigger.");

    public Task<ValidationResult> ValidateInputAsync(JsonElement input, ToolExecutionContext context)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<TaskListToolInput>(input) ?? new TaskListToolInput();
            if (!TaskToolSupport.TryParseStatus(parsed.Status, out _, out var error))
                return Task.FromResult(ValidationResult.Invalid(error!));
            if (parsed.Offset is < 0)
                return Task.FromResult(ValidationResult.Invalid("offset must be 0 or greater."));
            if (parsed.Limit is <= 0)
                return Task.FromResult(ValidationResult.Invalid("limit must be greater than 0."));
            return Task.FromResult(ValidationResult.Valid());
        }
        catch (JsonException ex)
        {
            return Task.FromResult(ValidationResult.Invalid(ex.Message));
        }
    }

    public bool IsReadOnly(JsonElement input) => true;

    public bool IsConcurrencySafe(JsonElement input) => true;

    public Task<ToolResult> ExecuteAsync(
        JsonElement input,
        ToolExecutionContext context,
        IProgress<ToolProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var parsed = JsonSerializer.Deserialize<TaskListToolInput>(input) ?? new TaskListToolInput();
        if (!TaskToolSupport.TryParseStatus(parsed.Status, out var status, out var error))
            return Task.FromResult(ToolResult.Error(error!));

        var items = _runtime.ListWorkItems()
            .Select(task => AgentTaskViewProjector.Project(_runtime, task))
            .Where(task =>
                string.IsNullOrWhiteSpace(parsed.Owner) ||
                string.Equals(task.WorkItem.Owner, parsed.Owner.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(task =>
                string.IsNullOrWhiteSpace(parsed.Status) ||
                task.Status == status)
            .OrderByDescending(task => task.WorkItem.UpdatedAt)
            .ThenBy(task => task.WorkItem.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var page = items
            .Skip(Math.Max(0, parsed.Offset ?? 0))
            .Take(parsed.Limit.HasValue ? Math.Max(1, parsed.Limit.Value) : int.MaxValue)
            .ToArray();

        var builder = new StringBuilder();
        builder.AppendLine("Tasks:");
        builder.AppendLine(TaskToolSupport.FormatPaginationMessage("task", items.Length, parsed.Offset ?? 0, page.Length));
        if (page.Length == 0)
        {
            builder.AppendLine("(none)");
        }
        else
        {
            foreach (var task in page)
            {
                var runNote = task.ActiveRun != null
                    ? $" active_run={task.ActiveRun.Id}"
                    : task.LatestRun != null
                        ? $" latest_run={task.LatestRun.Id}"
                        : string.Empty;
                builder.AppendLine(
                    $"- {task.WorkItem.Id} [{AgentTaskViewProjector.FormatStatus(task.Status)}] {task.WorkItem.Title}{runNote}");
            }
        }

        return Task.FromResult(ToolResult.Success(builder.ToString().TrimEnd()));
    }

    public string GetUserFacingName(JsonElement? input = null) => "Task list";

    public string? GetActivityDescription(JsonElement? input) => "Listing tasks";
}

public sealed class TaskStopTool : ITool
{
    private readonly IAgentTaskRuntime _runtime;

    public TaskStopTool(IAgentTaskRuntime runtime)
    {
        _runtime = runtime;
    }

    public string Name => "TaskStop";

    public string[] Aliases => ["TaskStopTool"];

    public Task<string> GetDescriptionAsync() =>
        Task.FromResult("Stop a running task by cancelling any active background runs and marking the task cancelled.");

    public JsonElement GetInputSchema() => JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "id": { "type": "string" },
            "reason": { "type": "string" }
          },
          "required": ["id"],
          "additionalProperties": false
        }
        """).RootElement.Clone();

    public Task<string> GetPromptAsync(ToolPromptContext context) =>
        Task.FromResult("Use TaskStop when a task is still running or queued and should be cancelled.");

    public Task<ValidationResult> ValidateInputAsync(JsonElement input, ToolExecutionContext context)
    {
        var parsed = JsonSerializer.Deserialize<TaskStopToolInput>(input);
        return Task.FromResult(string.IsNullOrWhiteSpace(parsed?.Id)
            ? ValidationResult.Invalid("id is required.")
            : ValidationResult.Valid());
    }

    public Task<ToolResult> ExecuteAsync(
        JsonElement input,
        ToolExecutionContext context,
        IProgress<ToolProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var parsed = JsonSerializer.Deserialize<TaskStopToolInput>(input);
        if (string.IsNullOrWhiteSpace(parsed?.Id))
            return Task.FromResult(ToolResult.Error("id is required."));

        var task = _runtime.GetWorkItem(parsed.Id.Trim());
        if (task == null)
            return Task.FromResult(ToolResult.Error($"No task matched id '{parsed.Id.Trim()}'."));

        var activeRuns = _runtime.ListBackgroundRuns()
            .Where(run =>
                string.Equals(run.WorkItemId, task.Id, StringComparison.OrdinalIgnoreCase) &&
                !AgentBackgroundRunWaiter.IsTerminal(run.Status))
            .OrderBy(run => run.StartedAt)
            .ToArray();
        var lines = new List<string>();

        if (activeRuns.Length == 0)
        {
            _runtime.UpdateWorkItem(task.Id, item => item.Status = AgentWorkItemStatus.Cancelled);
            lines.Add($"Task '{task.Id}' did not have an active background run. Marked it as cancelled.");
            return Task.FromResult(ToolResult.Success(string.Join(Environment.NewLine, lines)));
        }

        var anyRequested = false;
        foreach (var run in activeRuns)
        {
            var result = _runtime.RequestBackgroundRunCancellation(run.Id, parsed.Reason ?? "task stopped");
            lines.Add($"- {run.Id}: {result}");
            anyRequested |= result is AgentBackgroundRunCancellationResult.Requested or
                AgentBackgroundRunCancellationResult.AlreadyRequested;
        }

        if (anyRequested)
            _runtime.UpdateWorkItem(task.Id, item => item.Status = AgentWorkItemStatus.Cancelled);
        lines.Insert(0, $"Stop requested for task '{task.Id}'.");
        return Task.FromResult(anyRequested
            ? ToolResult.Success(string.Join(Environment.NewLine, lines))
            : ToolResult.Error(string.Join(Environment.NewLine, lines)));
    }

    public string GetUserFacingName(JsonElement? input = null) => "Task stop";

    public string? GetActivityDescription(JsonElement? input) => "Stopping task";
}

public sealed class TaskOutputTool : ITool
{
    private readonly IAgentTaskRuntime _runtime;

    public TaskOutputTool(IAgentTaskRuntime runtime)
    {
        _runtime = runtime;
    }

    public string Name => "TaskOutput";

    public string[] Aliases => ["TaskOutputTool"];

    public Task<string> GetDescriptionAsync() =>
        Task.FromResult("Read output captured for the latest or explicitly selected background run of a task.");

    public JsonElement GetInputSchema() => JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "id": { "type": "string", "description": "Task id" },
            "run_id": { "type": "string", "description": "Optional specific background run id" },
            "offset": { "type": "integer" },
            "limit": { "type": "integer" }
          },
          "required": ["id"],
          "additionalProperties": false
        }
        """).RootElement.Clone();

    public Task<string> GetPromptAsync(ToolPromptContext context) =>
        Task.FromResult("Use TaskOutput to fetch only the output window you need from a task's selected background run.");

    public Task<ValidationResult> ValidateInputAsync(JsonElement input, ToolExecutionContext context)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<TaskOutputToolInput>(input);
            if (string.IsNullOrWhiteSpace(parsed?.Id))
                return Task.FromResult(ValidationResult.Invalid("id is required."));
            if (parsed.Offset is < 0)
                return Task.FromResult(ValidationResult.Invalid("offset must be 0 or greater."));
            if (parsed.Limit is <= 0)
                return Task.FromResult(ValidationResult.Invalid("limit must be greater than 0."));
            return Task.FromResult(ValidationResult.Valid());
        }
        catch (JsonException ex)
        {
            return Task.FromResult(ValidationResult.Invalid(ex.Message));
        }
    }

    public bool IsReadOnly(JsonElement input) => true;

    public bool IsConcurrencySafe(JsonElement input) => true;

    public Task<ToolResult> ExecuteAsync(
        JsonElement input,
        ToolExecutionContext context,
        IProgress<ToolProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var parsed = JsonSerializer.Deserialize<TaskOutputToolInput>(input);
        if (string.IsNullOrWhiteSpace(parsed?.Id))
            return Task.FromResult(ToolResult.Error("id is required."));

        return Task.FromResult(TaskToolSupport.ReadOutput(
            _runtime,
            parsed.Id,
            parsed.RunId,
            parsed.Offset ?? 0,
            parsed.Limit,
            "Task output"));
    }

    public string GetUserFacingName(JsonElement? input = null) => "Task output";

    public string? GetActivityDescription(JsonElement? input) => "Reading task output";
}

internal static class TaskToolSupport
{
    public static bool TryParseStatus(
        string? value,
        out AgentTaskViewStatus status,
        out string? error)
    {
        error = null;
        if (AgentTaskViewProjector.TryParseStatus(value, out status))
            return true;

        error = "status must be pending, running, completed, failed, or cancelled.";
        return false;
    }

    public static string FormatTaskDetails(
        IAgentTaskRuntime runtime,
        AgentWorkItem task,
        string? header = null)
    {
        var projected = AgentTaskViewProjector.Project(runtime, task);
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(header))
        {
            builder.AppendLine(header);
            builder.AppendLine();
        }

        builder.AppendLine($"Task: {task.Id}");
        builder.AppendLine($"Title: {task.Title}");
        if (!string.IsNullOrWhiteSpace(task.Description))
            builder.AppendLine($"Description: {task.Description}");
        if (!string.IsNullOrWhiteSpace(task.Owner))
            builder.AppendLine($"Owner: {task.Owner}");
        builder.AppendLine($"Status: {AgentTaskViewProjector.FormatStatus(projected.Status)}");
        builder.AppendLine($"Work item status: {task.Status}");
        builder.AppendLine($"Background runs: {projected.Runs.Count}");
        if (projected.ActiveRun != null)
            builder.AppendLine($"Active run: {projected.ActiveRun.Id} [{projected.ActiveRun.Status}]");
        if (projected.LatestRun != null)
            builder.AppendLine($"Latest run: {projected.LatestRun.Id} [{projected.LatestRun.Status}]");
        return builder.ToString().TrimEnd();
    }

    public static ToolResult ReadOutput(
        IAgentTaskRuntime runtime,
        string taskId,
        string? runId,
        int offset,
        int? limit,
        string heading)
    {
        if (!AgentBackgroundRunSelector.TrySelect(
                runtime,
                taskId,
                runId,
                out var selection,
                out var error))
        {
            return ToolResult.Error(error!);
        }

        if (!AgentStatusFormatter.TryGetOutputPage(
                runtime,
                selection!.Run.Id,
                offset,
                limit,
                out var run,
                out var page,
                out error))
        {
            return ToolResult.Error(error!);
        }

        var builder = new StringBuilder();
        builder.AppendLine(heading);
        builder.AppendLine();
        if (selection.WorkItem != null)
            builder.AppendLine($"Task: {selection.WorkItem.Id}");
        builder.AppendLine($"Selected run: {run!.Id} ({selection.SelectionReason})");
        builder.AppendLine(AgentStatusFormatter.FormatOutputPage(run, page, includeRunHeader: true));
        return ToolResult.Success(builder.ToString().TrimEnd());
    }

    public static string FormatPaginationMessage(
        string itemLabel,
        int totalCount,
        int offset,
        int shownCount)
    {
        if (totalCount == 0 || shownCount == 0)
            return $"No {itemLabel}s matched the requested filters.";

        var start = offset + 1;
        var end = offset + shownCount;
        return start == 1 && end == totalCount
            ? $"Showing all {totalCount} {itemLabel}(s)."
            : $"Showing {itemLabel}s {start}-{end} of {totalCount}.";
    }
}

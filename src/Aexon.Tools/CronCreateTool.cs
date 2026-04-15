using System.Text;
using System.Text.Json;
using Aexon.Core.Cron;
using Aexon.Core.Tools;

namespace Aexon.Tools;

/// <summary>
/// Creates a new scheduled cron job.
/// </summary>
public sealed class CronCreateTool : ITool
{
    private readonly ICronRuntime _runtime;

    public CronCreateTool(ICronRuntime runtime)
    {
        _runtime = runtime;
    }

    public string Name => "CronCreate";

    public string[] Aliases => ["CronCreateTool"];

    public Task<string> GetDescriptionAsync() =>
        Task.FromResult("Create a scheduled cron job that runs a command on a recurring schedule.");

    public JsonElement GetInputSchema()
    {
        var schema = """
        {
            "type": "object",
            "properties": {
                "id": {
                    "type": "string",
                    "description": "Unique identifier for the cron job"
                },
                "schedule": {
                    "type": "string",
                    "description": "Cron expression (5 fields: minute hour day-of-month month day-of-week). Examples: '*/5 * * * *' (every 5 min), '0 9 * * 1-5' (9am weekdays), '30 2 1 * *' (2:30am on 1st of month)"
                },
                "command": {
                    "type": "string",
                    "description": "Shell command to execute when the cron job triggers"
                },
                "description": {
                    "type": "string",
                    "description": "Optional human-readable description of what this job does"
                }
            },
            "required": ["id", "schedule", "command"],
            "additionalProperties": false
        }
        """;

        return JsonDocument.Parse(schema).RootElement.Clone();
    }

    public Task<string> GetPromptAsync(ToolPromptContext context) =>
        Task.FromResult("""
            Create a recurring scheduled task using a cron expression.

            The cron expression uses 5 fields: minute hour day-of-month month day-of-week.
            Common patterns:
            - '*/5 * * * *'  — every 5 minutes
            - '0 * * * *'    — every hour
            - '0 9 * * 1-5'  — 9:00 AM on weekdays
            - '0 0 * * *'    — midnight daily
            - '30 2 1 * *'   — 2:30 AM on the 1st of each month

            Field ranges: minute (0-59), hour (0-23), day (1-31), month (1-12), weekday (0-6, 0=Sunday).
            Supported operators: * (any), , (list), - (range), / (step).
            """);

    public Task<ValidationResult> ValidateInputAsync(
        JsonElement input,
        ToolExecutionContext context)
    {
        if (input.ValueKind != JsonValueKind.Object)
            return Task.FromResult(ValidationResult.Invalid("Input must be an object."));

        if (!TryGetString(input, "id", out var id) || string.IsNullOrWhiteSpace(id))
            return Task.FromResult(ValidationResult.Invalid("id is required."));

        if (!TryGetString(input, "schedule", out var schedule) || string.IsNullOrWhiteSpace(schedule))
            return Task.FromResult(ValidationResult.Invalid("schedule is required."));

        if (!TryGetString(input, "command", out var command) || string.IsNullOrWhiteSpace(command))
            return Task.FromResult(ValidationResult.Invalid("command is required."));

        if (CronExpression.TryParse(schedule) == null)
            return Task.FromResult(ValidationResult.Invalid($"Invalid cron expression: {schedule}"));

        if (_runtime.GetJob(id) != null)
            return Task.FromResult(ValidationResult.Invalid($"Cron job '{id}' already exists."));

        return Task.FromResult(ValidationResult.Valid());
    }

    public Task<ToolResult> ExecuteAsync(
        JsonElement input,
        ToolExecutionContext context,
        IProgress<ToolProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        TryGetString(input, "id", out var id);
        TryGetString(input, "schedule", out var schedule);
        TryGetString(input, "command", out var command);
        TryGetString(input, "description", out var description);

        try
        {
            var job = _runtime.CreateJob(id!, schedule!, command!, description);
            return Task.FromResult(ToolResult.Success(FormatJob("Created cron job", job)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error(ex.Message));
        }
    }

    public string GetUserFacingName(JsonElement? input = null) => "Cron create";

    public string? GetActivityDescription(JsonElement? input) => "Creating cron job";

    private static string FormatJob(string heading, CronJob job)
    {
        var sb = new StringBuilder();
        sb.AppendLine(heading);
        sb.AppendLine();
        sb.Append("- id: ").AppendLine(job.Id);
        sb.Append("  schedule: ").AppendLine(job.Schedule);
        sb.Append("  command: ").AppendLine(job.Command);
        if (!string.IsNullOrWhiteSpace(job.Description))
            sb.Append("  description: ").AppendLine(job.Description);
        sb.Append("  enabled: ").AppendLine(job.Enabled.ToString().ToLowerInvariant());
        if (job.NextRunAt != null)
            sb.Append("  next_run: ").AppendLine(job.NextRunAt.Value.ToString("u"));
        return sb.ToString().TrimEnd();
    }

    private static bool TryGetString(JsonElement input, string propertyName, out string? value)
    {
        value = null;
        if (!input.TryGetProperty(propertyName, out var prop) ||
            prop.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = prop.GetString();
        return true;
    }
}

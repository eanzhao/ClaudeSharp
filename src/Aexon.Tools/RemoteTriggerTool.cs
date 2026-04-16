using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aexon.Core.Agents;
using Aexon.Core.Cron;
using Aexon.Core.Tools;

namespace Aexon.Tools;

public sealed class RemoteTriggerToolInput
{
    [JsonPropertyName("action")]
    public string? Action { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("task_id")]
    public string? TaskId { get; set; }

    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("schedule")]
    public string? Schedule { get; set; }

    [JsonPropertyName("secret")]
    public string? Secret { get; set; }

    [JsonPropertyName("payload")]
    public string? Payload { get; set; }
}

public sealed class RemoteTriggerTool : ITool
{
    private readonly IAgentRemoteTriggerRuntime _runtime;

    public RemoteTriggerTool(IAgentRemoteTriggerRuntime runtime)
    {
        _runtime = runtime;
    }

    public string Name => "RemoteTrigger";

    public string[] Aliases => ["RemoteTriggerTool"];

    public Task<string> GetDescriptionAsync() =>
        Task.FromResult("Create, list, delete, and fire webhook or scheduled remote triggers linked to tasks.");

    public JsonElement GetInputSchema() => JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "action": { "type": "string", "enum": ["create", "list", "delete", "fire"] },
            "id": { "type": "string" },
            "task_id": { "type": "string" },
            "kind": { "type": "string", "enum": ["webhook", "schedule"] },
            "description": { "type": "string" },
            "schedule": { "type": "string" },
            "secret": { "type": "string" },
            "payload": { "type": "string" }
          },
          "required": ["action"],
          "additionalProperties": false
        }
        """).RootElement.Clone();

    public Task<string> GetPromptAsync(ToolPromptContext context) =>
        Task.FromResult("""
            Use RemoteTrigger to manage task-linked webhook and timed triggers.

            - action=create with kind=webhook creates a secret-backed trigger.
            - action=create with kind=schedule requires a cron expression.
            - action=list shows the current trigger inventory.
            - action=delete removes a trigger.
            - action=fire is useful for webhook simulation and tests.
            """);

    public Task<ValidationResult> ValidateInputAsync(JsonElement input, ToolExecutionContext context)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<RemoteTriggerToolInput>(input) ?? new RemoteTriggerToolInput();
            return Task.FromResult(Validate(parsed));
        }
        catch (JsonException ex)
        {
            return Task.FromResult(ValidationResult.Invalid(ex.Message));
        }
    }

    public bool IsReadOnly(JsonElement input)
    {
        var parsed = JsonSerializer.Deserialize<RemoteTriggerToolInput>(input);
        return string.Equals(parsed?.Action, "list", StringComparison.OrdinalIgnoreCase);
    }

    public bool IsConcurrencySafe(JsonElement input)
    {
        var parsed = JsonSerializer.Deserialize<RemoteTriggerToolInput>(input);
        return string.Equals(parsed?.Action, "list", StringComparison.OrdinalIgnoreCase);
    }

    public Task<ToolResult> ExecuteAsync(
        JsonElement input,
        ToolExecutionContext context,
        IProgress<ToolProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var parsed = JsonSerializer.Deserialize<RemoteTriggerToolInput>(input) ?? new RemoteTriggerToolInput();
        var validation = Validate(parsed);
        if (!validation.IsValid)
            return Task.FromResult(ToolResult.Error(validation.Message!));

        return Task.FromResult(NormalizeAction(parsed.Action!) switch
        {
            "create" => ExecuteCreate(parsed),
            "list" => ExecuteList(parsed),
            "delete" => ExecuteDelete(parsed),
            "fire" => ExecuteFire(parsed),
            _ => ToolResult.Error("action must be create, list, delete, or fire."),
        });
    }

    public string GetUserFacingName(JsonElement? input = null) => "Remote trigger";

    public string? GetActivityDescription(JsonElement? input) => "Managing remote triggers";

    private ToolResult ExecuteCreate(RemoteTriggerToolInput input)
    {
        var kind = NormalizeAction(input.Kind!);
        var trigger = _runtime.CreateTrigger(
            input.Id,
            input.TaskId!,
            kind == "schedule"
                ? AgentRemoteTriggerKind.Schedule
                : AgentRemoteTriggerKind.Webhook,
            input.Description,
            input.Schedule,
            input.Secret);
        return ToolResult.Success(FormatTrigger("Created remote trigger", trigger));
    }

    private ToolResult ExecuteList(RemoteTriggerToolInput input)
    {
        var triggers = _runtime.ListTriggers(input.TaskId);
        var builder = new StringBuilder();
        builder.AppendLine("Remote triggers:");
        if (triggers.Count == 0)
        {
            builder.AppendLine("(none)");
        }
        else
        {
            foreach (var trigger in triggers)
            {
                builder.Append("- ")
                    .Append(trigger.Id)
                    .Append(" [")
                    .Append(trigger.Kind.ToString().ToLowerInvariant())
                    .Append("] task=")
                    .Append(trigger.WorkItemId);
                if (trigger.NextTriggerAt is { } next)
                    builder.Append(" next=").Append(next.ToString("O"));
                builder.AppendLine();
            }
        }

        return ToolResult.Success(builder.ToString().TrimEnd());
    }

    private ToolResult ExecuteDelete(RemoteTriggerToolInput input)
    {
        var deleted = _runtime.DeleteTrigger(input.Id!);
        return deleted
            ? ToolResult.Success($"Deleted remote trigger '{input.Id!.Trim()}'.")
            : ToolResult.Error($"No remote trigger matched id '{input.Id!.Trim()}'.");
    }

    private ToolResult ExecuteFire(RemoteTriggerToolInput input)
    {
        var result = _runtime.FireTrigger(
            input.Id!,
            new AgentRemoteTriggerFireRequest(
                AgentRemoteTriggerFireSource.Webhook,
                Secret: input.Secret,
                Payload: input.Payload));
        return result.Status == AgentRemoteTriggerFireStatus.Fired
            ? ToolResult.Success(result.Message)
            : ToolResult.Error(result.Message);
    }

    private static ValidationResult Validate(RemoteTriggerToolInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Action))
            return ValidationResult.Invalid("action is required.");

        return NormalizeAction(input.Action) switch
        {
            "create" => ValidateCreate(input),
            "list" => ValidationResult.Valid(),
            "delete" => string.IsNullOrWhiteSpace(input.Id)
                ? ValidationResult.Invalid("id is required for delete.")
                : ValidationResult.Valid(),
            "fire" => string.IsNullOrWhiteSpace(input.Id)
                ? ValidationResult.Invalid("id is required for fire.")
                : ValidationResult.Valid(),
            _ => ValidationResult.Invalid("action must be create, list, delete, or fire."),
        };
    }

    private static ValidationResult ValidateCreate(RemoteTriggerToolInput input)
    {
        if (string.IsNullOrWhiteSpace(input.TaskId))
            return ValidationResult.Invalid("task_id is required for create.");
        if (string.IsNullOrWhiteSpace(input.Kind))
            return ValidationResult.Invalid("kind is required for create.");

        var kind = NormalizeAction(input.Kind);
        if (kind is not ("webhook" or "schedule"))
            return ValidationResult.Invalid("kind must be webhook or schedule.");

        if (kind == "schedule")
        {
            if (string.IsNullOrWhiteSpace(input.Schedule))
                return ValidationResult.Invalid("schedule is required for schedule triggers.");
            if (CronExpression.TryParse(input.Schedule) == null)
                return ValidationResult.Invalid($"Invalid cron expression: {input.Schedule}");
        }

        return ValidationResult.Valid();
    }

    private static string FormatTrigger(string heading, AgentRemoteTrigger trigger)
    {
        var builder = new StringBuilder();
        builder.AppendLine(heading);
        builder.AppendLine();
        builder.AppendLine($"- id: {trigger.Id}");
        builder.AppendLine($"  task_id: {trigger.WorkItemId}");
        builder.AppendLine($"  kind: {trigger.Kind.ToString().ToLowerInvariant()}");
        if (!string.IsNullOrWhiteSpace(trigger.Description))
            builder.AppendLine($"  description: {trigger.Description}");
        if (!string.IsNullOrWhiteSpace(trigger.Schedule))
            builder.AppendLine($"  schedule: {trigger.Schedule}");
        if (!string.IsNullOrWhiteSpace(trigger.Secret))
            builder.AppendLine($"  secret: {trigger.Secret}");
        if (trigger.NextTriggerAt is { } next)
            builder.AppendLine($"  next_trigger_at: {next:O}");
        return builder.ToString().TrimEnd();
    }

    private static string NormalizeAction(string value) =>
        value.Trim().ToLowerInvariant();
}

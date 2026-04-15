using System.Text.Json;
using Aexon.Core.Cron;
using Aexon.Core.Tools;

namespace Aexon.Tools;

/// <summary>
/// Deletes a scheduled cron job.
/// </summary>
public sealed class CronDeleteTool : ITool
{
    private readonly ICronRuntime _runtime;

    public CronDeleteTool(ICronRuntime runtime)
    {
        _runtime = runtime;
    }

    public string Name => "CronDelete";

    public string[] Aliases => ["CronDeleteTool"];

    public Task<string> GetDescriptionAsync() =>
        Task.FromResult("Delete a scheduled cron job by id.");

    public JsonElement GetInputSchema()
    {
        var schema = """
        {
            "type": "object",
            "properties": {
                "id": {
                    "type": "string",
                    "description": "The id of the cron job to delete"
                }
            },
            "required": ["id"],
            "additionalProperties": false
        }
        """;

        return JsonDocument.Parse(schema).RootElement.Clone();
    }

    public Task<string> GetPromptAsync(ToolPromptContext context) =>
        Task.FromResult("Delete a cron job by its id. The job will stop executing after deletion.");

    public Task<ValidationResult> ValidateInputAsync(
        JsonElement input,
        ToolExecutionContext context)
    {
        if (input.ValueKind != JsonValueKind.Object)
            return Task.FromResult(ValidationResult.Invalid("Input must be an object."));

        if (!input.TryGetProperty("id", out var idProp) ||
            idProp.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(idProp.GetString()))
        {
            return Task.FromResult(ValidationResult.Invalid("id is required."));
        }

        var id = idProp.GetString()!;
        if (_runtime.GetJob(id) == null)
            return Task.FromResult(ValidationResult.Invalid($"Cron job '{id}' was not found."));

        return Task.FromResult(ValidationResult.Valid());
    }

    public Task<ToolResult> ExecuteAsync(
        JsonElement input,
        ToolExecutionContext context,
        IProgress<ToolProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var id = input.GetProperty("id").GetString()!;
        var deleted = _runtime.DeleteJob(id);

        return Task.FromResult(deleted
            ? ToolResult.Success($"Deleted cron job '{id}'.")
            : ToolResult.Error($"Cron job '{id}' was not found."));
    }

    public string GetUserFacingName(JsonElement? input = null) => "Cron delete";

    public string? GetActivityDescription(JsonElement? input) => "Deleting cron job";
}

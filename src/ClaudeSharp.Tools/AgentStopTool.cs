using System.Text.Json;
using System.Text.Json.Serialization;
using ClaudeSharp.Core.Agents;
using ClaudeSharp.Core.Permissions;
using ClaudeSharp.Core.Tools;

namespace ClaudeSharp.Tools;

/// <summary>
/// Represents the input payload for the AgentStop tool.
/// </summary>
public sealed class AgentStopToolInput
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}

/// <summary>
/// Requests cancellation for a running background subagent.
/// </summary>
public sealed class AgentStopTool : ITool
{
    private readonly IAgentTaskRuntime _taskRuntime;

    public AgentStopTool(IAgentTaskRuntime taskRuntime)
    {
        _taskRuntime = taskRuntime;
    }

    public string Name => "AgentStop";

    public Task<string> GetDescriptionAsync() =>
        Task.FromResult(
            "Request cancellation for a running background subagent.");

    public JsonElement GetInputSchema()
    {
        return JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "id": {
              "type": "string",
              "description": "The background-run id to cancel"
            },
            "reason": {
              "type": "string",
              "description": "Optional reason recorded on the background run"
            }
          },
          "required": ["id"],
          "additionalProperties": false
        }
        """).RootElement.Clone();
    }

    public Task<string> GetPromptAsync(ToolPromptContext context)
    {
        return Task.FromResult("""
            Cancel a background subagent that is still running.

            Use AgentStatus first if you need to confirm the exact background-run id.
            """);
    }

    public Task<ValidationResult> ValidateInputAsync(JsonElement input, ToolExecutionContext context)
    {
        var parsed = JsonSerializer.Deserialize<AgentStopToolInput>(input);
        if (parsed == null || string.IsNullOrWhiteSpace(parsed.Id))
            return Task.FromResult(ValidationResult.Invalid("id is required."));

        return Task.FromResult(ValidationResult.Valid());
    }

    public Task<PermissionResult> CheckPermissionsAsync(JsonElement input, ToolExecutionContext context) =>
        Task.FromResult(PermissionResult.Allow());

    public bool IsConcurrencySafe(JsonElement input) => true;

    public bool IsEnabled() => false;

    public string GetUserFacingName(JsonElement? input = null) => "Stop subagent";

    public string? GetActivityDescription(JsonElement? input) => "Stopping subagent";

    public Task<ToolResult> ExecuteAsync(
        JsonElement input,
        ToolExecutionContext context,
        IProgress<ToolProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var parsed = JsonSerializer.Deserialize<AgentStopToolInput>(input);
        if (parsed == null || string.IsNullOrWhiteSpace(parsed.Id))
            return Task.FromResult(ToolResult.Error("id is required."));

        var id = parsed.Id.Trim();
        var result = _taskRuntime.RequestBackgroundRunCancellation(id, parsed.Reason?.Trim());

        var message = result switch
        {
            AgentBackgroundRunCancellationResult.Requested =>
                $"Cancellation requested for {id}.",
            AgentBackgroundRunCancellationResult.AlreadyRequested =>
                $"Cancellation was already requested for {id}.",
            AgentBackgroundRunCancellationResult.AlreadyCompleted =>
                $"{id} has already finished.",
            AgentBackgroundRunCancellationResult.Unsupported =>
                $"{id} does not support cancellation.",
            _ =>
                $"No background run matched id '{id}'.",
        };

        if (result == AgentBackgroundRunCancellationResult.Requested)
            _taskRuntime.AppendBackgroundRunOutput(id, $"[status] {message}");

        return Task.FromResult(result is AgentBackgroundRunCancellationResult.Requested or
            AgentBackgroundRunCancellationResult.AlreadyRequested
            ? ToolResult.Success(message)
            : ToolResult.Error(message));
    }
}

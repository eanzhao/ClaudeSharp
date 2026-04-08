using System.Text.Json;
using System.Text.Json.Serialization;
using ClaudeSharp.Core.Agents;
using ClaudeSharp.Core.Permissions;
using ClaudeSharp.Core.Tools;

namespace ClaudeSharp.Tools;

/// <summary>
/// Represents the input payload for the AgentWait tool.
/// </summary>
public sealed class AgentWaitToolInput
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("poll_ms")]
    public int? PollMs { get; set; }

    [JsonPropertyName("timeout_ms")]
    public int? TimeoutMs { get; set; }

    [JsonPropertyName("include_output")]
    public bool IncludeOutput { get; set; }
}

/// <summary>
/// Waits for a background subagent to finish.
/// </summary>
public sealed class AgentWaitTool : ITool
{
    private readonly IAgentTaskRuntime _taskRuntime;

    public AgentWaitTool(IAgentTaskRuntime taskRuntime)
    {
        _taskRuntime = taskRuntime;
    }

    public string Name => "AgentWait";

    public Task<string> GetDescriptionAsync() =>
        Task.FromResult(
            "Wait for a background subagent to reach a terminal state and then return its final status.");

    public JsonElement GetInputSchema()
    {
        return JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "id": {
              "type": "string",
              "description": "The background-run id to wait for"
            },
            "poll_ms": {
              "type": "integer",
              "description": "How often to poll the run status while waiting"
            },
            "timeout_ms": {
              "type": "integer",
              "description": "Optional timeout for the wait operation"
            },
            "include_output": {
              "type": "boolean",
              "description": "When true, include the captured background-run output in the final result"
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
            Wait for a background subagent to finish.

            Use this after launching Agent with run_in_background=true when you need the final status before continuing.
            Set include_output=true if you also need the captured run output in the response.
            """);
    }

    public Task<ValidationResult> ValidateInputAsync(JsonElement input, ToolExecutionContext context)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<AgentWaitToolInput>(input);
            if (parsed == null || string.IsNullOrWhiteSpace(parsed.Id))
                return Task.FromResult(ValidationResult.Invalid("id is required."));
            if (parsed.PollMs is <= 0)
                return Task.FromResult(ValidationResult.Invalid("poll_ms must be greater than 0."));
            if (parsed.TimeoutMs is <= 0)
                return Task.FromResult(ValidationResult.Invalid("timeout_ms must be greater than 0."));

            return Task.FromResult(ValidationResult.Valid());
        }
        catch (JsonException ex)
        {
            return Task.FromResult(ValidationResult.Invalid(ex.Message));
        }
    }

    public Task<PermissionResult> CheckPermissionsAsync(JsonElement input, ToolExecutionContext context) =>
        Task.FromResult(PermissionResult.Allow());

    public bool IsReadOnly(JsonElement input) => true;

    public bool IsConcurrencySafe(JsonElement input) => true;

    public string GetUserFacingName(JsonElement? input = null) => "Wait for subagent";

    public string? GetActivityDescription(JsonElement? input) => "Waiting for subagent";

    public async Task<ToolResult> ExecuteAsync(
        JsonElement input,
        ToolExecutionContext context,
        IProgress<ToolProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var parsed = JsonSerializer.Deserialize<AgentWaitToolInput>(input);
        if (parsed == null || string.IsNullOrWhiteSpace(parsed.Id))
            return ToolResult.Error("id is required.");

        var id = parsed.Id.Trim();
        var waitResult = await AgentBackgroundRunWaiter.WaitAsync(
            _taskRuntime,
            id,
            TimeSpan.FromMilliseconds(parsed.PollMs ?? 500),
            parsed.TimeoutMs.HasValue
                ? TimeSpan.FromMilliseconds(parsed.TimeoutMs.Value)
                : null,
            cancellationToken: cancellationToken);

        return waitResult.Outcome switch
        {
            AgentBackgroundRunWaitOutcome.NotFound =>
                ToolResult.Error($"No background run matched id '{id}'."),
            AgentBackgroundRunWaitOutcome.TimedOut =>
                ToolResult.Error(BuildTimedOutMessage(waitResult)),
            _ =>
                ToolResult.Success(BuildCompletedMessage(waitResult, parsed.IncludeOutput)),
        };
    }

    private string BuildCompletedMessage(
        AgentBackgroundRunWaitResult waitResult,
        bool includeOutput)
    {
        AgentStatusFormatter.TryFormatDetails(
            _taskRuntime,
            waitResult.BackgroundRunId,
            includeOutput,
            outputOffset: null,
            outputLimit: null,
            out var details);

        return $"""
            Wait finished after {Math.Round(waitResult.Elapsed.TotalMilliseconds)}ms.

            {details}
            """.TrimEnd();
    }

    private static string BuildTimedOutMessage(AgentBackgroundRunWaitResult waitResult)
    {
        var status = waitResult.Run?.Status.ToString() ?? "Unknown";
        return $"Timed out after {Math.Round(waitResult.Elapsed.TotalMilliseconds)}ms while waiting for {waitResult.BackgroundRunId}. Current status: {status}.";
    }
}

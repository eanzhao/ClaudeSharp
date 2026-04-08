using System.Text.Json;
using System.Text.Json.Serialization;
using ClaudeSharp.Core.Agents;
using ClaudeSharp.Core.Tools;

namespace ClaudeSharp.Tools;

/// <summary>
/// Represents the input payload for the AgentStatus tool.
/// </summary>
public sealed class AgentStatusToolInput
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("include_output")]
    public bool IncludeOutput { get; set; } = true;

    [JsonPropertyName("output_offset")]
    public int? OutputOffset { get; set; }

    [JsonPropertyName("output_limit")]
    public int? OutputLimit { get; set; }
}

/// <summary>
/// Reads the current state of subagent work items and background runs.
/// </summary>
public sealed class AgentStatusTool : ITool
{
    private readonly IAgentTaskRuntime _taskRuntime;

    public AgentStatusTool(IAgentTaskRuntime taskRuntime)
    {
        _taskRuntime = taskRuntime;
    }

    public string Name => "AgentStatus";

    public Task<string> GetDescriptionAsync() =>
        Task.FromResult(
            "Inspect subagent work items and background runs. Use it to check whether a background subagent finished and to read its summary.");

    public JsonElement GetInputSchema()
    {
        return JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "id": {
              "type": "string",
              "description": "Optional work-item or background-run id to inspect in detail"
            },
            "include_output": {
              "type": "boolean",
              "description": "When inspecting a background run, include any captured output"
            },
            "output_offset": {
              "type": "integer",
              "description": "Skip this many output entries before formatting background-run output"
            },
            "output_limit": {
              "type": "integer",
              "description": "Return at most this many output entries from a background run"
            }
          },
          "additionalProperties": false
        }
        """).RootElement.Clone();
    }

    public Task<string> GetPromptAsync(ToolPromptContext context)
    {
        return Task.FromResult("""
            Check the state of subagent work items and background runs.

            Use this after Agent with run_in_background=true, or whenever you need to see whether a subagent has finished.
            When polling a long-running background run, use output_offset and output_limit to fetch only new output entries.
            """);
    }

    public Task<ValidationResult> ValidateInputAsync(JsonElement input, ToolExecutionContext context)
    {
        try
        {
            JsonSerializer.Deserialize<AgentStatusToolInput>(input);
            return Task.FromResult(ValidationResult.Valid());
        }
        catch (JsonException ex)
        {
            return Task.FromResult(ValidationResult.Invalid(ex.Message));
        }
    }

    public bool IsReadOnly(JsonElement input) => true;

    public bool IsConcurrencySafe(JsonElement input) => true;

    public string GetUserFacingName(JsonElement? input = null) => "Agent status";

    public string? GetActivityDescription(JsonElement? input) => "Checking subagent status";

    public Task<ToolResult> ExecuteAsync(
        JsonElement input,
        ToolExecutionContext context,
        IProgress<ToolProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var parsed = JsonSerializer.Deserialize<AgentStatusToolInput>(input) ?? new AgentStatusToolInput();
        if (string.IsNullOrWhiteSpace(parsed.Id))
            return Task.FromResult(ToolResult.Success(AgentStatusFormatter.FormatOverview(_taskRuntime)));

        var found = AgentStatusFormatter.TryFormatDetails(
            _taskRuntime,
            parsed.Id.Trim(),
            parsed.IncludeOutput,
            parsed.OutputOffset,
            parsed.OutputLimit,
            out var details);

        return Task.FromResult(found
            ? ToolResult.Success(details)
            : ToolResult.Error(details));
    }
}

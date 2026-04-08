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

    [JsonPropertyName("view")]
    public string? View { get; set; }

    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("owner")]
    public string? Owner { get; set; }

    [JsonPropertyName("offset")]
    public int? Offset { get; set; }

    [JsonPropertyName("limit")]
    public int? Limit { get; set; }

    [JsonPropertyName("recent_limit")]
    public int? RecentLimit { get; set; }

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
            "view": {
              "type": "string",
              "description": "For listings only: overview (default) or summary"
            },
            "kind": {
              "type": "string",
              "description": "For overview listings only: all, work_items, or background_runs"
            },
            "status": {
              "type": "string",
              "description": "For overview listings only: filter by a status like queued, running, completed, or blocked"
            },
            "owner": {
              "type": "string",
              "description": "For overview listings only: filter by owner"
            },
            "offset": {
              "type": "integer",
              "description": "For overview listings only: skip this many matching entries before listing results"
            },
            "limit": {
              "type": "integer",
              "description": "For overview listings only: return at most this many matching entries per section"
            },
            "recent_limit": {
              "type": "integer",
              "description": "For summary listings only: return at most this many recent work items and background runs"
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
            For overview listings, you can filter by kind, status, owner, offset, and limit.
            Set view=summary when you want a concise rollup of queue state, recent runs, and recent work items.
            When polling a long-running background run, use output_offset and output_limit to fetch only new output entries.
            """);
    }

    public Task<ValidationResult> ValidateInputAsync(JsonElement input, ToolExecutionContext context)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<AgentStatusToolInput>(input) ?? new AgentStatusToolInput();
            if (!AgentStatusFormatter.TryParseView(parsed.View, out _))
                return Task.FromResult(ValidationResult.Invalid("view must be overview or summary."));
            if (!AgentStatusFormatter.TryParseOverviewKind(parsed.Kind, out _))
                return Task.FromResult(ValidationResult.Invalid("kind must be all, work_items, or background_runs."));
            if (parsed.Offset is < 0)
                return Task.FromResult(ValidationResult.Invalid("offset must be 0 or greater."));
            if (parsed.Limit is <= 0)
                return Task.FromResult(ValidationResult.Invalid("limit must be greater than 0."));
            if (parsed.RecentLimit is <= 0)
                return Task.FromResult(ValidationResult.Invalid("recent_limit must be greater than 0."));
            if (parsed.OutputOffset is < 0)
                return Task.FromResult(ValidationResult.Invalid("output_offset must be 0 or greater."));
            if (parsed.OutputLimit is <= 0)
                return Task.FromResult(ValidationResult.Invalid("output_limit must be greater than 0."));
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
        {
            AgentStatusFormatter.TryParseView(parsed.View, out var view);
            AgentStatusFormatter.TryParseOverviewKind(parsed.Kind, out var kind);
            var data = view == AgentStatusView.Summary
                ? AgentStatusFormatter.FormatSummary(
                    _taskRuntime,
                    new AgentStatusSummaryOptions
                    {
                        Owner = parsed.Owner,
                        RecentLimit = parsed.RecentLimit ?? 3,
                    })
                : AgentStatusFormatter.FormatOverview(
                    _taskRuntime,
                    new AgentStatusOverviewOptions
                    {
                        Kind = kind,
                        Status = parsed.Status,
                        Owner = parsed.Owner,
                        Offset = Math.Max(0, parsed.Offset ?? 0),
                        Limit = parsed.Limit,
                    });
            return Task.FromResult(ToolResult.Success(data));
        }

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

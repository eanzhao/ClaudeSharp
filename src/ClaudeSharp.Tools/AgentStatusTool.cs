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

    [JsonPropertyName("filters")]
    public string? Filters { get; set; }

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

    [JsonPropertyName("output_window")]
    public string? OutputWindow { get; set; }

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
            "filters": {
              "type": "string",
              "description": "Optional compact filters such as kind=background_runs status=queued owner=subagent offset=0 limit=5 recent_limit=3"
            },
            "include_output": {
              "type": "boolean",
              "description": "When inspecting a background run, include any captured output"
            },
            "output_window": {
              "type": "string",
              "description": "For detailed background-run output only: offset and limit as offset:limit, for example 10:20"
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
            For overview listings, filters can be packed into one string like "kind=background_runs status=queued owner=subagent offset=0 limit=5".
            Set view=summary when you want a concise rollup of queue state, recent runs, and recent work items.
            When polling a long-running background run, use output_window like "10:20" to fetch only new output entries.
            """);
    }

    public Task<ValidationResult> ValidateInputAsync(JsonElement input, ToolExecutionContext context)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<AgentStatusToolInput>(input) ?? new AgentStatusToolInput();
            if (!TryNormalizeRequest(parsed, out _, out var error))
                return Task.FromResult(ValidationResult.Invalid(error!));

            return Task.FromResult(ValidationResult.Valid());
        }
        catch (JsonException ex)
        {
            return Task.FromResult(ValidationResult.Invalid(ex.Message));
        }
    }

    public bool IsReadOnly(JsonElement input) => true;

    public bool IsConcurrencySafe(JsonElement input) => true;

    public bool IsEnabled() => false;

    public string GetUserFacingName(JsonElement? input = null) => "Agent status";

    public string? GetActivityDescription(JsonElement? input) => "Checking subagent status";

    public Task<ToolResult> ExecuteAsync(
        JsonElement input,
        ToolExecutionContext context,
        IProgress<ToolProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var parsed = JsonSerializer.Deserialize<AgentStatusToolInput>(input) ?? new AgentStatusToolInput();
        if (!TryNormalizeRequest(parsed, out var request, out var error))
            return Task.FromResult(ToolResult.Error(error!));

        if (string.IsNullOrWhiteSpace(request.Id))
        {
            var data = request.View == AgentStatusView.Summary
                ? AgentStatusFormatter.FormatSummary(
                    _taskRuntime,
                    new AgentStatusSummaryOptions
                    {
                        Owner = request.Owner,
                        RecentLimit = request.RecentLimit,
                    })
                : AgentStatusFormatter.FormatOverview(
                    _taskRuntime,
                    new AgentStatusOverviewOptions
                    {
                        Kind = request.Kind,
                        Status = request.Status,
                        Owner = request.Owner,
                        Offset = request.Offset,
                        Limit = request.Limit,
                    });
            return Task.FromResult(ToolResult.Success(data));
        }

        var found = AgentStatusFormatter.TryFormatDetails(
            _taskRuntime,
            request.Id.Trim(),
            request.IncludeOutput,
            request.OutputOffset,
            request.OutputLimit,
            out var details);

        return Task.FromResult(found
            ? ToolResult.Success(details)
            : ToolResult.Error(details));
    }

    private static bool TryNormalizeRequest(
        AgentStatusToolInput input,
        out NormalizedAgentStatusRequest request,
        out string? error)
    {
        error = null;
        AgentStatusFormatter.TryParseView(input.View, out var view);
        AgentStatusFormatter.TryParseOverviewKind(input.Kind, out var kind);

        if (!string.IsNullOrWhiteSpace(input.View) &&
            !AgentStatusFormatter.TryParseView(input.View, out view))
        {
            request = default;
            error = "view must be overview or summary.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(input.Kind) &&
            !AgentStatusFormatter.TryParseOverviewKind(input.Kind, out kind))
        {
            request = default;
            error = "kind must be all, work_items, or background_runs.";
            return false;
        }

        var status = input.Status;
        var owner = input.Owner;
        var offset = input.Offset ?? 0;
        var limit = input.Limit;
        var recentLimit = input.RecentLimit ?? 3;
        var outputOffset = input.OutputOffset;
        var outputLimit = input.OutputLimit;

        if (!TryApplyFilters(
                input.Filters,
                ref kind,
                ref status,
                ref owner,
                ref offset,
                ref limit,
                ref recentLimit,
                out error))
        {
            request = default;
            return false;
        }

        if (!TryApplyOutputWindow(
                input.OutputWindow,
                ref outputOffset,
                ref outputLimit,
                out error))
        {
            request = default;
            return false;
        }

        if (offset < 0)
        {
            request = default;
            error = "offset must be 0 or greater.";
            return false;
        }

        if (limit is <= 0)
        {
            request = default;
            error = "limit must be greater than 0.";
            return false;
        }

        if (recentLimit <= 0)
        {
            request = default;
            error = "recent_limit must be greater than 0.";
            return false;
        }

        if (outputOffset is < 0)
        {
            request = default;
            error = "output_offset must be 0 or greater.";
            return false;
        }

        if (outputLimit is <= 0)
        {
            request = default;
            error = "output_limit must be greater than 0.";
            return false;
        }

        request = new NormalizedAgentStatusRequest(
            input.Id?.Trim(),
            view,
            kind,
            status,
            owner,
            offset,
            limit,
            recentLimit,
            input.IncludeOutput,
            outputOffset,
            outputLimit);
        return true;
    }

    private static bool TryApplyFilters(
        string? filters,
        ref AgentStatusOverviewKind kind,
        ref string? status,
        ref string? owner,
        ref int offset,
        ref int? limit,
        ref int recentLimit,
        out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(filters))
            return true;

        foreach (var token in filters.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = token.IndexOf('=');
            if (separatorIndex <= 0 || separatorIndex == token.Length - 1)
            {
                error = $"filters contains an invalid token: {token}";
                return false;
            }

            var key = token[..separatorIndex];
            var value = token[(separatorIndex + 1)..];

            switch (key)
            {
                case "kind":
                    if (!AgentStatusFormatter.TryParseOverviewKind(value, out kind))
                    {
                        error = "kind must be all, work_items, or background_runs.";
                        return false;
                    }

                    break;

                case "status":
                    status = value;
                    break;

                case "owner":
                    owner = value;
                    break;

                case "offset":
                    if (!int.TryParse(value, out offset))
                    {
                        error = "offset must be an integer.";
                        return false;
                    }

                    break;

                case "limit":
                    if (!int.TryParse(value, out var parsedLimit))
                    {
                        error = "limit must be an integer.";
                        return false;
                    }

                    limit = parsedLimit;
                    break;

                case "recent_limit":
                    if (!int.TryParse(value, out recentLimit))
                    {
                        error = "recent_limit must be an integer.";
                        return false;
                    }

                    break;

                default:
                    error = $"filters contains an unknown key: {key}";
                    return false;
            }
        }

        return true;
    }

    private static bool TryApplyOutputWindow(
        string? outputWindow,
        ref int? outputOffset,
        ref int? outputLimit,
        out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(outputWindow))
            return true;

        var separators = new[] { ':', ',' };
        var parts = outputWindow
            .Split(separators, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            error = "output_window must look like offset:limit.";
            return false;
        }

        if (!int.TryParse(parts[0], out var parsedOffset) ||
            !int.TryParse(parts[1], out var parsedLimit))
        {
            error = "output_window must contain integer offset and limit values.";
            return false;
        }

        outputOffset ??= parsedOffset;
        outputLimit ??= parsedLimit;
        return true;
    }

    private readonly record struct NormalizedAgentStatusRequest(
        string? Id,
        AgentStatusView View,
        AgentStatusOverviewKind Kind,
        string? Status,
        string? Owner,
        int Offset,
        int? Limit,
        int RecentLimit,
        bool IncludeOutput,
        int? OutputOffset,
        int? OutputLimit);
}

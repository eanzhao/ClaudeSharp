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

    [JsonPropertyName("ids")]
    public string[]? Ids { get; set; }

    [JsonPropertyName("wait_mode")]
    public string? WaitMode { get; set; }

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
            "Wait for one or more background subagents to reach terminal states and then return their final statuses.");

    public JsonElement GetInputSchema()
    {
        return JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "ids": {
              "type": "array",
              "items": {
                "type": "string"
              },
              "description": "One or more background-run ids to wait for"
            },
            "wait_mode": {
              "type": "string",
              "enum": ["all", "any"],
              "description": "Whether to wait for all runs or return when any run finishes"
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
          "required": ["ids"],
          "additionalProperties": false
        }
        """).RootElement.Clone();
    }

    public Task<string> GetPromptAsync(ToolPromptContext context)
    {
        return Task.FromResult("""
            Wait for one or more background subagents to finish.

            Use this after launching Agent with run_in_background=true when you need a final status before continuing.
            Always pass ids. For a single run, pass an array with one id.
            Set include_output=true if you also need the captured run output in the response.
            """);
    }

    public Task<ValidationResult> ValidateInputAsync(JsonElement input, ToolExecutionContext context)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<AgentWaitToolInput>(input);
            if (parsed == null)
                return Task.FromResult(ValidationResult.Invalid("id or ids is required."));

            if (!TryCollectRunIds(parsed, out _, out var error))
                return Task.FromResult(ValidationResult.Invalid(error!));

            if (parsed.PollMs is <= 0)
                return Task.FromResult(ValidationResult.Invalid("poll_ms must be greater than 0."));
            if (parsed.TimeoutMs is <= 0)
                return Task.FromResult(ValidationResult.Invalid("timeout_ms must be greater than 0."));
            if (!TryParseWaitMode(parsed.WaitMode, out _, out error))
                return Task.FromResult(ValidationResult.Invalid(error!));

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

    public bool IsEnabled() => false;

    public string GetUserFacingName(JsonElement? input = null) => "Wait for subagent";

    public string? GetActivityDescription(JsonElement? input) => "Waiting for subagent";

    public async Task<ToolResult> ExecuteAsync(
        JsonElement input,
        ToolExecutionContext context,
        IProgress<ToolProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var parsed = JsonSerializer.Deserialize<AgentWaitToolInput>(input);
        if (parsed == null)
            return ToolResult.Error("id or ids is required.");

        if (!TryCollectRunIds(parsed, out var runIds, out var error))
            return ToolResult.Error(error!);

        if (!TryParseWaitMode(parsed.WaitMode, out var waitMode, out error))
            return ToolResult.Error(error!);

        var waitResult = await AgentBackgroundRunWaiter.WaitManyAsync(
            _taskRuntime,
            runIds,
            waitMode,
            TimeSpan.FromMilliseconds(parsed.PollMs ?? 500),
            parsed.TimeoutMs.HasValue
                ? TimeSpan.FromMilliseconds(parsed.TimeoutMs.Value)
                : null,
            cancellationToken: cancellationToken);

        return waitResult.Outcome switch
        {
            AgentBackgroundRunWaitOutcome.NotFound =>
                ToolResult.Error(BuildNotFoundMessage(waitResult)),
            AgentBackgroundRunWaitOutcome.TimedOut =>
                ToolResult.Error(BuildTimedOutMessage(waitResult)),
            _ =>
                ToolResult.Success(BuildCompletedMessage(waitResult, parsed.IncludeOutput)),
        };
    }

    private string BuildCompletedMessage(
        AgentBackgroundRunWaitBatchResult waitResult,
        bool includeOutput)
    {
        var lines = new List<string>
        {
            $"Wait finished after {Math.Round(waitResult.Elapsed.TotalMilliseconds)}ms.",
        };

        if (waitResult.Mode == AgentBackgroundRunWaitMode.Any && waitResult.PendingRuns.Count > 0)
            lines.Add($"At least one background run finished. {waitResult.PendingRuns.Count} run(s) are still active.");
        else
            lines.Add($"All {waitResult.CompletedRuns.Count} background run(s) reached terminal states.");

        if (waitResult.CompletedRuns.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Completed runs:");
            foreach (var snapshot in waitResult.CompletedRuns)
                lines.Add($"- {snapshot.BackgroundRunId}: {snapshot.Run!.Status}");
        }

        if (waitResult.PendingRuns.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Still running:");
            foreach (var snapshot in waitResult.PendingRuns)
                lines.Add($"- {snapshot.BackgroundRunId}: {snapshot.Run!.Status}");
        }

        if (includeOutput && waitResult.CompletedRuns.Count > 0)
        {
            foreach (var snapshot in waitResult.CompletedRuns)
            {
                if (!AgentStatusFormatter.TryFormatDetails(
                        _taskRuntime,
                        snapshot.BackgroundRunId,
                        includeOutput: true,
                        outputOffset: null,
                        outputLimit: null,
                        out var details))
                {
                    continue;
                }

                lines.Add(string.Empty);
                lines.Add(details);
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildTimedOutMessage(AgentBackgroundRunWaitBatchResult waitResult)
    {
        if (waitResult.PendingRuns.Count == 0 && waitResult.CompletedRuns.Count == 0)
            return $"Timed out after {Math.Round(waitResult.Elapsed.TotalMilliseconds)}ms while waiting for background runs.";

        var statuses = waitResult.CompletedRuns
            .Concat(waitResult.PendingRuns)
            .Select(snapshot => $"{snapshot.BackgroundRunId}={snapshot.Run?.Status ?? AgentBackgroundRunStatus.Queued}");
        var target = waitResult.Mode == AgentBackgroundRunWaitMode.Any ? "any of the requested runs" : "all requested runs";
        return $"Timed out after {Math.Round(waitResult.Elapsed.TotalMilliseconds)}ms while waiting for {target}. Current statuses: {string.Join(", ", statuses)}.";
    }

    private static string BuildNotFoundMessage(AgentBackgroundRunWaitBatchResult waitResult) =>
        waitResult.MissingRunIds.Count == 1
            ? $"No background run matched id '{waitResult.MissingRunIds[0]}'."
            : $"No background runs matched these ids: {string.Join(", ", waitResult.MissingRunIds)}.";

    private static bool TryCollectRunIds(
        AgentWaitToolInput parsed,
        out List<string> runIds,
        out string? error)
    {
        runIds = [];
        error = null;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(parsed.Id))
        {
            var normalized = parsed.Id.Trim();
            if (seen.Add(normalized))
                runIds.Add(normalized);
        }

        if (parsed.Ids != null)
        {
            foreach (var id in parsed.Ids)
            {
                if (string.IsNullOrWhiteSpace(id))
                {
                    error = "ids cannot contain empty values.";
                    return false;
                }

                var normalized = id.Trim();
                if (seen.Add(normalized))
                    runIds.Add(normalized);
            }
        }

        if (runIds.Count == 0)
        {
            error = "id or ids is required.";
            return false;
        }

        return true;
    }

    private static bool TryParseWaitMode(
        string? rawMode,
        out AgentBackgroundRunWaitMode waitMode,
        out string? error)
    {
        error = null;
        waitMode = AgentBackgroundRunWaitMode.All;

        if (string.IsNullOrWhiteSpace(rawMode))
            return true;

        if (string.Equals(rawMode, "all", StringComparison.OrdinalIgnoreCase))
            return true;

        if (string.Equals(rawMode, "any", StringComparison.OrdinalIgnoreCase))
        {
            waitMode = AgentBackgroundRunWaitMode.Any;
            return true;
        }

        error = "wait_mode must be either 'all' or 'any'.";
        return false;
    }
}

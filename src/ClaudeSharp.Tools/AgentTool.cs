using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClaudeSharp.Core.Agents;
using ClaudeSharp.Core.Hooks;
using ClaudeSharp.Core.Providers;
using ClaudeSharp.Core.Query;
using ClaudeSharp.Core.Tools;

namespace ClaudeSharp.Tools;

/// <summary>
/// Represents the input payload for the Agent tool.
/// </summary>
public sealed class AgentToolInput
{
    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } = "";

    [JsonPropertyName("subagent_type")]
    public string? SubagentType { get; set; }

    [JsonPropertyName("run_in_background")]
    public bool RunInBackground { get; set; }

    [JsonPropertyName("use_isolated_workspace")]
    public bool UseIsolatedWorkspace { get; set; } = true;
}

/// <summary>
/// Runs a read-only subagent for focused codebase research.
/// </summary>
public sealed class AgentTool : ITool
{
    private readonly IAgentExecutionRunner _runner;
    private readonly IProviderCapabilityRouter _providerCapabilityRouter;
    private readonly IAgentTaskRuntime _taskRuntime;
    private readonly IHookRuntime _hooks;

    public AgentTool(
        IAgentExecutionRunner runner,
        IProviderCapabilityRouter? providerCapabilityRouter = null,
        IAgentTaskRuntime? taskRuntime = null,
        IHookRuntime? hooks = null)
    {
        _runner = runner;
        _providerCapabilityRouter = providerCapabilityRouter ?? new DefaultProviderCapabilityRouter();
        _taskRuntime = taskRuntime ?? new InMemoryAgentTaskRuntime();
        _hooks = hooks ?? HookRuntime.Empty;
    }

    public string Name => "Agent";

    public Task<string> GetDescriptionAsync() =>
        Task.FromResult(
            "Launch a focused read-only subagent that can inspect the codebase and return a concise summary.");

    public JsonElement GetInputSchema()
    {
        return JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "prompt": {
              "type": "string",
              "description": "The concrete subtask for the subagent to investigate"
            },
            "subagent_type": {
              "type": "string",
              "description": "Optional hint like research, debugging, or review"
            },
            "run_in_background": {
              "type": "boolean",
              "description": "When true, launch the subagent in the background and return immediately"
            },
            "use_isolated_workspace": {
              "type": "boolean",
              "description": "When true, run the subagent in a temporary isolated workspace when possible"
            }
          },
          "required": ["prompt"],
          "additionalProperties": false
        }
        """).RootElement.Clone();
    }

    public Task<string> GetPromptAsync(ToolPromptContext context)
    {
        return Task.FromResult("""
            Launch a focused read-only subagent for bounded research work.

            Good uses:
            - Explore a subsystem and summarize how it works
            - Trace where a behavior is implemented
            - Gather evidence before editing code yourself
            - Review a narrow slice of the codebase and report findings

            Important limits:
            - This ClaudeSharp implementation runs subagents in read-only mode
            - The subagent can inspect files and use web discovery tools, but it does not edit files
            - Set run_in_background=true when the work can continue asynchronously
            - By default the subagent runs in a temporary isolated workspace when the repo supports it
            - Use it for separable investigation tasks, not for the final user-facing answer
            - Give it a concrete prompt with a clear scope and expected output
            """);
    }

    public Task<ValidationResult> ValidateInputAsync(JsonElement input, ToolExecutionContext context)
    {
        var parsed = JsonSerializer.Deserialize<AgentToolInput>(input);
        if (parsed == null || string.IsNullOrWhiteSpace(parsed.Prompt))
            return Task.FromResult(ValidationResult.Invalid("prompt is required."));

        return Task.FromResult(ValidationResult.Valid());
    }

    public bool IsReadOnly(JsonElement input) => true;

    public bool IsConcurrencySafe(JsonElement input) => true;

    public string GetUserFacingName(JsonElement? input = null) => "Subagent";

    public string? GetActivityDescription(JsonElement? input) => "Running subagent";

    public async Task<ToolResult> ExecuteAsync(
        JsonElement input,
        ToolExecutionContext context,
        IProgress<ToolProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var parsed = JsonSerializer.Deserialize<AgentToolInput>(input);
        if (parsed == null || string.IsNullOrWhiteSpace(parsed.Prompt))
            return ToolResult.Error("prompt is required.");

        var title = BuildTitle(parsed.Prompt);
        var workItem = _taskRuntime.CreateWorkItem(
            title,
            description: parsed.Prompt,
            owner: "subagent");
        _taskRuntime.UpdateWorkItem(workItem.Id, item => item.Status = AgentWorkItemStatus.InProgress);

        if (parsed.RunInBackground)
            return LaunchInBackground(workItem, parsed, context);

        try
        {
            var result = await RunSubagentAsync(parsed, context, cancellationToken);

            _taskRuntime.UpdateWorkItem(workItem.Id, item =>
            {
                item.Status = result.Success
                    ? AgentWorkItemStatus.Completed
                    : AgentWorkItemStatus.Blocked;
            });

            if (!result.Success)
            {
                return ToolResult.Error(
                    $"Subagent {workItem.Id} failed: {result.ErrorMessage ?? "Unknown error"}");
            }

            return ToolResult.Success(FormatResult(workItem.Id, parsed, result));
        }
        catch (OperationCanceledException)
        {
            _taskRuntime.UpdateWorkItem(workItem.Id, item => item.Status = AgentWorkItemStatus.Cancelled);
            return ToolResult.Error($"Subagent {workItem.Id} was cancelled.");
        }
        catch (Exception ex)
        {
            _taskRuntime.UpdateWorkItem(workItem.Id, item => item.Status = AgentWorkItemStatus.Blocked);
            return ToolResult.Error($"Subagent {workItem.Id} failed: {ex.Message}");
        }
    }

    private ToolResult LaunchInBackground(
        AgentWorkItem workItem,
        AgentToolInput input,
        ToolExecutionContext context)
    {
        var backgroundRun = _taskRuntime.StartBackgroundRun(
            BuildTitle(input.Prompt),
            owner: "subagent");
        _taskRuntime.AppendBackgroundRunOutput(
            backgroundRun.Id,
            $"Queued prompt: {input.Prompt.Trim()}");

        _ = Task.Run(async () =>
        {
            try
            {
                var result = await RunSubagentAsync(input, context, CancellationToken.None);
                if (result.Success)
                {
                    _taskRuntime.UpdateWorkItem(workItem.Id, item =>
                        item.Status = AgentWorkItemStatus.Completed);
                    _taskRuntime.AppendBackgroundRunOutput(
                        backgroundRun.Id,
                        FormatResult(workItem.Id, input, result));
                    _taskRuntime.StopBackgroundRun(backgroundRun.Id, "completed");
                    return;
                }

                var error = result.ErrorMessage ?? "Unknown error";
                _taskRuntime.UpdateWorkItem(workItem.Id, item =>
                    item.Status = AgentWorkItemStatus.Blocked);
                _taskRuntime.AppendBackgroundRunOutput(
                    backgroundRun.Id,
                    $"Subagent {workItem.Id} failed: {error}");
                _taskRuntime.FailBackgroundRun(backgroundRun.Id, error);
            }
            catch (OperationCanceledException)
            {
                _taskRuntime.UpdateWorkItem(workItem.Id, item =>
                    item.Status = AgentWorkItemStatus.Cancelled);
                _taskRuntime.AppendBackgroundRunOutput(
                    backgroundRun.Id,
                    $"Subagent {workItem.Id} was cancelled.");
                _taskRuntime.StopBackgroundRun(backgroundRun.Id, "cancelled");
            }
            catch (Exception ex)
            {
                _taskRuntime.UpdateWorkItem(workItem.Id, item =>
                    item.Status = AgentWorkItemStatus.Blocked);
                _taskRuntime.AppendBackgroundRunOutput(
                    backgroundRun.Id,
                    $"Subagent {workItem.Id} failed: {ex.Message}");
                _taskRuntime.FailBackgroundRun(backgroundRun.Id, ex.Message);
            }
        });

        return ToolResult.Success(
            $"Subagent {workItem.Id} started in the background as {backgroundRun.Id}. " +
            $"Use AgentStatus with id=\"{backgroundRun.Id}\" to inspect progress.");
    }

    private Task<AgentExecutionResult> RunSubagentAsync(
        AgentToolInput input,
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        return _runner.RunAsync(
            new AgentExecutionRequest
            {
                Prompt = input.Prompt.Trim(),
                WorkingDirectory = context.WorkingDirectory,
                Model = context.MainLoopModel,
                Tools = BuildReadOnlyToolRegistry(context.MainLoopModel),
                PermissionContext = context.PermissionContext,
                UseIsolatedWorkspace = input.UseIsolatedWorkspace,
                SystemPromptAppendix = BuildSubagentSystemPrompt(input.SubagentType),
                Hooks = _hooks,
            },
            cancellationToken);
    }

    private ToolRegistry BuildReadOnlyToolRegistry(string model)
    {
        var registry = new ToolRegistry();
        registry.Register(new FileReadTool());
        registry.Register(new GlobTool());
        registry.Register(new GrepTool());
        registry.Register(new WebFetchTool());
        registry.Register(new WebSearchTool(_providerCapabilityRouter, () => model));
        return registry;
    }

    private static string BuildSubagentSystemPrompt(string? subagentType)
    {
        var kind = string.IsNullOrWhiteSpace(subagentType)
            ? "research"
            : subagentType.Trim();

        return $"""
            You are a subordinate ClaudeSharp agent helping another agent.

            Your job:
            - Focus only on the assigned subtask
            - Investigate efficiently and summarize the answer for the parent agent
            - Stay read-only and do not propose file edits unless the prompt explicitly asks for a recommendation
            - Do not ask the user follow-up questions; make reasonable assumptions and keep moving
            - End with a concise, high-signal summary the parent agent can reuse directly

            Subagent type hint: {kind}
            """;
    }

    private static string BuildTitle(string prompt)
    {
        var singleLine = prompt
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();

        return singleLine.Length <= 72
            ? singleLine
            : singleLine[..69] + "...";
    }

    private static string FormatResult(
        string workItemId,
        AgentToolInput input,
        AgentExecutionResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Subagent {workItemId} completed.");
        builder.AppendLine($"Turns: {result.TurnCount}");
        builder.AppendLine(
            $"Usage: in={result.Usage.InputTokens}, out={result.Usage.OutputTokens}");

        if (!string.IsNullOrWhiteSpace(input.SubagentType))
            builder.AppendLine($"Type: {input.SubagentType.Trim()}");

        builder.AppendLine();
        builder.AppendLine("Summary:");
        builder.AppendLine(result.Summary);

        return builder.ToString().TrimEnd();
    }
}

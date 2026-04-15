using System.Text.Json;
using ClaudeSharp.Core.Permissions;
using ClaudeSharp.Core.Query;
using ClaudeSharp.Core.Tools;

namespace ClaudeSharp.Tools;

/// <summary>
/// Enters plan mode so the model can inspect context and produce a plan before execution.
/// </summary>
public sealed class EnterPlanModeTool : ITool
{
    private static readonly JsonElement EmptySchema = JsonDocument.Parse("""
    {
      "type": "object",
      "properties": {},
      "additionalProperties": false
    }
    """).RootElement.Clone();

    private readonly IPlanModeController _controller;

    public EnterPlanModeTool(IPlanModeController controller)
    {
        _controller = controller;
    }

    public string Name => PlanModeToolPolicy.EnterPlanModeToolName;

    public Task<string> GetDescriptionAsync() =>
        Task.FromResult(
            "Enter planning-only mode. After this, only inspection tools remain available and you must return a structured plan instead of executing changes.");

    public JsonElement GetInputSchema() => EmptySchema;

    public Task<string> GetPromptAsync(ToolPromptContext context)
    {
        return Task.FromResult("""
            Enter planning-only mode before implementation.

            Use this when the user wants to review the approach first.
            After entering plan mode, inspect the codebase, gather evidence, and return a structured plan instead of making changes.
            """);
    }

    public Task<PermissionResult> CheckPermissionsAsync(
        JsonElement input,
        ToolExecutionContext context) =>
        Task.FromResult(PermissionResult.Allow());

    public bool IsEnabled() => !_controller.IsPlanModeActive;

    public bool IsConcurrencySafe(JsonElement input) => false;

    public string GetUserFacingName(JsonElement? input = null) => "Enter plan mode";

    public string? GetActivityDescription(JsonElement? input) => "Entering plan mode";

    public async Task<ToolResult> ExecuteAsync(
        JsonElement input,
        ToolExecutionContext context,
        IProgress<ToolProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var changed = await _controller.EnterPlanModeAsync(cancellationToken);
        var allowedTools = string.Join(", ", PlanModeToolPolicy.AllowedToolNamesInPlanMode);

        return ToolResult.Success(
            changed
                ? $"Plan mode enabled. Available tools: {allowedTools}. Inspect the codebase and return a structured plan. Only call {PlanModeToolPolicy.ExitPlanModeToolName} after the user explicitly approves the plan."
                : $"Plan mode is already active. Available tools: {allowedTools}. Keep inspecting and refine the structured plan until the user approves execution.");
    }
}

/// <summary>
/// Exits plan mode and restores the previous execution permission mode.
/// </summary>
public sealed class ExitPlanModeTool : ITool
{
    private static readonly JsonElement EmptySchema = JsonDocument.Parse("""
    {
      "type": "object",
      "properties": {},
      "additionalProperties": false
    }
    """).RootElement.Clone();

    private readonly IPlanModeController _controller;

    public ExitPlanModeTool(IPlanModeController controller)
    {
        _controller = controller;
    }

    public string Name => PlanModeToolPolicy.ExitPlanModeToolName;

    public Task<string> GetDescriptionAsync() =>
        Task.FromResult(
            "Exit planning-only mode after the user approves the plan, then continue with implementation.");

    public JsonElement GetInputSchema() => EmptySchema;

    public Task<string> GetPromptAsync(ToolPromptContext context)
    {
        return Task.FromResult("""
            Exit planning-only mode and restore execution tools.

            Only use this after the user explicitly approves the plan and asks you to start implementing it.
            """);
    }

    public Task<PermissionResult> CheckPermissionsAsync(
        JsonElement input,
        ToolExecutionContext context) =>
        Task.FromResult(PermissionResult.Allow());

    public bool IsEnabled() => _controller.IsPlanModeActive;

    public bool IsConcurrencySafe(JsonElement input) => false;

    public string GetUserFacingName(JsonElement? input = null) => "Exit plan mode";

    public string? GetActivityDescription(JsonElement? input) => "Exiting plan mode";

    public async Task<ToolResult> ExecuteAsync(
        JsonElement input,
        ToolExecutionContext context,
        IProgress<ToolProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!_controller.IsPlanModeActive)
            return ToolResult.Error("Plan mode is not active.");

        var restoredMode = await _controller.ExitPlanModeAsync(cancellationToken);
        return ToolResult.Success(
            $"Plan mode disabled. Restored permission mode: {restoredMode}. Normal execution tools are available again.");
    }
}

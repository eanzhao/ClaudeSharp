using System.Text.Json;
using System.Text.Json.Serialization;
using Aexon.Core.Agents;
using Aexon.Core.Permissions;
using Aexon.Core.Tools;

namespace Aexon.Tools;

/// <summary>
/// Represents the input payload for the AgentResume tool.
/// </summary>
public sealed class AgentResumeToolInput
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";
}

/// <summary>
/// Resumes an approved work item that is waiting for mailbox-triggered reactivation.
/// </summary>
public sealed class AgentResumeTool : ITool
{
    private readonly IAgentTaskRuntime _taskRuntime;
    private readonly IAgentMessageRuntime _messageRuntime;
    private readonly IAgentMessageActivationRuntime _activationRuntime;

    public AgentResumeTool(
        IAgentTaskRuntime taskRuntime,
        IAgentMessageRuntime messageRuntime,
        IAgentMessageActivationRuntime activationRuntime)
    {
        _taskRuntime = taskRuntime;
        _messageRuntime = messageRuntime;
        _activationRuntime = activationRuntime;
    }

    public string Name => "AgentResume";

    public Task<string> GetDescriptionAsync() =>
        Task.FromResult(
            "Resume an approved subagent work item that is waiting to continue after a mailbox approval.");

    public JsonElement GetInputSchema()
    {
        return JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "id": {
              "type": "string",
              "description": "The work-item id to resume"
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
            Resume an approved agent work item.

            Use this when AgentStatus shows a work item in AwaitingResume and you want to continue it now.
            Pass the work-item id, not the background-run id.
            """);
    }

    public Task<ValidationResult> ValidateInputAsync(JsonElement input, ToolExecutionContext context)
    {
        var parsed = JsonSerializer.Deserialize<AgentResumeToolInput>(input);
        if (parsed == null || string.IsNullOrWhiteSpace(parsed.Id))
            return Task.FromResult(ValidationResult.Invalid("id is required."));

        return Task.FromResult(ValidationResult.Valid());
    }

    public Task<PermissionResult> CheckPermissionsAsync(JsonElement input, ToolExecutionContext context) =>
        Task.FromResult(PermissionResult.Allow());

    public bool IsReadOnly(JsonElement input) => false;

    public bool IsConcurrencySafe(JsonElement input) => false;

    public bool IsEnabled() => false;

    public string GetUserFacingName(JsonElement? input = null) => "Resume subagent";

    public string? GetActivityDescription(JsonElement? input) => "Resuming subagent";

    public async Task<ToolResult> ExecuteAsync(
        JsonElement input,
        ToolExecutionContext context,
        IProgress<ToolProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var parsed = JsonSerializer.Deserialize<AgentResumeToolInput>(input);
        if (parsed == null || string.IsNullOrWhiteSpace(parsed.Id))
            return ToolResult.Error("id is required.");

        var result = await AgentWorkItemResumer.TryResumeAsync(
            _taskRuntime,
            _messageRuntime,
            _activationRuntime,
            parsed.Id,
            cancellationToken);

        var message = AgentWorkItemResumeFormatter.Format(result);
        return result.Status is AgentWorkItemResumeStatus.Resumed or AgentWorkItemResumeStatus.AlreadyActive
            ? ToolResult.Success(message)
            : ToolResult.Error(message);
    }
}

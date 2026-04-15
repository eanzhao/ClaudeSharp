using Aexon.Core.Context;
using Aexon.Core.Hooks;
using Aexon.Core.Messages;
using Aexon.Core.Permissions;
using Aexon.Core.Tools;

namespace Aexon.Core.Agents;

/// <summary>
/// Represents a subagent execution request.
/// </summary>
public sealed record AgentExecutionRequest
{
    public required string Prompt { get; init; }
    public required string WorkingDirectory { get; init; }
    public required string Model { get; init; }
    public required ToolRegistry Tools { get; init; }
    public required PermissionContext PermissionContext { get; init; }
    public bool UseIsolatedWorkspace { get; init; } = true;
    public IProgress<AgentExecutionProgress>? Progress { get; init; }
    public string? MemoryContent { get; init; }
    public string? SessionMemoryContent { get; init; }
    public string? SystemPromptAppendix { get; init; }
    public IHookRuntime? Hooks { get; init; }
}

/// <summary>
/// Represents the normalized result of a subagent execution.
/// </summary>
public sealed record AgentExecutionResult(
    string Summary,
    bool Success,
    TokenUsage Usage,
    int TurnCount,
    string? ErrorMessage = null);

/// <summary>
/// Represents a normalized progress update emitted by a subagent execution.
/// </summary>
public sealed record AgentExecutionProgress(
    string Type,
    string Message,
    string? ToolName = null,
    string? ToolUseId = null,
    bool IsError = false);

/// <summary>
/// Defines the contract for running subagent requests.
/// </summary>
public interface IAgentExecutionRunner
{
    Task<AgentExecutionResult> RunAsync(
        AgentExecutionRequest request,
        CancellationToken cancellationToken = default);
}

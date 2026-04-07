using ClaudeSharp.Core.Context;
using ClaudeSharp.Core.Hooks;
using ClaudeSharp.Core.Messages;
using ClaudeSharp.Core.Permissions;
using ClaudeSharp.Core.Tools;

namespace ClaudeSharp.Core.Agents;

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
/// Defines the contract for running subagent requests.
/// </summary>
public interface IAgentExecutionRunner
{
    Task<AgentExecutionResult> RunAsync(
        AgentExecutionRequest request,
        CancellationToken cancellationToken = default);
}

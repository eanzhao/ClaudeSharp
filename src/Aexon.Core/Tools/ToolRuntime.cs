using System.Text.Json;
using Aexon.Core.Messages;

namespace Aexon.Core.Tools;

/// <summary>
/// Defines the contract for tool runtime.
/// </summary>
public interface IToolRuntime
{
    IAsyncEnumerable<ToolRunUpdate> RunBatchAsync(
        IReadOnlyList<ToolUseBlock> invocations,
        ToolExecutionContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents tool run update.
/// </summary>
public abstract record ToolRunUpdate;

/// <summary>
/// Represents tool permission request update.
/// </summary>
public sealed record ToolPermissionRequestUpdate : ToolRunUpdate
{
    public required ToolUseBlock Invocation { get; init; }
    public required ITool Tool { get; init; }
    public required string Description { get; init; }
    public required JsonElement ObservedInput { get; init; }

    private readonly TaskCompletionSource<bool> _response =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void SetResponse(bool approved) => _response.TrySetResult(approved);

    public Task<bool> WaitForResponseAsync() => _response.Task;
}

/// <summary>
/// Represents tool progress update.
/// </summary>
public sealed record ToolProgressUpdate(
    string ToolUseId,
    string ToolName,
    ToolProgress Progress) : ToolRunUpdate;

/// <summary>
/// Represents tool completed update.
/// </summary>
public sealed record ToolCompletedUpdate(ToolRunOutcome Outcome) : ToolRunUpdate;

/// <summary>
/// Represents tool run outcome.
/// </summary>
public sealed record ToolRunOutcome(
    ToolUseBlock Invocation,
    ITool Tool,
    ToolResult Result);

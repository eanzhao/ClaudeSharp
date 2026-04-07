using System.Text.Json;
using ClaudeSharp.Core.Messages;

namespace ClaudeSharp.Core.Tools;

/// <summary>
/// 工具运行时接口。
/// 把 QueryEngine 里的工具调度逻辑拆出来，后面不管接流式 API 还是 headless host，
/// 都可以复用同一套 batch 执行协议。
/// </summary>
public interface IToolRuntime
{
    IAsyncEnumerable<ToolRunUpdate> RunBatchAsync(
        IReadOnlyList<ToolUseBlock> invocations,
        ToolExecutionContext context,
        CancellationToken cancellationToken = default);
}

public abstract record ToolRunUpdate;

/// <summary>
/// 运行时向宿主请求权限确认。
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

public sealed record ToolProgressUpdate(
    string ToolUseId,
    string ToolName,
    ToolProgress Progress) : ToolRunUpdate;

public sealed record ToolCompletedUpdate(ToolRunOutcome Outcome) : ToolRunUpdate;

public sealed record ToolRunOutcome(
    ToolUseBlock Invocation,
    ITool Tool,
    ToolResult Result);

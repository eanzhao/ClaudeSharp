namespace Aexon.Core.Tools;

/// <summary>
/// Controls how a batch of tool-use invocations is executed.
/// </summary>
public enum ToolBatchExecutionMode
{
    /// <summary>
    /// Each tool's <see cref="ITool.IsConcurrencySafe"/> decides whether it
    /// runs in the concurrent or sequential phase. This is the default.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// Forces every tool in the batch to run sequentially regardless of its
    /// concurrency-safety declaration. Useful for debugging and reproducible
    /// ordering.
    /// </summary>
    Sequential = 1,

    /// <summary>
    /// Forces every tool in the batch to run in parallel regardless of its
    /// concurrency-safety declaration. The caller accepts responsibility for
    /// any ordering or filesystem-interleaving hazards this may expose.
    /// </summary>
    Parallel = 2,
}

namespace Aexon.Core.Agents;

/// <summary>
/// Defines background-run wait outcomes.
/// </summary>
public enum AgentBackgroundRunWaitOutcome
{
    Completed,
    TimedOut,
    NotFound,
}

/// <summary>
/// Defines how a batch wait should complete.
/// </summary>
public enum AgentBackgroundRunWaitMode
{
    All,
    Any,
}

/// <summary>
/// Captures the current state of a background run during a wait operation.
/// </summary>
public sealed record AgentBackgroundRunWaitSnapshot(
    string BackgroundRunId,
    AgentBackgroundRun? Run);

/// <summary>
/// Represents a background-run wait result.
/// </summary>
public sealed record AgentBackgroundRunWaitResult(
    AgentBackgroundRunWaitOutcome Outcome,
    string BackgroundRunId,
    AgentBackgroundRun? Run,
    TimeSpan Elapsed);

/// <summary>
/// Represents the result of waiting on multiple background runs.
/// </summary>
public sealed record AgentBackgroundRunWaitBatchResult(
    AgentBackgroundRunWaitOutcome Outcome,
    AgentBackgroundRunWaitMode Mode,
    IReadOnlyList<AgentBackgroundRunWaitSnapshot> CompletedRuns,
    IReadOnlyList<AgentBackgroundRunWaitSnapshot> PendingRuns,
    IReadOnlyList<string> MissingRunIds,
    TimeSpan Elapsed);

/// <summary>
/// Waits for a background run to reach a terminal state.
/// </summary>
public static class AgentBackgroundRunWaiter
{
    public static async Task<AgentBackgroundRunWaitResult> WaitAsync(
        IAgentTaskRuntime runtime,
        string backgroundRunId,
        TimeSpan pollInterval,
        TimeSpan? timeout = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentException.ThrowIfNullOrWhiteSpace(backgroundRunId);

        var normalizedId = backgroundRunId.Trim();
        var batchResult = await WaitManyAsync(
            runtime,
            [normalizedId],
            AgentBackgroundRunWaitMode.All,
            pollInterval,
            timeout,
            delayAsync,
            cancellationToken);

        var snapshot = batchResult.CompletedRuns.FirstOrDefault() ??
            batchResult.PendingRuns.FirstOrDefault();

        return new AgentBackgroundRunWaitResult(
            batchResult.Outcome,
            normalizedId,
            snapshot?.Run,
            batchResult.Elapsed);
    }

    public static async Task<AgentBackgroundRunWaitBatchResult> WaitManyAsync(
        IAgentTaskRuntime runtime,
        IEnumerable<string> backgroundRunIds,
        AgentBackgroundRunWaitMode mode,
        TimeSpan pollInterval,
        TimeSpan? timeout = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(backgroundRunIds);

        var normalizedIds = NormalizeIds(backgroundRunIds);
        if (normalizedIds.Count == 0)
            throw new ArgumentException("At least one background-run id is required.", nameof(backgroundRunIds));

        var normalizedPollInterval = pollInterval <= TimeSpan.Zero
            ? TimeSpan.FromMilliseconds(500)
            : pollInterval;
        var startedAt = DateTimeOffset.UtcNow;
        delayAsync ??= static (delay, token) => Task.Delay(delay, token);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var completedRuns = new List<AgentBackgroundRunWaitSnapshot>();
            var pendingRuns = new List<AgentBackgroundRunWaitSnapshot>();
            var missingRunIds = new List<string>();

            foreach (var runId in normalizedIds)
            {
                var run = runtime.GetBackgroundRun(runId);
                if (run == null)
                {
                    missingRunIds.Add(runId);
                    continue;
                }

                var snapshot = new AgentBackgroundRunWaitSnapshot(runId, run);
                if (IsTerminal(run.Status))
                    completedRuns.Add(snapshot);
                else
                    pendingRuns.Add(snapshot);
            }

            var elapsed = DateTimeOffset.UtcNow - startedAt;
            if (missingRunIds.Count > 0)
            {
                return new AgentBackgroundRunWaitBatchResult(
                    AgentBackgroundRunWaitOutcome.NotFound,
                    mode,
                    completedRuns,
                    pendingRuns,
                    missingRunIds,
                    elapsed);
            }

            if ((mode == AgentBackgroundRunWaitMode.Any && completedRuns.Count > 0) ||
                (mode == AgentBackgroundRunWaitMode.All && pendingRuns.Count == 0))
            {
                return new AgentBackgroundRunWaitBatchResult(
                    AgentBackgroundRunWaitOutcome.Completed,
                    mode,
                    completedRuns,
                    pendingRuns,
                    [],
                    elapsed);
            }

            if (timeout is { } timeoutValue && elapsed >= timeoutValue)
            {
                return new AgentBackgroundRunWaitBatchResult(
                    AgentBackgroundRunWaitOutcome.TimedOut,
                    mode,
                    completedRuns,
                    pendingRuns,
                    [],
                    elapsed);
            }

            var delay = normalizedPollInterval;
            if (timeout is { } boundedTimeout)
            {
                var remaining = boundedTimeout - elapsed;
                if (remaining <= TimeSpan.Zero)
                    remaining = TimeSpan.FromMilliseconds(1);

                delay = delay <= remaining ? delay : remaining;
            }

            await delayAsync(delay, cancellationToken);
        }
    }

    public static bool IsTerminal(AgentBackgroundRunStatus status) =>
        status is AgentBackgroundRunStatus.Stopped or
            AgentBackgroundRunStatus.Failed or
            AgentBackgroundRunStatus.Cancelled;

    private static List<string> NormalizeIds(IEnumerable<string> backgroundRunIds)
    {
        var results = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var runId in backgroundRunIds)
        {
            if (string.IsNullOrWhiteSpace(runId))
                continue;

            var normalizedId = runId.Trim();
            if (seen.Add(normalizedId))
                results.Add(normalizedId);
        }

        return results;
    }
}

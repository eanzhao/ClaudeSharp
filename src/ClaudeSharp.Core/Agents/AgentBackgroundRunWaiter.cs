namespace ClaudeSharp.Core.Agents;

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
/// Represents a background-run wait result.
/// </summary>
public sealed record AgentBackgroundRunWaitResult(
    AgentBackgroundRunWaitOutcome Outcome,
    string BackgroundRunId,
    AgentBackgroundRun? Run,
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
        var normalizedPollInterval = pollInterval <= TimeSpan.Zero
            ? TimeSpan.FromMilliseconds(500)
            : pollInterval;
        var startedAt = DateTimeOffset.UtcNow;
        delayAsync ??= static (delay, token) => Task.Delay(delay, token);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var run = runtime.GetBackgroundRun(normalizedId);
            var elapsed = DateTimeOffset.UtcNow - startedAt;
            if (run == null)
            {
                return new AgentBackgroundRunWaitResult(
                    AgentBackgroundRunWaitOutcome.NotFound,
                    normalizedId,
                    null,
                    elapsed);
            }

            if (IsTerminal(run.Status))
            {
                return new AgentBackgroundRunWaitResult(
                    AgentBackgroundRunWaitOutcome.Completed,
                    normalizedId,
                    run,
                    elapsed);
            }

            if (timeout is { } timeoutValue && elapsed >= timeoutValue)
            {
                return new AgentBackgroundRunWaitResult(
                    AgentBackgroundRunWaitOutcome.TimedOut,
                    normalizedId,
                    run,
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
}

namespace ClaudeSharp.Core.Agents;

/// <summary>
/// Schedules background subagent runs with a bounded concurrency limit.
/// </summary>
public sealed class BackgroundAgentRunScheduler
{
    private readonly object _gate = new();
    private readonly LinkedList<ScheduledRun> _pendingRuns = [];
    private readonly Dictionary<string, LinkedListNode<ScheduledRun>> _pendingRunNodes =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ScheduledRun> _runningRuns =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly int _maxConcurrency;

    /// <summary>
    /// Initializes a new instance of the <see cref="BackgroundAgentRunScheduler"/> class.
    /// </summary>
    /// <param name="maxConcurrency">Maximum number of runs that can execute at the same time.</param>
    public BackgroundAgentRunScheduler(int maxConcurrency = 1)
    {
        _maxConcurrency = Math.Max(1, maxConcurrency);
    }

    /// <summary>
    /// Queues a background run and returns a cancellation callback for it.
    /// </summary>
    public Action Enqueue(
        string runId,
        Func<CancellationToken, Task> executeAsync,
        Action? onStarted = null,
        Action? onCancelledWhileQueued = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentNullException.ThrowIfNull(executeAsync);

        var scheduledRun = new ScheduledRun(
            runId.Trim(),
            executeAsync,
            onStarted,
            onCancelledWhileQueued);

        lock (_gate)
        {
            if (_runningRuns.Count < _maxConcurrency)
            {
                StartRunNoLock(scheduledRun);
            }
            else
            {
                var node = _pendingRuns.AddLast(scheduledRun);
                _pendingRunNodes[scheduledRun.RunId] = node;
            }
        }

        return () => Cancel(runId);
    }

    private void Cancel(string runId)
    {
        ScheduledRun? queuedRun = null;
        ScheduledRun? runningRun = null;

        lock (_gate)
        {
            if (_pendingRunNodes.TryGetValue(runId, out var queuedNode))
            {
                _pendingRunNodes.Remove(runId);
                _pendingRuns.Remove(queuedNode);
                queuedRun = queuedNode.Value;
            }
            else if (_runningRuns.TryGetValue(runId, out var activeRun))
            {
                runningRun = activeRun;
            }
        }

        if (queuedRun != null)
        {
            try
            {
                queuedRun.CancellationSource.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The queued run was torn down while the cancellation was in flight.
            }

            try
            {
                queuedRun.OnCancelledWhileQueued?.Invoke();
            }
            finally
            {
                queuedRun.CancellationSource.Dispose();
            }

            return;
        }

        if (runningRun == null)
            return;

        try
        {
            runningRun.CancellationSource.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The background run finished while the cancellation was in flight.
        }
    }

    private void StartRunNoLock(ScheduledRun scheduledRun)
    {
        _runningRuns[scheduledRun.RunId] = scheduledRun;
        scheduledRun.OnStarted?.Invoke();
        _ = Task.Run(() => ExecuteRunAsync(scheduledRun));
    }

    private async Task ExecuteRunAsync(ScheduledRun scheduledRun)
    {
        try
        {
            await scheduledRun.ExecuteAsync(scheduledRun.CancellationSource.Token);
        }
        catch (OperationCanceledException) when (scheduledRun.CancellationSource.IsCancellationRequested)
        {
            // The caller owns the terminal cancellation state transition.
        }
        catch
        {
            // The caller owns failure reporting. The scheduler only keeps the queue moving.
        }
        finally
        {
            scheduledRun.CancellationSource.Dispose();
            StartNextQueuedRun(scheduledRun.RunId);
        }
    }

    private void StartNextQueuedRun(string completedRunId)
    {
        lock (_gate)
        {
            _runningRuns.Remove(completedRunId);

            if (_pendingRuns.First is null)
                return;

            var nextNode = _pendingRuns.First;
            if (nextNode == null)
                return;

            _pendingRuns.RemoveFirst();
            _pendingRunNodes.Remove(nextNode.Value.RunId);
            StartRunNoLock(nextNode.Value);
        }
    }

    private sealed class ScheduledRun
    {
        public ScheduledRun(
            string runId,
            Func<CancellationToken, Task> executeAsync,
            Action? onStarted,
            Action? onCancelledWhileQueued)
        {
            RunId = runId;
            ExecuteAsync = executeAsync;
            OnStarted = onStarted;
            OnCancelledWhileQueued = onCancelledWhileQueued;
        }

        public string RunId { get; }

        public CancellationTokenSource CancellationSource { get; } = new();

        public Func<CancellationToken, Task> ExecuteAsync { get; }

        public Action? OnStarted { get; }

        public Action? OnCancelledWhileQueued { get; }
    }
}

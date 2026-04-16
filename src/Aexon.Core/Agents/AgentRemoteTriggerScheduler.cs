namespace Aexon.Core.Agents;

public sealed class AgentRemoteTriggerScheduler : IAsyncDisposable
{
    private readonly IAgentRemoteTriggerRuntime _runtime;
    private readonly TimeSpan _pollInterval;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;

    public AgentRemoteTriggerScheduler(
        IAgentRemoteTriggerRuntime runtime,
        TimeSpan? pollInterval = null)
    {
        _runtime = runtime;
        _pollInterval = pollInterval.GetValueOrDefault(TimeSpan.FromSeconds(30));
        if (_pollInterval <= TimeSpan.Zero)
            _pollInterval = TimeSpan.FromSeconds(30);
        _loop = RunAsync(_cts.Token);
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        try
        {
            await _loop;
        }
        catch (OperationCanceledException)
        {
        }

        _cts.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_pollInterval, cancellationToken);
                Tick(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Scheduler failures should not kill the loop.
            }
        }
    }

    private void Tick(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var triggers = _runtime.ListTriggers()
            .Where(trigger =>
                trigger.Enabled &&
                trigger.Kind == AgentRemoteTriggerKind.Schedule &&
                trigger.NextTriggerAt is { } next &&
                next <= now)
            .ToArray();

        foreach (var trigger in triggers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _runtime.FireTrigger(
                trigger.Id,
                new AgentRemoteTriggerFireRequest(
                    AgentRemoteTriggerFireSource.Schedule,
                    FiredAt: now));
        }
    }
}

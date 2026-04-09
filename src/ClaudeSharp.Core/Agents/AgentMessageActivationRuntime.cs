namespace ClaudeSharp.Core.Agents;

/// <summary>
/// Defines mailbox-driven agent activation outcomes.
/// </summary>
public enum AgentMessageActivationStatus
{
    Reactivated,
    AlreadyActive,
    NotRegistered,
    Failed,
}

/// <summary>
/// Represents the result of trying to reactivate an agent from mailbox traffic.
/// </summary>
public sealed record AgentMessageActivationResult
{
    public required string Owner { get; init; }
    public AgentMessageActivationStatus Status { get; init; }
    public string? BackgroundRunId { get; init; }
    public string? WorkItemId { get; init; }
    public string? Message { get; init; }

    public static AgentMessageActivationResult Reactivated(
        string owner,
        string backgroundRunId,
        string workItemId,
        string? message = null) =>
        new()
        {
            Owner = owner,
            Status = AgentMessageActivationStatus.Reactivated,
            BackgroundRunId = backgroundRunId,
            WorkItemId = workItemId,
            Message = message,
        };

    public static AgentMessageActivationResult AlreadyActive(
        string owner,
        string? message = null) =>
        new()
        {
            Owner = owner,
            Status = AgentMessageActivationStatus.AlreadyActive,
            Message = message,
        };

    public static AgentMessageActivationResult NotRegistered(string owner) =>
        new()
        {
            Owner = owner,
            Status = AgentMessageActivationStatus.NotRegistered,
        };

    public static AgentMessageActivationResult Failed(
        string owner,
        string message) =>
        new()
        {
            Owner = owner,
            Status = AgentMessageActivationStatus.Failed,
            Message = message,
        };
}

/// <summary>
/// Defines the contract for mailbox-triggered agent activation.
/// </summary>
public interface IAgentMessageActivationRuntime
{
    void RegisterOwner(
        string owner,
        Func<string?, CancellationToken, Task<AgentMessageActivationResult>> activateAsync);

    Task<AgentMessageActivationResult> TryActivateAsync(
        string owner,
        string? reason = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Stores mailbox activation registrations in memory.
/// </summary>
public sealed class InMemoryAgentMessageActivationRuntime : IAgentMessageActivationRuntime
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Func<string?, CancellationToken, Task<AgentMessageActivationResult>>> _activators =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task<AgentMessageActivationResult>> _inFlightActivations =
        new(StringComparer.OrdinalIgnoreCase);

    public void RegisterOwner(
        string owner,
        Func<string?, CancellationToken, Task<AgentMessageActivationResult>> activateAsync)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentNullException.ThrowIfNull(activateAsync);

        lock (_gate)
            _activators[owner.Trim()] = activateAsync;
    }

    public Task<AgentMessageActivationResult> TryActivateAsync(
        string owner,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(owner))
            return Task.FromResult(AgentMessageActivationResult.NotRegistered(string.Empty));

        var normalizedOwner = owner.Trim();
        lock (_gate)
        {
            if (!_activators.TryGetValue(normalizedOwner, out var activateAsync))
                return Task.FromResult(AgentMessageActivationResult.NotRegistered(normalizedOwner));

            if (_inFlightActivations.TryGetValue(normalizedOwner, out var inFlight))
                return inFlight;

            var activationTask = ExecuteActivationAsync(
                normalizedOwner,
                activateAsync,
                reason,
                cancellationToken);
            _inFlightActivations[normalizedOwner] = activationTask;
            return activationTask;
        }
    }

    private async Task<AgentMessageActivationResult> ExecuteActivationAsync(
        string owner,
        Func<string?, CancellationToken, Task<AgentMessageActivationResult>> activateAsync,
        string? reason,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await activateAsync(reason, cancellationToken);
            return string.IsNullOrWhiteSpace(result.Owner)
                ? result with { Owner = owner }
                : result;
        }
        catch (Exception ex)
        {
            return AgentMessageActivationResult.Failed(owner, ex.Message);
        }
        finally
        {
            lock (_gate)
                _inFlightActivations.Remove(owner);
        }
    }
}

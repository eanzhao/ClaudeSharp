namespace Aexon.Core.Agents;

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
/// Represents a mailbox-triggered activation request.
/// </summary>
public sealed record AgentMessageActivationRequest
{
    public required AgentMessage Message { get; init; }
    public string Owner => Message.To;
    public string? ResumeReason => Message.Protocol?.ResumeReason;
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
        Func<AgentMessageActivationRequest, CancellationToken, Task<AgentMessageActivationResult>> activateAsync);

    Task<AgentMessageActivationResult> TryActivateAsync(
        AgentMessage message,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Stores mailbox activation registrations in memory.
/// </summary>
public sealed class InMemoryAgentMessageActivationRuntime : IAgentMessageActivationRuntime
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Func<AgentMessageActivationRequest, CancellationToken, Task<AgentMessageActivationResult>>> _activators =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task<AgentMessageActivationResult>> _inFlightActivations =
        new(StringComparer.OrdinalIgnoreCase);

    public void RegisterOwner(
        string owner,
        Func<AgentMessageActivationRequest, CancellationToken, Task<AgentMessageActivationResult>> activateAsync)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentNullException.ThrowIfNull(activateAsync);

        lock (_gate)
            _activators[owner.Trim()] = activateAsync;
    }

    public Task<AgentMessageActivationResult> TryActivateAsync(
        AgentMessage message,
        CancellationToken cancellationToken = default)
    {
        if (message == null || string.IsNullOrWhiteSpace(message.To))
            return Task.FromResult(AgentMessageActivationResult.NotRegistered(string.Empty));

        var request = new AgentMessageActivationRequest
        {
            Message = message.Clone(),
        };
        var normalizedOwner = request.Owner.Trim();
        lock (_gate)
        {
            if (!_activators.TryGetValue(normalizedOwner, out var activateAsync))
                return Task.FromResult(AgentMessageActivationResult.NotRegistered(normalizedOwner));

            if (_inFlightActivations.TryGetValue(normalizedOwner, out var inFlight))
            {
                if (!inFlight.IsCompleted)
                    return inFlight;

                _inFlightActivations.Remove(normalizedOwner);
            }

            var activationTask = ExecuteActivationAsync(
                normalizedOwner,
                activateAsync,
                request,
                cancellationToken);
            if (!activationTask.IsCompleted)
                _inFlightActivations[normalizedOwner] = activationTask;
            return activationTask;
        }
    }

    private async Task<AgentMessageActivationResult> ExecuteActivationAsync(
        string owner,
        Func<AgentMessageActivationRequest, CancellationToken, Task<AgentMessageActivationResult>> activateAsync,
        AgentMessageActivationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await activateAsync(request, cancellationToken);
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

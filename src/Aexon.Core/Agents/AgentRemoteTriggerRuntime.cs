using Aexon.Core.Cron;

namespace Aexon.Core.Agents;

public enum AgentRemoteTriggerKind
{
    Webhook,
    Schedule,
}

public enum AgentRemoteTriggerFireSource
{
    Manual,
    Webhook,
    Schedule,
}

public enum AgentRemoteTriggerFireStatus
{
    Fired,
    NotFound,
    Disabled,
    SecretMismatch,
    TaskNotFound,
}

public sealed class AgentRemoteTrigger
{
    public required string Id { get; init; }
    public required string WorkItemId { get; set; }
    public AgentRemoteTriggerKind Kind { get; set; }
    public string? Description { get; set; }
    public string? Schedule { get; set; }
    public string? Secret { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastTriggeredAt { get; set; }
    public DateTimeOffset? NextTriggerAt { get; set; }

    public AgentRemoteTrigger Clone() =>
        new()
        {
            Id = Id,
            WorkItemId = WorkItemId,
            Kind = Kind,
            Description = Description,
            Schedule = Schedule,
            Secret = Secret,
            Enabled = Enabled,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            LastTriggeredAt = LastTriggeredAt,
            NextTriggerAt = NextTriggerAt,
        };
}

public sealed record AgentRemoteTriggerFireRequest(
    AgentRemoteTriggerFireSource Source,
    string? Secret = null,
    string? Payload = null,
    DateTimeOffset? FiredAt = null);

public sealed record AgentRemoteTriggerFireResult(
    AgentRemoteTriggerFireStatus Status,
    AgentRemoteTrigger? Trigger,
    AgentBackgroundRun? BackgroundRun,
    string Message);

public interface IAgentRemoteTriggerRuntime
{
    AgentRemoteTrigger CreateTrigger(
        string? id,
        string workItemId,
        AgentRemoteTriggerKind kind,
        string? description = null,
        string? schedule = null,
        string? secret = null);

    AgentRemoteTrigger? GetTrigger(string id);

    IReadOnlyList<AgentRemoteTrigger> ListTriggers(string? workItemId = null);

    bool DeleteTrigger(string id);

    AgentRemoteTriggerFireResult FireTrigger(
        string id,
        AgentRemoteTriggerFireRequest request);
}

public sealed class InMemoryAgentRemoteTriggerRuntime : IAgentRemoteTriggerRuntime
{
    private readonly object _gate = new();
    private readonly IAgentTaskRuntime _taskRuntime;
    private readonly Dictionary<string, AgentRemoteTrigger> _triggers =
        new(StringComparer.OrdinalIgnoreCase);
    private int _sequence;

    public InMemoryAgentRemoteTriggerRuntime(
        IAgentTaskRuntime taskRuntime,
        IEnumerable<AgentRemoteTrigger>? triggers = null)
    {
        _taskRuntime = taskRuntime;

        if (triggers == null)
            return;

        foreach (var trigger in triggers)
        {
            _triggers[trigger.Id] = trigger.Clone();
            _sequence = Math.Max(_sequence, ParseSequence(trigger.Id));
        }
    }

    public AgentRemoteTrigger CreateTrigger(
        string? id,
        string workItemId,
        AgentRemoteTriggerKind kind,
        string? description = null,
        string? schedule = null,
        string? secret = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workItemId);
        var normalizedWorkItemId = workItemId.Trim();
        if (_taskRuntime.GetWorkItem(normalizedWorkItemId) == null)
            throw new InvalidOperationException($"Task '{normalizedWorkItemId}' does not exist.");

        var normalizedId = string.IsNullOrWhiteSpace(id)
            ? $"trigger-{Interlocked.Increment(ref _sequence)}"
            : id.Trim();

        if (kind == AgentRemoteTriggerKind.Schedule &&
            CronExpression.TryParse(schedule ?? string.Empty) == null)
        {
            throw new InvalidOperationException($"Invalid cron expression: {schedule}");
        }

        var now = DateTimeOffset.UtcNow;
        var trigger = new AgentRemoteTrigger
        {
            Id = normalizedId,
            WorkItemId = normalizedWorkItemId,
            Kind = kind,
            Description = Normalize(description),
            Schedule = kind == AgentRemoteTriggerKind.Schedule
                ? schedule?.Trim()
                : null,
            Secret = kind == AgentRemoteTriggerKind.Webhook
                ? Normalize(secret) ?? $"rtg-{Guid.NewGuid():N}"[..12]
                : null,
            CreatedAt = now,
            UpdatedAt = now,
            NextTriggerAt = kind == AgentRemoteTriggerKind.Schedule
                ? CronExpression.TryParse(schedule!.Trim())?.NextOccurrence(now)
                : null,
        };

        lock (_gate)
        {
            if (_triggers.ContainsKey(trigger.Id))
                throw new InvalidOperationException($"Remote trigger '{trigger.Id}' already exists.");

            _triggers[trigger.Id] = trigger;
        }

        return trigger.Clone();
    }

    public AgentRemoteTrigger? GetTrigger(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        lock (_gate)
            return _triggers.TryGetValue(id.Trim(), out var trigger) ? trigger.Clone() : null;
    }

    public IReadOnlyList<AgentRemoteTrigger> ListTriggers(string? workItemId = null)
    {
        lock (_gate)
        {
            return _triggers.Values
                .Where(trigger =>
                    string.IsNullOrWhiteSpace(workItemId) ||
                    string.Equals(trigger.WorkItemId, workItemId.Trim(), StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(trigger => trigger.CreatedAt)
                .ThenBy(trigger => trigger.Id, StringComparer.OrdinalIgnoreCase)
                .Select(trigger => trigger.Clone())
                .ToArray();
        }
    }

    public bool DeleteTrigger(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return false;

        lock (_gate)
            return _triggers.Remove(id.Trim());
    }

    public AgentRemoteTriggerFireResult FireTrigger(
        string id,
        AgentRemoteTriggerFireRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(request);

        AgentRemoteTrigger? trigger;
        lock (_gate)
            _triggers.TryGetValue(id.Trim(), out trigger);

        if (trigger == null)
        {
            return new AgentRemoteTriggerFireResult(
                AgentRemoteTriggerFireStatus.NotFound,
                null,
                null,
                $"No remote trigger matched id '{id.Trim()}'.");
        }

        if (!trigger.Enabled)
        {
            return new AgentRemoteTriggerFireResult(
                AgentRemoteTriggerFireStatus.Disabled,
                trigger.Clone(),
                null,
                $"Remote trigger '{trigger.Id}' is disabled.");
        }

        if (trigger.Kind == AgentRemoteTriggerKind.Webhook &&
            !string.IsNullOrWhiteSpace(trigger.Secret) &&
            !string.Equals(trigger.Secret, request.Secret, StringComparison.Ordinal))
        {
            return new AgentRemoteTriggerFireResult(
                AgentRemoteTriggerFireStatus.SecretMismatch,
                trigger.Clone(),
                null,
                $"Webhook secret did not match trigger '{trigger.Id}'.");
        }

        var workItem = _taskRuntime.GetWorkItem(trigger.WorkItemId);
        if (workItem == null)
        {
            return new AgentRemoteTriggerFireResult(
                AgentRemoteTriggerFireStatus.TaskNotFound,
                trigger.Clone(),
                null,
                $"Task '{trigger.WorkItemId}' linked to trigger '{trigger.Id}' no longer exists.");
        }

        var firedAt = request.FiredAt ?? DateTimeOffset.UtcNow;
        var backgroundRun = _taskRuntime.StartBackgroundRun(
            $"{trigger.Kind.ToString().ToLowerInvariant()} trigger {trigger.Id}",
            owner: workItem.Owner,
            workItemId: workItem.Id,
            initialStatus: AgentBackgroundRunStatus.Running);
        _taskRuntime.AppendBackgroundRunOutput(
            backgroundRun.Id,
            $"[trigger] {trigger.Id} fired via {request.Source.ToString().ToLowerInvariant()} at {firedAt:O}");
        if (!string.IsNullOrWhiteSpace(request.Payload))
        {
            _taskRuntime.AppendBackgroundRunOutput(
                backgroundRun.Id,
                request.Payload.Trim());
        }

        _taskRuntime.StopBackgroundRun(backgroundRun.Id, "trigger recorded");

        lock (_gate)
        {
            if (_triggers.TryGetValue(trigger.Id, out var stored))
            {
                stored.LastTriggeredAt = firedAt;
                stored.UpdatedAt = firedAt;
                if (stored.Kind == AgentRemoteTriggerKind.Schedule &&
                    !string.IsNullOrWhiteSpace(stored.Schedule))
                {
                    stored.NextTriggerAt = CronExpression
                        .TryParse(stored.Schedule)?
                        .NextOccurrence(firedAt);
                }

                trigger = stored.Clone();
            }
        }

        return new AgentRemoteTriggerFireResult(
            AgentRemoteTriggerFireStatus.Fired,
            trigger,
            _taskRuntime.GetBackgroundRun(backgroundRun.Id),
            $"Trigger '{trigger!.Id}' fired and recorded output to {backgroundRun.Id}.");
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int ParseSequence(string id)
    {
        if (!id.StartsWith("trigger-", StringComparison.OrdinalIgnoreCase))
            return 0;

        return int.TryParse(id["trigger-".Length..], out var value)
            ? value
            : 0;
    }
}

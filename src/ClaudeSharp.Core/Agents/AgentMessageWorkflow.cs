namespace ClaudeSharp.Core.Agents;

/// <summary>
/// Defines the supported mailbox action workflow types.
/// </summary>
public enum AgentMessageActionType
{
    PlanApproval,
    Shutdown,
    FollowUp,
}

/// <summary>
/// Represents an actionable mailbox item waiting for a response.
/// </summary>
public sealed record AgentMessageActionItem
{
    public required string Participant { get; init; }
    public required AgentMessage TriggerMessage { get; init; }
    public required AgentMessageActionType ActionType { get; init; }
    public required IReadOnlyList<string> Decisions { get; init; }
    public AgentMessage? ResolutionMessage { get; init; }
    public bool IsResolved => ResolutionMessage != null;
}

/// <summary>
/// Represents a normalized mailbox response to send.
/// </summary>
public sealed record AgentMessageResponseSpec
{
    public required string From { get; init; }
    public required string To { get; init; }
    public required AgentMessageKind Kind { get; init; }
    public required string Body { get; init; }
    public required string RelatedMessageId { get; init; }
    public string? Subject { get; init; }
    public AgentMessageProtocol? Protocol { get; init; }
}

/// <summary>
/// Provides helpers for mailbox action workflows such as approvals and shutdowns.
/// </summary>
public static class AgentMessageWorkflow
{
    public static IReadOnlyList<AgentMessageActionItem> ListPendingActions(
        IAgentMessageRuntime runtime,
        string participant,
        int? limit = null)
    {
        if (string.IsNullOrWhiteSpace(participant))
            return [];

        var normalizedParticipant = participant.Trim();
        var candidates = runtime.ListMessages(new AgentMessageListOptions
        {
            Recipient = normalizedParticipant,
        });

        var items = new List<AgentMessageActionItem>();
        foreach (var message in candidates)
        {
            var item = DescribeAction(runtime, message);
            if (item == null || item.IsResolved)
                continue;

            items.Add(item with { Participant = normalizedParticipant });
        }

        var ordered = items
            .OrderByDescending(item => item.TriggerMessage.CreatedAt)
            .ThenByDescending(item => item.TriggerMessage.Id, StringComparer.OrdinalIgnoreCase);
        return limit is > 0
            ? ordered.Take(limit.Value).ToArray()
            : ordered.ToArray();
    }

    public static AgentMessageActionItem? DescribeAction(
        IAgentMessageRuntime runtime,
        AgentMessage message)
    {
        if (!TryClassify(message, out var actionType, out var decisions))
            return null;

        var participant = string.IsNullOrWhiteSpace(message.To)
            ? string.Empty
            : message.To.Trim();
        var resolution = FindResolution(runtime, message, participant);
        return new AgentMessageActionItem
        {
            Participant = participant,
            TriggerMessage = message,
            ActionType = actionType,
            Decisions = decisions,
            ResolutionMessage = resolution,
        };
    }

    public static bool TryBuildResponse(
        AgentMessage triggerMessage,
        string responder,
        string decision,
        string? note,
        out AgentMessageResponseSpec? response,
        out string? error)
    {
        response = null;
        error = null;

        if (triggerMessage == null)
        {
            error = "Trigger message is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(responder))
        {
            error = "Responder is required.";
            return false;
        }

        if (!TryClassify(triggerMessage, out var actionType, out _))
        {
            error = $"Message '{triggerMessage.Id}' is not actionable.";
            return false;
        }

        var normalizedResponder = responder.Trim();
        if (!string.Equals(normalizedResponder, triggerMessage.To, StringComparison.OrdinalIgnoreCase))
        {
            error = $"Responder must match the original recipient '{triggerMessage.To}'.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(decision))
        {
            error = "Decision is required.";
            return false;
        }

        var normalizedDecision = decision.Trim().ToLowerInvariant();
        switch (actionType)
        {
            case AgentMessageActionType.PlanApproval:
                return TryBuildPlanApprovalResponse(triggerMessage, normalizedResponder, normalizedDecision, note, out response, out error);
            case AgentMessageActionType.Shutdown:
                return TryBuildShutdownResponse(triggerMessage, normalizedResponder, normalizedDecision, note, out response, out error);
            case AgentMessageActionType.FollowUp:
                return TryBuildFollowUpResponse(triggerMessage, normalizedResponder, normalizedDecision, note, out response, out error);
            default:
                error = $"Unsupported action workflow '{actionType}'.";
                return false;
        }
    }

    public static bool TryClassify(
        AgentMessage message,
        out AgentMessageActionType actionType,
        out IReadOnlyList<string> decisions)
    {
        if (message.Kind == AgentMessageKind.PlanApprovalRequest)
        {
            actionType = AgentMessageActionType.PlanApproval;
            decisions = ["approve", "reject"];
            return true;
        }

        if (message.Kind == AgentMessageKind.ShutdownRequest)
        {
            actionType = AgentMessageActionType.Shutdown;
            decisions = ["ack", "decline"];
            return true;
        }

        if (message.Protocol?.RequiresResponse == true)
        {
            actionType = AgentMessageActionType.FollowUp;
            decisions = ["reply", "ack", "done"];
            return true;
        }

        actionType = default;
        decisions = [];
        return false;
    }

    private static AgentMessage? FindResolution(
        IAgentMessageRuntime runtime,
        AgentMessage triggerMessage,
        string participant)
    {
        return runtime.ListThread(triggerMessage.ThreadId)
            .Where(candidate =>
                !string.Equals(candidate.Id, triggerMessage.Id, StringComparison.OrdinalIgnoreCase) &&
                candidate.CreatedAt >= triggerMessage.CreatedAt &&
                string.Equals(candidate.From, participant, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(candidate.To, triggerMessage.From, StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault(candidate => IsResolutionFor(triggerMessage, candidate));
    }

    private static bool IsResolutionFor(AgentMessage triggerMessage, AgentMessage candidate)
    {
        return triggerMessage.Kind switch
        {
            AgentMessageKind.PlanApprovalRequest => candidate.Kind == AgentMessageKind.PlanApprovalResponse,
            AgentMessageKind.ShutdownRequest => candidate.Kind == AgentMessageKind.ShutdownResponse,
            _ => string.Equals(candidate.RelatedMessageId, triggerMessage.Id, StringComparison.OrdinalIgnoreCase) ||
                 candidate.Protocol?.ActionName is "follow-up-response" or "follow-up-acknowledged" or "follow-up-completed",
        };
    }

    private static bool TryBuildPlanApprovalResponse(
        AgentMessage triggerMessage,
        string responder,
        string decision,
        string? note,
        out AgentMessageResponseSpec? response,
        out string? error)
    {
        response = null;
        error = null;

        if (decision is not ("approve" or "reject"))
        {
            error = "Plan approval messages support decisions: approve, reject.";
            return false;
        }

        response = new AgentMessageResponseSpec
        {
            From = responder,
            To = triggerMessage.From,
            Kind = AgentMessageKind.PlanApprovalResponse,
            Body = string.IsNullOrWhiteSpace(note)
                ? (decision == "approve" ? "Approved plan request." : "Rejected plan request.")
                : note.Trim(),
            Subject = BuildReplySubject(triggerMessage.Subject),
            RelatedMessageId = triggerMessage.Id,
            Protocol = new AgentMessageProtocol
            {
                ActionName = decision == "approve"
                    ? "plan-approval-approved"
                    : "plan-approval-rejected",
            },
        };
        return true;
    }

    private static bool TryBuildShutdownResponse(
        AgentMessage triggerMessage,
        string responder,
        string decision,
        string? note,
        out AgentMessageResponseSpec? response,
        out string? error)
    {
        response = null;
        error = null;

        if (decision is not ("ack" or "decline"))
        {
            error = "Shutdown messages support decisions: ack, decline.";
            return false;
        }

        response = new AgentMessageResponseSpec
        {
            From = responder,
            To = triggerMessage.From,
            Kind = AgentMessageKind.ShutdownResponse,
            Body = string.IsNullOrWhiteSpace(note)
                ? (decision == "ack" ? "Acknowledged shutdown request." : "Declined shutdown request.")
                : note.Trim(),
            Subject = BuildReplySubject(triggerMessage.Subject),
            RelatedMessageId = triggerMessage.Id,
            Protocol = new AgentMessageProtocol
            {
                ActionName = decision == "ack"
                    ? "shutdown-acknowledged"
                    : "shutdown-declined",
            },
        };
        return true;
    }

    private static bool TryBuildFollowUpResponse(
        AgentMessage triggerMessage,
        string responder,
        string decision,
        string? note,
        out AgentMessageResponseSpec? response,
        out string? error)
    {
        response = null;
        error = null;

        if (decision is not ("reply" or "ack" or "done"))
        {
            error = "Follow-up messages support decisions: reply, ack, done.";
            return false;
        }

        response = new AgentMessageResponseSpec
        {
            From = responder,
            To = triggerMessage.From,
            Kind = AgentMessageKind.Note,
            Body = string.IsNullOrWhiteSpace(note)
                ? decision switch
                {
                    "ack" => "Acknowledged.",
                    "done" => "Done.",
                    _ => "Reply sent.",
                }
                : note.Trim(),
            Subject = BuildReplySubject(triggerMessage.Subject),
            RelatedMessageId = triggerMessage.Id,
            Protocol = new AgentMessageProtocol
            {
                ActionName = decision switch
                {
                    "ack" => "follow-up-acknowledged",
                    "done" => "follow-up-completed",
                    _ => "follow-up-response",
                },
            },
        };
        return true;
    }

    private static string? BuildReplySubject(string? subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
            return null;

        var normalized = subject.Trim();
        return normalized.StartsWith("Re:", StringComparison.OrdinalIgnoreCase)
            ? normalized
            : $"Re: {normalized}";
    }
}

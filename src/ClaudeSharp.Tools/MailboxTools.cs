using System.Text.Json;
using System.Text.Json.Serialization;
using ClaudeSharp.Core.Agents;
using ClaudeSharp.Core.Permissions;
using ClaudeSharp.Core.Tools;

namespace ClaudeSharp.Tools;

/// <summary>
/// Represents the top-level SendMessage tool input.
/// </summary>
public sealed class SendMessageToolInput
{
    [JsonPropertyName("request")]
    public SendMessageRequestInput? Request { get; set; }
}

/// <summary>
/// Represents a send-message request payload.
/// </summary>
public sealed class SendMessageRequestInput
{
    [JsonPropertyName("to")]
    public string? To { get; set; }

    [JsonPropertyName("from")]
    public string? From { get; set; }

    [JsonPropertyName("team_name")]
    public string? TeamName { get; set; }

    [JsonPropertyName("subject")]
    public string? Subject { get; set; }

    [JsonPropertyName("reply_to_message_id")]
    public string? ReplyToMessageId { get; set; }

    [JsonPropertyName("message")]
    public JsonElement Message { get; set; }
}

/// <summary>
/// Represents the top-level mailbox status tool input.
/// </summary>
public sealed class MailboxStatusToolInput
{
    [JsonPropertyName("request")]
    public MailboxStatusRequestInput? Request { get; set; }
}

/// <summary>
/// Represents the top-level mailbox respond tool input.
/// </summary>
public sealed class MailboxRespondToolInput
{
    [JsonPropertyName("request")]
    public MailboxRespondRequestInput? Request { get; set; }
}

/// <summary>
/// Represents a mailbox status query request.
/// </summary>
public sealed class MailboxStatusRequestInput
{
    [JsonPropertyName("view")]
    public string? View { get; set; }

    [JsonPropertyName("message_id")]
    public string? MessageId { get; set; }

    [JsonPropertyName("thread_id")]
    public string? ThreadId { get; set; }

    [JsonPropertyName("participant")]
    public string? Participant { get; set; }

    [JsonPropertyName("sender")]
    public string? Sender { get; set; }

    [JsonPropertyName("recipient")]
    public string? Recipient { get; set; }

    [JsonPropertyName("unread_only")]
    public bool UnreadOnly { get; set; }

    [JsonPropertyName("limit")]
    public int? Limit { get; set; }

    [JsonPropertyName("mark_as_read")]
    public bool MarkAsRead { get; set; }
}

/// <summary>
/// Represents a mailbox action response request.
/// </summary>
public sealed class MailboxRespondRequestInput
{
    [JsonPropertyName("message_id")]
    public string? MessageId { get; set; }

    [JsonPropertyName("decision")]
    public string? Decision { get; set; }

    [JsonPropertyName("responder")]
    public string? Responder { get; set; }

    [JsonPropertyName("note")]
    public string? Note { get; set; }

    [JsonPropertyName("mark_original_as_read")]
    public bool MarkOriginalAsRead { get; set; } = true;
}

/// <summary>
/// Sends structured or plain-text mailbox messages between agents.
/// </summary>
public sealed class SendMessageTool : ITool
{
    private readonly IAgentMessageRuntime _messageRuntime;
    private readonly IAgentTeamRuntime? _teamRuntime;
    private readonly IAgentMessageActivationRuntime? _activationRuntime;
    private readonly IAgentTaskRuntime? _taskRuntime;
    private readonly string? _currentWorkItemId;

    public SendMessageTool(
        IAgentMessageRuntime messageRuntime,
        IAgentTeamRuntime? teamRuntime = null,
        IAgentMessageActivationRuntime? activationRuntime = null,
        IAgentTaskRuntime? taskRuntime = null,
        string? currentWorkItemId = null)
    {
        _messageRuntime = messageRuntime;
        _teamRuntime = teamRuntime;
        _activationRuntime = activationRuntime;
        _taskRuntime = taskRuntime;
        _currentWorkItemId = string.IsNullOrWhiteSpace(currentWorkItemId)
            ? null
            : currentWorkItemId.Trim();
    }

    public string Name => "SendMessage";

    public Task<string> GetDescriptionAsync() =>
        Task.FromResult("Send a mailbox message to another local agent or teammate.");

    public JsonElement GetInputSchema() =>
        JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "request": {
              "type": "object",
              "properties": {
                "to": { "type": "string" },
                "from": { "type": "string" },
                "team_name": { "type": "string" },
                "subject": { "type": "string" },
                "reply_to_message_id": { "type": "string" },
                "message": {
                  "oneOf": [
                    { "type": "string" },
                    {
                      "type": "object",
                      "properties": {
                        "kind": { "type": "string" },
                        "body": { "type": "string" },
                        "subject": { "type": "string" },
                        "action": { "type": "string" },
                        "requires_response": { "type": "boolean" },
                        "resume_reason": { "type": "string" }
                      },
                      "required": ["kind", "body"],
                      "additionalProperties": false
                    }
                  ]
                }
              },
              "required": ["to", "message"],
              "additionalProperties": false
            }
          },
          "required": ["request"],
          "additionalProperties": false
        }
        """).RootElement.Clone();

    public Task<string> GetPromptAsync(ToolPromptContext context) =>
        Task.FromResult("""
            Send a structured mailbox message to another local agent.

            Supported targets in this ClaudeSharp build:
            - a raw local mailbox address like "main" or "subagent"
            - a teammate name inside a known team by setting team_name
            - "*" or "broadcast" inside a known team to fan out to all teammates

            bridge: and uds: targets are not implemented yet in this build.
            """);

    public Task<ValidationResult> ValidateInputAsync(JsonElement input, ToolExecutionContext context)
    {
        var parsed = JsonSerializer.Deserialize<SendMessageToolInput>(input);
        if (parsed?.Request == null ||
            string.IsNullOrWhiteSpace(parsed.Request.To) ||
            parsed.Request.Message.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return Task.FromResult(ValidationResult.Invalid("request.to and request.message are required."));
        }

        return Task.FromResult(
            TryParseMessage(parsed.Request.Message, parsed.Request.Subject, out _, out _, out _, out var error)
                ? ValidationResult.Valid()
                : ValidationResult.Invalid(error!));
    }

    public Task<PermissionResult> CheckPermissionsAsync(JsonElement input, ToolExecutionContext context) =>
        Task.FromResult(PermissionResult.Allow());

    public bool IsReadOnly(JsonElement input) => false;
    public bool IsConcurrencySafe(JsonElement input) => false;
    public string GetUserFacingName(JsonElement? input = null) => "Send message";
    public string? GetActivityDescription(JsonElement? input) => "Sending mailbox message";

    public async Task<ToolResult> ExecuteAsync(
        JsonElement input,
        ToolExecutionContext context,
        IProgress<ToolProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var parsed = JsonSerializer.Deserialize<SendMessageToolInput>(input);
        if (parsed?.Request == null)
            return ToolResult.Error("request.to and request.message are required.");

        var request = parsed.Request;
        if (!TryParseMessage(request.Message, request.Subject, out var kind, out var body, out var protocol, out var error, out var subject))
            return ToolResult.Error(error!);

        var sender = ResolveSender(request);
        if (sender == null)
            return ToolResult.Error("Sender could not be resolved.");

        if (request.To!.StartsWith("bridge:", StringComparison.OrdinalIgnoreCase) ||
            request.To.StartsWith("uds:", StringComparison.OrdinalIgnoreCase))
        {
            return ToolResult.Error("bridge: and uds: targets are not implemented in this ClaudeSharp build.");
        }

        List<AgentMessage> delivered;
        try
        {
            delivered = ResolveRecipients(request)
                .Select(recipient => _messageRuntime.SendMessage(
                    sender,
                    recipient,
                    body!,
                    kind,
                    subject,
                    request.ReplyToMessageId,
                    protocol))
                .ToList();
        }
        catch (Exception ex)
        {
            return ToolResult.Error(ex.Message);
        }

        TryTrackCurrentWorkItemApproval(delivered);
        _ = TrySynchronizeApprovalTasks();

        var lines = new List<string>
        {
            $"Delivered {delivered.Count} message(s).",
        };
        foreach (var message in delivered)
            lines.Add($"- {AgentMessageFormatter.FormatSummaryLine(message)}");

        foreach (var activation in await ActivateRecipientsAsync(delivered, sender, cancellationToken))
            lines.Add(FormatActivationLine(activation));

        return ToolResult.Success(string.Join(Environment.NewLine, lines));
    }

    private void TryTrackCurrentWorkItemApproval(IReadOnlyList<AgentMessage> delivered)
    {
        if (_taskRuntime == null ||
            string.IsNullOrWhiteSpace(_currentWorkItemId))
        {
            return;
        }

        var approvalRequest = delivered
            .LastOrDefault(message => message.Kind == AgentMessageKind.PlanApprovalRequest);
        if (approvalRequest == null)
            return;

        try
        {
            AgentWorkItemApprovalCoordinator.TryMarkAwaitingApproval(
                _taskRuntime,
                _currentWorkItemId,
                approvalRequest);
        }
        catch
        {
            // Approval tracking is best-effort; the mailbox message itself was delivered successfully.
        }
    }

    private AgentMailboxTaskProjectionResult? TrySynchronizeApprovalTasks()
    {
        if (_taskRuntime == null)
            return null;

        try
        {
            return AgentMailboxTaskProjector.Synchronize(_messageRuntime, _taskRuntime);
        }
        catch
        {
            return null;
        }
    }

    private async Task<IReadOnlyList<AgentMessageActivationResult>> ActivateRecipientsAsync(
        IReadOnlyList<AgentMessage> delivered,
        string sender,
        CancellationToken cancellationToken)
    {
        if (_activationRuntime == null || delivered.Count == 0)
            return [];

        var results = new List<AgentMessageActivationResult>();
        foreach (var recipient in delivered
                     .Select(message => message.To)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var message = delivered
                .Last(candidate => string.Equals(candidate.To, recipient, StringComparison.OrdinalIgnoreCase));
            var result = await _activationRuntime.TryActivateAsync(message, cancellationToken);
            if (result.Status != AgentMessageActivationStatus.NotRegistered)
                results.Add(result);
        }

        return results;
    }

    private string? ResolveSender(SendMessageRequestInput request)
    {
        if (string.IsNullOrWhiteSpace(request.TeamName))
            return string.IsNullOrWhiteSpace(request.From) ? "main" : request.From.Trim();

        var team = _teamRuntime == null
            ? null
            : AgentTeamLookup.ResolveTeam(_teamRuntime, request.TeamName);
        if (team == null)
            throw new InvalidOperationException($"Team '{request.TeamName!.Trim()}' was not found.");

        if (string.IsNullOrWhiteSpace(request.From))
            return "main";

        var senderMember = AgentTeamLookup.ResolveMember(team, request.From);
        return senderMember == null
            ? request.From.Trim()
            : $"{team.Name}/{senderMember.Name}";
    }

    private IReadOnlyList<string> ResolveRecipients(SendMessageRequestInput request)
    {
        if (string.IsNullOrWhiteSpace(request.TeamName))
            return [request.To!.Trim()];

        if (_teamRuntime == null)
            throw new InvalidOperationException("Team runtime is not configured.");

        var team = AgentTeamLookup.ResolveTeam(_teamRuntime, request.TeamName);
        if (team == null)
            throw new InvalidOperationException($"Team '{request.TeamName!.Trim()}' was not found.");

        if (request.To is "*" or "broadcast")
        {
            return team.Members
                .OrderBy(member => member.Role == AgentTeamMemberRole.Lead ? 0 : 1)
                .ThenBy(member => member.Name, StringComparer.OrdinalIgnoreCase)
                .Select(member => $"{team.Name}/{member.Name}")
                .ToArray();
        }

        var teammate = AgentTeamLookup.ResolveMember(team, request.To!);
        if (teammate == null)
            throw new InvalidOperationException($"Teammate '{request.To!.Trim()}' was not found in team '{team.Name}'.");

        return [$"{team.Name}/{teammate.Name}"];
    }

    private static bool TryParseMessage(
        JsonElement value,
        string? fallbackSubject,
        out AgentMessageKind kind,
        out string? body,
        out AgentMessageProtocol? protocol,
        out string? error) =>
        TryParseMessage(value, fallbackSubject, out kind, out body, out protocol, out error, out _);

    private static bool TryParseMessage(
        JsonElement value,
        string? fallbackSubject,
        out AgentMessageKind kind,
        out string? body,
        out AgentMessageProtocol? protocol,
        out string? error,
        out string? subject)
    {
        kind = AgentMessageKind.Note;
        body = null;
        protocol = null;
        error = null;
        subject = string.IsNullOrWhiteSpace(fallbackSubject) ? null : fallbackSubject.Trim();

        if (value.ValueKind == JsonValueKind.String)
        {
            body = value.GetString()?.Trim();
            error = string.IsNullOrWhiteSpace(body) ? "request.message cannot be empty." : null;
            return error == null;
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            error = "request.message must be a string or an object.";
            return false;
        }

        if (!value.TryGetProperty("kind", out var kindValue) ||
            !Enum.TryParse<AgentMessageKind>(kindValue.GetString(), ignoreCase: true, out kind))
        {
            error = "request.message.kind is invalid.";
            return false;
        }

        if (!value.TryGetProperty("body", out var bodyValue) ||
            bodyValue.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(bodyValue.GetString()))
        {
            error = "request.message.body is required.";
            return false;
        }

        body = bodyValue.GetString()!.Trim();
        if (value.TryGetProperty("subject", out var subjectValue) &&
            subjectValue.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(subjectValue.GetString()))
        {
            subject = subjectValue.GetString()!.Trim();
        }

        var actionName = value.TryGetProperty("action", out var actionValue) &&
            actionValue.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(actionValue.GetString())
            ? actionValue.GetString()!.Trim()
            : null;
        var requiresResponse = value.TryGetProperty("requires_response", out var requiresResponseValue) &&
            requiresResponseValue.ValueKind is JsonValueKind.True or JsonValueKind.False &&
            requiresResponseValue.GetBoolean();
        var resumeReason = value.TryGetProperty("resume_reason", out var resumeReasonValue) &&
            resumeReasonValue.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(resumeReasonValue.GetString())
            ? resumeReasonValue.GetString()!.Trim()
            : null;

        if (!string.IsNullOrWhiteSpace(actionName) || requiresResponse || !string.IsNullOrWhiteSpace(resumeReason))
        {
            protocol = new AgentMessageProtocol
            {
                ActionName = actionName,
                RequiresResponse = requiresResponse,
                ResumeReason = resumeReason,
            };
        }

        return true;
    }

    internal static string FormatActivationLineForSharedUse(AgentMessageActivationResult result) =>
        FormatActivationLine(result);

    private static string FormatActivationLine(AgentMessageActivationResult result)
    {
        return result.Status switch
        {
            AgentMessageActivationStatus.Reactivated =>
                $"- Reactivated {result.Owner} as {result.BackgroundRunId} ({result.WorkItemId})." +
                $"{FormatActivationMessageSuffix(result)}",
            AgentMessageActivationStatus.AlreadyActive =>
                $"- {result.Owner} already has an active background run." +
                $"{FormatActivationMessageSuffix(result)}",
            AgentMessageActivationStatus.Failed =>
                $"- Failed to reactivate {result.Owner}: {result.Message}",
            _ => $"- No activation handler is registered for {result.Owner}.",
        };
    }

    private static string FormatActivationMessageSuffix(AgentMessageActivationResult result) =>
        string.IsNullOrWhiteSpace(result.Message) ? string.Empty : $" {result.Message}";
}

/// <summary>
/// Reads mailbox summaries and message details.
/// </summary>
public sealed class MailboxStatusTool : ITool
{
    private readonly IAgentMessageRuntime _messageRuntime;

    public MailboxStatusTool(IAgentMessageRuntime messageRuntime)
    {
        _messageRuntime = messageRuntime;
    }

    public string Name => "MailboxStatus";

    public Task<string> GetDescriptionAsync() =>
        Task.FromResult("Inspect mailbox summaries, unread messages, and message details.");

    public JsonElement GetInputSchema() =>
        JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
                "request": {
                  "type": "object",
                  "properties": {
                "view": {
                  "type": "string",
                  "enum": ["overview", "list", "inbox", "outbox", "thread", "pending"]
                },
                "message_id": { "type": "string" },
                "thread_id": { "type": "string" },
                "participant": { "type": "string" },
                "sender": { "type": "string" },
                "recipient": { "type": "string" },
                "unread_only": { "type": "boolean" },
                "limit": { "type": "integer" },
                "mark_as_read": { "type": "boolean" }
              },
              "additionalProperties": false
            }
          },
          "required": ["request"],
          "additionalProperties": false
        }
        """).RootElement.Clone();

    public Task<string> GetPromptAsync(ToolPromptContext context) =>
        Task.FromResult("""
            Inspect local mailbox state.

            Use this after SendMessage, or when you need to check whether a teammate or local agent has unread messages.
            Set request.message_id to inspect one message in detail. Set mark_as_read=true to acknowledge it.
            Otherwise you can filter by participant, sender, recipient, and unread_only, or set
            request.view to "inbox", "outbox", "thread", or "pending" for a focused mailbox view.
            """);

    public Task<ValidationResult> ValidateInputAsync(JsonElement input, ToolExecutionContext context)
    {
        var parsed = JsonSerializer.Deserialize<MailboxStatusToolInput>(input);
        if (parsed?.Request == null)
            return Task.FromResult(ValidationResult.Invalid("request is required."));

        if (parsed.Request.Limit is <= 0)
            return Task.FromResult(ValidationResult.Invalid("request.limit must be greater than 0."));

        if (!IsSupportedView(parsed.Request.View))
            return Task.FromResult(ValidationResult.Invalid("request.view must be one of overview, list, inbox, outbox, thread, or pending."));

        if (IsThreadView(parsed.Request.View) &&
            string.IsNullOrWhiteSpace(parsed.Request.ThreadId))
        {
            return Task.FromResult(ValidationResult.Invalid("request.thread_id is required when request.view is thread."));
        }

        if (IsPendingView(parsed.Request.View) &&
            string.IsNullOrWhiteSpace(parsed.Request.Participant) &&
            string.IsNullOrWhiteSpace(parsed.Request.Recipient))
        {
            return Task.FromResult(ValidationResult.Invalid("request.participant or request.recipient is required when request.view is pending."));
        }

        return Task.FromResult(ValidationResult.Valid());
    }

    public Task<PermissionResult> CheckPermissionsAsync(JsonElement input, ToolExecutionContext context) =>
        Task.FromResult(PermissionResult.Allow());

    public bool IsReadOnly(JsonElement input)
    {
        var parsed = JsonSerializer.Deserialize<MailboxStatusToolInput>(input);
        return parsed?.Request?.MarkAsRead != true;
    }

    public bool IsConcurrencySafe(JsonElement input) => true;
    public string GetUserFacingName(JsonElement? input = null) => "Mailbox status";
    public string? GetActivityDescription(JsonElement? input) => "Reading mailbox";

    public Task<ToolResult> ExecuteAsync(
        JsonElement input,
        ToolExecutionContext context,
        IProgress<ToolProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var parsed = JsonSerializer.Deserialize<MailboxStatusToolInput>(input);
        if (parsed?.Request == null)
            return Task.FromResult(ToolResult.Error("request is required."));

        var request = parsed.Request;
        if (!string.IsNullOrWhiteSpace(request.MessageId))
        {
            if (request.MarkAsRead)
                _messageRuntime.MarkMessageRead(request.MessageId);

            var message = _messageRuntime.GetMessage(request.MessageId);
            return Task.FromResult(message == null
                ? ToolResult.Error($"Message '{request.MessageId.Trim()}' was not found.")
                : ToolResult.Success(AgentMessageFormatter.FormatDetails(message)));
        }

        if (IsThreadView(request.View))
        {
            var threadId = request.ThreadId!.Trim();
            var thread = _messageRuntime.ListThread(threadId);
            if (request.MarkAsRead)
            {
                foreach (var message in thread.Where(message => ShouldMarkMessageAsRead(message, request)))
                    _messageRuntime.MarkMessageRead(message.Id);

                thread = _messageRuntime.ListThread(threadId);
            }

            return Task.FromResult(ToolResult.Success(
                AgentMessageFormatter.FormatThread(threadId, thread)));
        }

        if (IsPendingView(request.View))
        {
            var participant = ResolveInboxParticipant(request);
            if (string.IsNullOrWhiteSpace(participant))
                return Task.FromResult(ToolResult.Error("request.participant or request.recipient is required for pending view."));

            var pending = AgentMessageWorkflow.ListPendingActions(_messageRuntime, participant, request.Limit);
            if (request.MarkAsRead)
            {
                foreach (var item in pending)
                    _messageRuntime.MarkMessageRead(item.TriggerMessage.Id);
            }

            return Task.FromResult(ToolResult.Success(
                AgentMessageFormatter.FormatPendingActions(participant, pending)));
        }

        if (IsInboxView(request.View))
        {
            var participant = ResolveInboxParticipant(request);
            if (string.IsNullOrWhiteSpace(participant))
                return Task.FromResult(ToolResult.Error("request.participant or request.recipient is required for inbox view."));

            if (request.MarkAsRead)
                _messageRuntime.MarkRecipientMessagesRead(participant);

            var inbox = _messageRuntime.ListMessages(new AgentMessageListOptions
            {
                Recipient = participant,
                Status = request.UnreadOnly ? AgentMessageStatus.Delivered : null,
                Limit = request.Limit,
            });

            return Task.FromResult(ToolResult.Success(
                AgentMessageFormatter.FormatInbox(participant, inbox)));
        }

        if (IsOutboxView(request.View))
        {
            var participant = ResolveOutboxParticipant(request);
            if (string.IsNullOrWhiteSpace(participant))
                return Task.FromResult(ToolResult.Error("request.participant or request.sender is required for outbox view."));

            var outbox = _messageRuntime.ListMessages(new AgentMessageListOptions
            {
                Sender = participant,
                Limit = request.Limit,
            });

            return Task.FromResult(ToolResult.Success(
                AgentMessageFormatter.FormatOutbox(participant, outbox)));
        }

        if (request.MarkAsRead &&
            !string.IsNullOrWhiteSpace(request.Recipient))
        {
            _messageRuntime.MarkRecipientMessagesRead(request.Recipient);
        }

        var hasFilter = !string.IsNullOrWhiteSpace(request.Participant) ||
                        !string.IsNullOrWhiteSpace(request.Sender) ||
                        !string.IsNullOrWhiteSpace(request.Recipient) ||
                        request.UnreadOnly ||
                        request.Limit.HasValue;
        if (!hasFilter)
        {
            return Task.FromResult(ToolResult.Success(
                AgentMessageFormatter.FormatOverview(
                    _messageRuntime.ListMessages(new AgentMessageListOptions { Limit = 5 }),
                    _messageRuntime.GetUnreadCounts())));
        }

        var messages = _messageRuntime.ListMessages(new AgentMessageListOptions
            {
                Sender = request.Sender,
                Recipient = request.Recipient,
                ThreadId = request.ThreadId,
                Status = request.UnreadOnly ? AgentMessageStatus.Delivered : null,
                Limit = request.Limit,
            })
            .Where(message =>
                string.IsNullOrWhiteSpace(request.Participant) ||
                string.Equals(message.From, request.Participant.Trim(), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(message.To, request.Participant.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return Task.FromResult(ToolResult.Success(AgentMessageFormatter.FormatList(messages)));
    }

    private static bool IsSupportedView(string? view) =>
        string.IsNullOrWhiteSpace(view) ||
        view.Equals("overview", StringComparison.OrdinalIgnoreCase) ||
        view.Equals("list", StringComparison.OrdinalIgnoreCase) ||
        view.Equals("inbox", StringComparison.OrdinalIgnoreCase) ||
        view.Equals("outbox", StringComparison.OrdinalIgnoreCase) ||
        view.Equals("thread", StringComparison.OrdinalIgnoreCase) ||
        view.Equals("pending", StringComparison.OrdinalIgnoreCase);

    private static bool IsInboxView(string? view) =>
        view?.Equals("inbox", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsOutboxView(string? view) =>
        view?.Equals("outbox", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsThreadView(string? view) =>
        view?.Equals("thread", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsPendingView(string? view) =>
        view?.Equals("pending", StringComparison.OrdinalIgnoreCase) == true;

    private static bool ShouldMarkMessageAsRead(
        AgentMessage message,
        MailboxStatusRequestInput request)
    {
        if (message.Status != AgentMessageStatus.Delivered)
            return false;

        if (!string.IsNullOrWhiteSpace(request.Recipient))
        {
            return string.Equals(
                message.To,
                request.Recipient.Trim(),
                StringComparison.OrdinalIgnoreCase);
        }

        if (!string.IsNullOrWhiteSpace(request.Participant))
        {
            return string.Equals(
                message.To,
                request.Participant.Trim(),
                StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    private static string? ResolveInboxParticipant(MailboxStatusRequestInput request) =>
        string.IsNullOrWhiteSpace(request.Recipient)
            ? request.Participant?.Trim()
            : request.Recipient.Trim();

    private static string? ResolveOutboxParticipant(MailboxStatusRequestInput request) =>
        string.IsNullOrWhiteSpace(request.Sender)
            ? request.Participant?.Trim()
            : request.Sender.Trim();
}

/// <summary>
/// Responds to actionable mailbox messages such as approvals, shutdown requests, and follow-ups.
/// </summary>
public sealed class MailboxRespondTool : ITool
{
    private readonly IAgentMessageRuntime _messageRuntime;
    private readonly IAgentMessageActivationRuntime? _activationRuntime;
    private readonly IAgentTaskRuntime? _taskRuntime;

    public MailboxRespondTool(
        IAgentMessageRuntime messageRuntime,
        IAgentMessageActivationRuntime? activationRuntime = null,
        IAgentTaskRuntime? taskRuntime = null)
    {
        _messageRuntime = messageRuntime;
        _activationRuntime = activationRuntime;
        _taskRuntime = taskRuntime;
    }

    public string Name => "MailboxRespond";

    public Task<string> GetDescriptionAsync() =>
        Task.FromResult("Respond to approvals, shutdown requests, or structured follow-up messages in the mailbox.");

    public JsonElement GetInputSchema() =>
        JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "request": {
              "type": "object",
              "properties": {
                "message_id": { "type": "string" },
                "decision": { "type": "string" },
                "responder": { "type": "string" },
                "note": { "type": "string" },
                "mark_original_as_read": { "type": "boolean" }
              },
              "required": ["message_id", "decision"],
              "additionalProperties": false
            }
          },
          "required": ["request"],
          "additionalProperties": false
        }
        """).RootElement.Clone();

    public Task<string> GetPromptAsync(ToolPromptContext context) =>
        Task.FromResult("""
            Respond to an actionable mailbox message.

            Use this for:
            - plan approval requests
            - shutdown requests
            - follow-up messages that explicitly require a response

            Provide request.message_id and a request.decision such as approve, reject, ack, decline, reply, or done.
            """);

    public Task<ValidationResult> ValidateInputAsync(JsonElement input, ToolExecutionContext context)
    {
        var parsed = JsonSerializer.Deserialize<MailboxRespondToolInput>(input);
        if (parsed?.Request == null ||
            string.IsNullOrWhiteSpace(parsed.Request.MessageId) ||
            string.IsNullOrWhiteSpace(parsed.Request.Decision))
        {
            return Task.FromResult(ValidationResult.Invalid("request.message_id and request.decision are required."));
        }

        return Task.FromResult(ValidationResult.Valid());
    }

    public Task<PermissionResult> CheckPermissionsAsync(JsonElement input, ToolExecutionContext context) =>
        Task.FromResult(PermissionResult.Allow());

    public bool IsReadOnly(JsonElement input) => false;
    public bool IsConcurrencySafe(JsonElement input) => false;
    public string GetUserFacingName(JsonElement? input = null) => "Mailbox respond";
    public string? GetActivityDescription(JsonElement? input) => "Responding to mailbox action";

    public async Task<ToolResult> ExecuteAsync(
        JsonElement input,
        ToolExecutionContext context,
        IProgress<ToolProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var parsed = JsonSerializer.Deserialize<MailboxRespondToolInput>(input);
        if (parsed?.Request == null)
            return ToolResult.Error("request.message_id and request.decision are required.");

        var request = parsed.Request;
        var triggerMessage = _messageRuntime.GetMessage(request.MessageId!);
        if (triggerMessage == null)
            return ToolResult.Error($"Message '{request.MessageId!.Trim()}' was not found.");

        var responder = string.IsNullOrWhiteSpace(request.Responder)
            ? triggerMessage.To
            : request.Responder.Trim();
        if (!AgentMessageWorkflow.TryBuildResponse(
                triggerMessage,
                responder,
                request.Decision!,
                request.Note,
                out var response,
                out var error))
        {
            return ToolResult.Error(error!);
        }

        var delivered = _messageRuntime.SendMessage(
            response!.From,
            response.To,
            response.Kind,
            response.Body,
            response.Subject,
            response.RelatedMessageId,
            response.Protocol);
        if (request.MarkOriginalAsRead)
            _messageRuntime.MarkMessageRead(triggerMessage.Id);
        TryUpdateOriginalWorkItem(triggerMessage, delivered);
        _ = TrySynchronizeApprovalTasks();

        var lines = new List<string>
        {
            $"Responded to {triggerMessage.Id} with {delivered.Id}.",
            $"- Decision: {request.Decision!.Trim().ToLowerInvariant()}",
            $"- {AgentMessageFormatter.FormatSummaryLine(delivered)}",
        };

        if (_activationRuntime != null && ShouldAttemptActivation(delivered))
        {
            var activation = await _activationRuntime.TryActivateAsync(delivered, cancellationToken);
            if (activation.Status != AgentMessageActivationStatus.NotRegistered)
                lines.Add(SendMessageTool.FormatActivationLineForSharedUse(activation));
        }

        return ToolResult.Success(string.Join(Environment.NewLine, lines));
    }

    private void TryUpdateOriginalWorkItem(
        AgentMessage triggerMessage,
        AgentMessage responseMessage)
    {
        if (_taskRuntime == null ||
            triggerMessage.Kind != AgentMessageKind.PlanApprovalRequest ||
            responseMessage.Kind != AgentMessageKind.PlanApprovalResponse)
        {
            return;
        }

        try
        {
            AgentWorkItemApprovalCoordinator.TryApplyApprovalResponse(
                _taskRuntime,
                triggerMessage,
                responseMessage);
        }
        catch
        {
            // Approval coordination is best-effort; the mailbox response still succeeds.
        }
    }

    private static bool ShouldAttemptActivation(AgentMessage responseMessage)
    {
        return responseMessage.Kind != AgentMessageKind.PlanApprovalResponse ||
               !string.Equals(
                   responseMessage.Protocol?.ActionName,
                   "plan-approval-rejected",
                   StringComparison.OrdinalIgnoreCase);
    }

    private AgentMailboxTaskProjectionResult? TrySynchronizeApprovalTasks()
    {
        if (_taskRuntime == null)
            return null;

        try
        {
            return AgentMailboxTaskProjector.Synchronize(_messageRuntime, _taskRuntime);
        }
        catch
        {
            return null;
        }
    }
}

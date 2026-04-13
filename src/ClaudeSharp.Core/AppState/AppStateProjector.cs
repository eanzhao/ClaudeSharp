using ClaudeSharp.Core.Agents;
using ClaudeSharp.Core.Configuration;
using ClaudeSharp.Core.Mcp;
using ClaudeSharp.Core.Permissions;

namespace ClaudeSharp.Core.AppState;

/// <summary>
/// Projects runtime services into a host-facing app-state snapshot.
/// </summary>
public sealed class AppStateProjector
{
    public AppStateSnapshot CreateSnapshot(
        string workingDirectory,
        PermissionMode permissionMode,
        AgentAutoResumeMode autoResumeMode = AgentAutoResumeMode.Queue,
        string? sessionId = null,
        string? memoryRootDirectory = null,
        ManagedSettingsSnapshot? managedSettings = null,
        AnthropicTokenSourceSnapshot? activeTokenSource = null,
        McpConnectionManager? mcpConnectionManager = null,
        IAgentTaskRuntime? agentTaskRuntime = null,
        IAgentTeamRuntime? agentTeamRuntime = null,
        IAgentMessageRuntime? agentMessageRuntime = null,
        AgentRuntimeOptions? agentRuntimeOptions = null)
    {
        var workItems = SnapshotWorkItems(agentTaskRuntime);
        var effectiveAutoResumeMode = agentRuntimeOptions?.AutoResumeMode ?? autoResumeMode;

        return new AppStateSnapshot
        {
            SessionId = sessionId,
            WorkingDirectory = workingDirectory,
            PermissionMode = permissionMode,
            AutoResumeMode = effectiveAutoResumeMode,
            ActiveTaskId = SelectActiveTaskId(workItems),
            MemoryRootDirectory = memoryRootDirectory,
            ManagedSettings = managedSettings ?? ManagedSettingsSnapshot.Empty,
            ActiveTokenSource = activeTokenSource,
            McpConnections = SnapshotMcpConnections(mcpConnectionManager),
            WorkItems = workItems,
            TaskAttention = SnapshotTaskAttention(agentTaskRuntime),
            Teams = SnapshotTeams(agentTeamRuntime),
            Mailboxes = SnapshotMailboxes(agentMessageRuntime),
        };
    }

    internal static IReadOnlyDictionary<string, McpConnectionState> SnapshotMcpConnections(
        McpConnectionManager? manager)
    {
        if (manager == null)
        {
            return new Dictionary<string, McpConnectionState>(StringComparer.OrdinalIgnoreCase);
        }

        return manager.Snapshot()
            .ToDictionary(
                snapshot => snapshot.ServerId,
                snapshot => snapshot.State,
                StringComparer.OrdinalIgnoreCase);
    }

    internal static IReadOnlyDictionary<string, AgentWorkItemStatus> SnapshotWorkItems(
        IAgentTaskRuntime? runtime)
    {
        if (runtime == null)
        {
            return new Dictionary<string, AgentWorkItemStatus>(StringComparer.OrdinalIgnoreCase);
        }

        return runtime.ListWorkItems()
            .ToDictionary(
                item => item.Id,
                item => item.Status,
                StringComparer.OrdinalIgnoreCase);
    }

    internal static string? SelectActiveTaskId(
        IReadOnlyDictionary<string, AgentWorkItemStatus> workItems)
    {
        static int Rank(AgentWorkItemStatus status) =>
            status switch
            {
                AgentWorkItemStatus.InProgress => 0,
                AgentWorkItemStatus.AwaitingResume => 1,
                AgentWorkItemStatus.AwaitingApproval => 2,
                AgentWorkItemStatus.Blocked => 3,
                AgentWorkItemStatus.Pending => 4,
                _ => 5,
            };

        return workItems
            .Where(entry => entry.Value is AgentWorkItemStatus.InProgress or AgentWorkItemStatus.AwaitingResume or AgentWorkItemStatus.AwaitingApproval or AgentWorkItemStatus.Blocked or AgentWorkItemStatus.Pending)
            .OrderBy(entry => Rank(entry.Value))
            .ThenBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .Select(entry => entry.Key)
            .FirstOrDefault();
    }

    internal static IReadOnlyList<AppStateTeamSnapshot> SnapshotTeams(
        IAgentTeamRuntime? runtime)
    {
        if (runtime == null)
            return [];

        return runtime.ListTeams()
            .Select(team =>
            {
                var lead = string.IsNullOrWhiteSpace(team.LeadMemberId)
                    ? null
                    : team.GetMember(team.LeadMemberId!);

                return new AppStateTeamSnapshot
                {
                    Id = team.Id,
                    Name = team.Name,
                    Description = team.Description,
                    LeadName = lead?.Name,
                    Members = team.Members
                        .OrderBy(member => member.Role == AgentTeamMemberRole.Lead ? 0 : 1)
                        .ThenBy(member => member.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(member => member.Name)
                        .ToArray(),
                };
            })
            .ToArray();
    }

    internal static AppStateTaskAttentionSnapshot SnapshotTaskAttention(
        IAgentTaskRuntime? runtime)
    {
        if (runtime == null)
            return new AppStateTaskAttentionSnapshot();

        var workItems = runtime.ListWorkItems();
        return new AppStateTaskAttentionSnapshot
        {
            AwaitingApprovalCount = workItems.Count(item => item.Status == AgentWorkItemStatus.AwaitingApproval),
            AwaitingResumeCount = workItems.Count(item => item.Status == AgentWorkItemStatus.AwaitingResume),
        };
    }

    internal static IReadOnlyList<AppStateMailboxSnapshot> SnapshotMailboxes(
        IAgentMessageRuntime? runtime)
    {
        if (runtime == null)
            return [];

        var messages = runtime.ListMessages();
        var participants = messages
            .SelectMany(message => new[] { message.From, message.To })
            .Where(participant => !string.IsNullOrWhiteSpace(participant))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(participant => participant, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return participants
            .Select(participant =>
            {
                var related = messages.Where(message =>
                        string.Equals(message.From, participant, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(message.To, participant, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                var inbox = related.Where(message =>
                    string.Equals(message.To, participant, StringComparison.OrdinalIgnoreCase)).ToArray();
                var outbox = related.Where(message =>
                    string.Equals(message.From, participant, StringComparison.OrdinalIgnoreCase)).ToArray();
                var latest = related
                    .OrderByDescending(message => message.CreatedAt)
                    .ThenByDescending(message => message.Id, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                var pendingActions = AgentMessageWorkflow.ListPendingActions(runtime, participant);
                var latestCounterparty = latest == null
                    ? null
                    : string.Equals(latest.From, participant, StringComparison.OrdinalIgnoreCase)
                        ? latest.To
                        : latest.From;

                return new AppStateMailboxSnapshot
                {
                    Participant = participant,
                    InboxCount = inbox.Length,
                    UnreadCount = inbox.Count(message => message.Status == AgentMessageStatus.Delivered),
                    OutboxCount = outbox.Length,
                    ThreadCount = related
                        .Select(message => message.ThreadId)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count(),
                    PendingActionCount = pendingActions.Count,
                    PendingPlanApprovalCount = pendingActions.Count(item =>
                        item.ActionType == AgentMessageActionType.PlanApproval),
                    LatestThreadId = latest?.ThreadId,
                    LatestSubject = latest?.Subject,
                    LatestCounterparty = latestCounterparty,
                    LatestMessageAt = latest?.CreatedAt,
                };
            })
            .ToArray();
    }

}

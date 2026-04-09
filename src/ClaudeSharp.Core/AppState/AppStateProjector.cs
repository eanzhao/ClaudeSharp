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
        string? sessionId = null,
        string? memoryRootDirectory = null,
        ManagedSettingsSnapshot? managedSettings = null,
        AnthropicTokenSourceSnapshot? activeTokenSource = null,
        McpConnectionManager? mcpConnectionManager = null,
        IAgentTaskRuntime? agentTaskRuntime = null,
        IAgentTeamRuntime? agentTeamRuntime = null,
        IAgentMailboxRuntime? agentMailboxRuntime = null)
    {
        var workItems = SnapshotWorkItems(agentTaskRuntime);

        return new AppStateSnapshot
        {
            SessionId = sessionId,
            WorkingDirectory = workingDirectory,
            PermissionMode = permissionMode,
            ActiveTaskId = SelectActiveTaskId(workItems),
            MemoryRootDirectory = memoryRootDirectory,
            ManagedSettings = managedSettings ?? ManagedSettingsSnapshot.Empty,
            ActiveTokenSource = activeTokenSource,
            McpConnections = SnapshotMcpConnections(mcpConnectionManager),
            WorkItems = workItems,
            Teams = SnapshotTeams(agentTeamRuntime),
            Mailboxes = SnapshotMailboxes(agentMailboxRuntime),
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
                AgentWorkItemStatus.Blocked => 1,
                AgentWorkItemStatus.Pending => 2,
                _ => 3,
            };

        return workItems
            .Where(entry => entry.Value is AgentWorkItemStatus.InProgress or AgentWorkItemStatus.Blocked or AgentWorkItemStatus.Pending)
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

    internal static IReadOnlyList<AppStateMailboxSnapshot> SnapshotMailboxes(
        IAgentMailboxRuntime? runtime)
    {
        if (runtime == null)
            return [];

        return runtime.ListMailboxes()
            .Select(mailbox => new AppStateMailboxSnapshot
            {
                Participant = mailbox.Participant,
                InboxCount = mailbox.InboxCount,
                UnreadCount = mailbox.UnreadCount,
                OutboxCount = mailbox.OutboxCount,
                ThreadCount = mailbox.ThreadCount,
                LatestThreadId = mailbox.LatestThreadId,
                LatestSubject = mailbox.LatestSubject,
                LatestCounterparty = mailbox.LatestCounterparty,
                LatestMessageAt = mailbox.LatestMessageAt,
            })
            .ToArray();
    }
}

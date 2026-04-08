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
        IAgentTaskRuntime? agentTaskRuntime = null)
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
}

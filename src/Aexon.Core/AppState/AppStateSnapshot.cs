using Aexon.Core.Agents;
using Aexon.Core.Channels;
using Aexon.Core.Configuration;
using Aexon.Core.Mcp;
using Aexon.Core.Permissions;

namespace Aexon.Core.AppState;

/// <summary>
/// Represents app state snapshot.
/// </summary>
public sealed record AppStateSnapshot
{
    public string? SessionId { get; init; }
    public string WorkingDirectory { get; init; } = Environment.CurrentDirectory;
    public PermissionMode PermissionMode { get; init; } = PermissionMode.Default;
    public AgentAutoResumeMode AutoResumeMode { get; init; } = AgentAutoResumeMode.Queue;
    public string? ActiveTaskId { get; init; }
    public string? MemoryRootDirectory { get; init; }
    public ManagedSettingsSnapshot ManagedSettings { get; init; } = ManagedSettingsSnapshot.Empty;
    public AnthropicTokenSourceSnapshot? ActiveTokenSource { get; init; }
    public IReadOnlyDictionary<string, McpConnectionState> McpConnections { get; init; } =
        new Dictionary<string, McpConnectionState>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<ChannelConnectionSnapshot> ChannelConnections { get; init; } = [];
    public IReadOnlyDictionary<string, AgentWorkItemStatus> WorkItems { get; init; } =
        new Dictionary<string, AgentWorkItemStatus>(StringComparer.OrdinalIgnoreCase);
    public AppStateTaskAttentionSnapshot TaskAttention { get; init; } = new();
    public IReadOnlyList<AppStateTeamSnapshot> Teams { get; init; } = [];
    public IReadOnlyList<AppStateMailboxSnapshot> Mailboxes { get; init; } = [];
    public IReadOnlyList<AppStateTodoSnapshot> Todos { get; init; } = [];
}

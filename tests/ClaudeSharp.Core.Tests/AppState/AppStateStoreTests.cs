using ClaudeSharp.Core.AppState;
using ClaudeSharp.Core.Agents;
using ClaudeSharp.Core.Mcp;
using ClaudeSharp.Core.Permissions;

namespace ClaudeSharp.Core.Tests.AppState;

/// <summary>
/// Contains tests for app State Store.
/// </summary>
public sealed class AppStateStoreTests
{
    [Fact]
    public void Store_UpdateAndResetEmitSnapshots()
    {
        var store = new AppStateStore(new AppStateSnapshot
        {
            SessionId = "session-1",
            WorkingDirectory = "/work",
            MemoryRootDirectory = "/mem",
        });

        var snapshots = new List<AppStateSnapshot>();
        store.Changed += snapshot => snapshots.Add(snapshot);

        var updated = store.Update(snapshot => snapshot with
        {
            PermissionMode = PermissionMode.Plan,
            ActiveTaskId = "task-1",
            McpConnections = new Dictionary<string, McpConnectionState>
            {
                ["server-a"] = McpConnectionState.Connected,
            },
            WorkItems = new Dictionary<string, AgentWorkItemStatus>
            {
                ["task-1"] = AgentWorkItemStatus.InProgress,
            },
        });

        Assert.Equal(PermissionMode.Plan, updated.PermissionMode);
        Assert.Equal("task-1", store.Current.ActiveTaskId);
        Assert.Equal("/mem", store.Current.MemoryRootDirectory);
        Assert.Single(snapshots);

        store.Reset();

        Assert.Equal(Environment.CurrentDirectory, store.Current.WorkingDirectory);
        Assert.Equal(2, snapshots.Count);
    }

    [Fact]
    public async Task HostBridge_PublishesCurrentSnapshot()
    {
        var store = new AppStateStore(new AppStateSnapshot
        {
            SessionId = "session-1",
            WorkingDirectory = "/work",
        });
        var boundary = new RecordingBoundary();
        var bridge = new AppStateHostBridge(store, boundary);

        await bridge.PublishAsync();

        store.Update(snapshot => snapshot with { SessionId = "session-2" });
        await bridge.PublishAsync();

        Assert.Equal(2, boundary.Snapshots.Count);
        Assert.Equal("session-1", boundary.Snapshots[0].SessionId);
        Assert.Equal("session-2", boundary.Snapshots[1].SessionId);
    }

    private sealed class RecordingBoundary : IAppStateHostBoundary
    {
        public List<AppStateSnapshot> Snapshots { get; } = [];

        public Task ApplyAsync(
            AppStateSnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            Snapshots.Add(snapshot);
            return Task.CompletedTask;
        }
    }
}

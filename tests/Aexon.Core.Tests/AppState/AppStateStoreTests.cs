using Aexon.Core.AppState;
using Aexon.Core.Agents;
using Aexon.Core.Configuration;
using Aexon.Core.Mcp;
using Aexon.Core.Permissions;

namespace Aexon.Core.Tests.AppState;

/// <summary>
/// Contains tests for app State Store.
/// </summary>
public sealed class AppStateStoreTests
{
    [Fact]
    public void Store_UpdateAndResetEmitSnapshots()
    {
        var managedSettings = new ManagedSettingsSnapshot
        {
            OrganizationPolicy = new OrganizationPolicySnapshot
            {
                OrganizationId = "org-1",
                WorkspaceId = "workspace-1",
                RequiresManagedAccess = true,
                AllowUserProvidedTokenSources = false,
                AllowWebSearch = false,
                AllowExternalMcpServers = false,
                AllowPlugins = false,
                AllowedProviderKinds = ["anthropic"],
            },
            TokenSources =
            [
                new AnthropicTokenSourceSnapshot
                {
                    Id = "environment",
                    Kind = AnthropicTokenSourceKind.EnvironmentVariable,
                    DisplayName = "Environment variable",
                    IsDefault = true,
                    IsActive = true,
                    Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["name"] = "ANTHROPIC_API_KEY",
                    },
                },
                new AnthropicTokenSourceSnapshot
                {
                    Id = "workspace",
                    Kind = AnthropicTokenSourceKind.Workspace,
                    ParentId = "environment",
                    DisplayName = "Workspace secret",
                },
            ],
        };

        var store = new AppStateStore(new AppStateSnapshot
        {
            SessionId = "session-1",
            WorkingDirectory = "/work",
            MemoryRootDirectory = "/mem",
            ManagedSettings = managedSettings,
            ActiveTokenSource = managedSettings.TokenSources[0],
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
        Assert.Equal("org-1", store.Current.ManagedSettings.OrganizationPolicy.OrganizationId);
        Assert.Equal("environment", store.Current.ActiveTokenSource?.Id);
        Assert.Single(snapshots);

        store.Reset();

        Assert.Equal(Environment.CurrentDirectory, store.Current.WorkingDirectory);
        Assert.Equal(ManagedSettingsSnapshot.Empty, store.Current.ManagedSettings);
        Assert.Null(store.Current.ActiveTokenSource);
        Assert.Equal(2, snapshots.Count);
    }

    [Fact]
    public void Reset_WithExplicitSnapshot_ReplacesManagedSettingsAndTokenSource()
    {
        var store = new AppStateStore(new AppStateSnapshot
        {
            SessionId = "session-1",
            ManagedSettings = new ManagedSettingsSnapshot
            {
                OrganizationPolicy = new OrganizationPolicySnapshot
                {
                    OrganizationId = "org-1",
                    RequiresManagedAccess = true,
                },
            },
            ActiveTokenSource = new AnthropicTokenSourceSnapshot
            {
                Id = "environment",
                Kind = AnthropicTokenSourceKind.EnvironmentVariable,
                IsActive = true,
            },
        });

        store.Reset(new AppStateSnapshot
        {
            SessionId = "session-2",
            WorkingDirectory = "/workspace",
            ManagedSettings = new ManagedSettingsSnapshot
            {
                OrganizationPolicy = new OrganizationPolicySnapshot
                {
                    OrganizationId = "org-2",
                    WorkspaceId = "workspace-2",
                    AllowWebSearch = false,
                },
                TokenSources =
                [
                    new AnthropicTokenSourceSnapshot
                    {
                        Id = "login",
                        Kind = AnthropicTokenSourceKind.UserLogin,
                        IsDefault = true,
                    },
                ],
            },
            ActiveTokenSource = new AnthropicTokenSourceSnapshot
            {
                Id = "login",
                Kind = AnthropicTokenSourceKind.UserLogin,
                IsDefault = true,
                IsActive = true,
            },
        });

        Assert.Equal("session-2", store.Current.SessionId);
        Assert.Equal("/workspace", store.Current.WorkingDirectory);
        Assert.Equal("org-2", store.Current.ManagedSettings.OrganizationPolicy.OrganizationId);
        Assert.Single(store.Current.ManagedSettings.TokenSources);
        Assert.Equal("login", store.Current.ActiveTokenSource?.Id);
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

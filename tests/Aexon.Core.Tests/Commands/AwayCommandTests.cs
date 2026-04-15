using Aexon.Commands;
using Aexon.Core.Agents;
using Aexon.Core.Commands;
using Aexon.Core.Context;
using Aexon.Core.Messages;
using Aexon.Core.Permissions;
using Aexon.Core.Query;
using Aexon.Core.Storage;
using Aexon.Core.Tests.Runtime;
using Aexon.Core.Tools;

namespace Aexon.Core.Tests.Commands;

public sealed class AwayCommandTests
{
    [Fact]
    public async Task AwayEnterAndExit_TogglesAwayModeState()
    {
        using var temp = new TempDirectory();
        using var bundle = CreateEngineBundle(temp.Root);
        var lines = new List<string>();
        var context = CreateContext(bundle.Engine, bundle.PermissionContext, lines);

        await new AwayCommand().ExecuteAsync("", context);
        var enterOutput = string.Join(Environment.NewLine, lines);
        Assert.Contains("Away mode enabled", enterOutput, StringComparison.Ordinal);
        Assert.True(bundle.Engine.IsAwayModeActive);

        lines.Clear();
        await new AwayCommand().ExecuteAsync("exit", context);
        var exitOutput = string.Join(Environment.NewLine, lines);
        Assert.Contains("Welcome back!", exitOutput, StringComparison.Ordinal);
        Assert.False(bundle.Engine.IsAwayModeActive);
    }

    [Fact]
    public async Task AwayStatus_ReportsCurrentState()
    {
        using var temp = new TempDirectory();
        using var bundle = CreateEngineBundle(temp.Root);
        var lines = new List<string>();
        var context = CreateContext(bundle.Engine, bundle.PermissionContext, lines);

        await new AwayCommand().ExecuteAsync("status", context);
        Assert.Contains("inactive", string.Join(Environment.NewLine, lines), StringComparison.Ordinal);

        lines.Clear();
        await new AwayCommand().ExecuteAsync("testing", context);
        lines.Clear();
        await new AwayCommand().ExecuteAsync("status", context);
        Assert.Contains("active", string.Join(Environment.NewLine, lines), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AwayWithReason_UsesProvidedReason()
    {
        using var temp = new TempDirectory();
        using var bundle = CreateEngineBundle(temp.Root);
        var lines = new List<string>();
        var context = CreateContext(bundle.Engine, bundle.PermissionContext, lines);

        await new AwayCommand().ExecuteAsync("going for coffee", context);
        Assert.Equal("going for coffee", bundle.Engine.AwayTriggerReason);
    }

    [Fact]
    public async Task AwayBack_SynonymForExit()
    {
        using var temp = new TempDirectory();
        using var bundle = CreateEngineBundle(temp.Root);
        var lines = new List<string>();
        var context = CreateContext(bundle.Engine, bundle.PermissionContext, lines);

        await new AwayCommand().ExecuteAsync("", context);
        lines.Clear();
        await new AwayCommand().ExecuteAsync("back", context);
        Assert.False(bundle.Engine.IsAwayModeActive);
    }

    private static CommandContext CreateContext(
        QueryEngine engine,
        PermissionContext permissionContext,
        List<string> lines) =>
        new()
        {
            WriteLine = lines.Add,
            Tools = new ToolRegistry(),
            QueryEngine = engine,
            PermissionContext = permissionContext,
            AgentTaskRuntime = new InMemoryAgentTaskRuntime(),
            Commands = [],
            CancellationToken = CancellationToken.None,
        };

    private static EngineBundle CreateEngineBundle(
        string workingDirectory)
    {
        var permissionContext = new PermissionContext
        {
            WorkingDirectory = workingDirectory,
        };
        var journal = new RecordingJournal();
        var provider = new ContextProvider
        {
            WorkingDirectory = workingDirectory,
            PermissionContext = permissionContext,
        };

        var engine = TestSupport.CreateQueryEngine(
            TestSupport.CreateChatClient(new FakeAnthropicHandler()),
            new ToolRegistry(),
            provider,
            new DefaultPermissionChecker(),
            new QueryEngineConfig
            {
                EnableAutoCompact = false,
            },
            journal: journal);

        return new EngineBundle(engine, journal, permissionContext);
    }

    private sealed record EngineBundle(
        QueryEngine Engine,
        RecordingJournal Journal,
        PermissionContext PermissionContext) : IDisposable
    {
        public void Dispose() => Engine.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}

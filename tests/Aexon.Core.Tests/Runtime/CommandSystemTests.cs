using Aexon.Core.Agents;
using Aexon.Core.Commands;
using Aexon.Core.Tools;

namespace Aexon.Core.Tests.Runtime;

/// <summary>
/// Contains tests for command System.
/// </summary>
public sealed class CommandSystemTests
{
    [Fact]
    public void Register_ResolvesAliases_AndDeduplicatesCommands()
    {
        var registry = new CommandRegistry();
        registry.Register(new TestCommand("alpha", ["a", "first"]));
        registry.Register(new TestCommand("beta", ["b"]));

        Assert.Same(registry.Get("/alpha"), registry.Get("/a"));
        Assert.Same(registry.Get("/beta"), registry.Get("b"));
        Assert.Equal(["alpha", "beta"], registry.GetAll().Select(command => command.Name));
    }

    [Fact]
    public void IsCommand_ReturnsFalseForUnknownOrNonCommandInputs()
    {
        var registry = new CommandRegistry();
        registry.Register(new TestCommand("alpha"));

        Assert.False(registry.IsCommand("alpha"));
        Assert.False(registry.IsCommand("/missing"));
    }

    [Fact]
    public void CommandContext_WiresDependencies()
    {
        var tools = new ToolRegistry();
        tools.Register(new FakeTool { Name = "alpha" });

        var context = new CommandContext
        {
            WriteLine = _ => { },
            Tools = tools,
            QueryEngine = null!,
            PermissionContext = new Aexon.Core.Permissions.PermissionContext(),
            AgentTaskRuntime = new InMemoryAgentTaskRuntime(),
            AgentRuntimeOptions = new AgentRuntimeOptions
            {
                AutoResumeMode = AgentAutoResumeMode.Disabled,
            },
            AgentTeamRuntime = new InMemoryAgentTeamRuntime(),
            Commands = [new TestCommand("alpha")],
            DelayAsync = (_, _) => Task.CompletedTask,
            CancellationToken = CancellationToken.None,
            RequestExit = () => { },
            RequestClear = () => { },
        };

        Assert.NotNull(context.Tools.Get("alpha"));
        Assert.Single(context.Commands);
        Assert.NotNull(context.AgentTaskRuntime);
        Assert.NotNull(context.AgentTeamRuntime);
        Assert.Equal(AgentAutoResumeMode.Disabled, context.CurrentAgentAutoResumeMode);
        Assert.NotNull(context.DelayAsync);
        Assert.NotNull(context.RequestExit);
        Assert.NotNull(context.RequestClear);
    }

    private sealed class TestCommand : ICommand
    {
        public TestCommand(string name, string[]? aliases = null)
        {
            Name = name;
            Aliases = aliases ?? [];
        }

        public string Name { get; }
        public string Description => Name;
        public string[] Aliases { get; }

        public Task ExecuteAsync(string args, CommandContext context) =>
            Task.CompletedTask;
    }
}

using ClaudeSharp.Core.Commands;
using ClaudeSharp.Core.Tools;

namespace ClaudeSharp.Core.Tests.Runtime;

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
            PermissionContext = new ClaudeSharp.Core.Permissions.PermissionContext(),
            Commands = [new TestCommand("alpha")],
            RequestExit = () => { },
            RequestClear = () => { },
        };

        Assert.NotNull(context.Tools.Get("alpha"));
        Assert.Single(context.Commands);
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

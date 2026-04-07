using ClaudeSharp.Core.Commands;
using ClaudeSharp.Core.Tools;

namespace ClaudeSharp.Core.Tests.Foundations;

public sealed class CommandSystemTests
{
    [Fact]
    public void ICommand_DefaultMembers_ExposeEmptyAliases_AndRequiresArgsFalse()
    {
        ICommand command = new MinimalCommand();

        Assert.Empty(command.Aliases);
        Assert.False(command.RequiresArgs);
    }

    [Fact]
    public void Register_ResolvesAliases_AndGetAllRemovesDuplicates()
    {
        var registry = new CommandRegistry();
        registry.Register(new TestCommand("alpha", ["a", "first"]));
        registry.Register(new TestCommand("beta", ["b"]));

        Assert.NotNull(registry.Get("/alpha"));
        Assert.NotNull(registry.Get("/A"));
        Assert.NotNull(registry.Get("first"));
        Assert.NotNull(registry.Get("/b"));
        Assert.Equal(["alpha", "beta"], registry.GetAll().Select(command => command.Name));
    }

    [Theory]
    [InlineData("/alpha")]
    [InlineData("/alpha run")]
    public void IsCommand_ReturnsTrueForRegisteredCommands(string input)
    {
        var registry = new CommandRegistry();
        registry.Register(new TestCommand("alpha"));

        Assert.True(registry.IsCommand(input));
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
    public void CommandContext_ExposesWiredDependencies()
    {
        var tool = FoundationTestHelpers.Tool("alpha");
        var registry = new ToolRegistry();
        registry.Register(tool);
        var context = new CommandContext
        {
            WriteLine = _ => { },
            Tools = registry,
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

        public Task ExecuteAsync(string args, CommandContext context) => Task.CompletedTask;
    }

    private sealed class MinimalCommand : ICommand
    {
        public string Name => "minimal";
        public string Description => "minimal";

        public Task ExecuteAsync(string args, CommandContext context) => Task.CompletedTask;
    }
}

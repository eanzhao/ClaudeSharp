using System.Text.Json;
using Aexon.Cli;
using Aexon.Core.Commands;
using Aexon.Core.Hooks;
using Aexon.Core.Storage;
using Aexon.Core.Tests.Runtime;
using Aexon.Core.Tools;

namespace Aexon.Core.Tests.Extensions;

public class SessionBuilderTests
{
    [Fact]
    public void RegisterTool_AddsToRegistry()
    {
        var (builder, tools, _, _) = CreateBuilder();
        var tool = new FakeTool { Name = "alpha" };

        builder.RegisterTool(tool);

        Assert.Same(tool, tools.Get("alpha"));
    }

    [Fact]
    public void RegisterTool_CollisionWithExistingTool_Throws()
    {
        var (builder, tools, _, _) = CreateBuilder();
        tools.Register(new FakeTool { Name = "alpha" });

        var ex = Assert.Throws<InvalidOperationException>(
            () => builder.RegisterTool(new FakeTool { Name = "alpha" }));
        Assert.Contains("alpha", ex.Message);
    }

    [Fact]
    public void RegisterTool_CollisionWithDeferredTool_Throws()
    {
        var (builder, tools, _, _) = CreateBuilder();
        tools.RegisterDeferred(new DeferredToolRegistration(
            "beta",
            () => new FakeTool { Name = "beta" }));

        Assert.Throws<InvalidOperationException>(
            () => builder.RegisterTool(new FakeTool { Name = "beta" }));
    }

    [Fact]
    public void RegisterCommand_AddsToRegistry()
    {
        var (builder, _, commands, _) = CreateBuilder();
        var command = new FakeCommand("ping");

        builder.RegisterCommand(command);

        Assert.Same(command, commands.Get("ping"));
    }

    [Fact]
    public void RegisterCommand_CollisionThrows()
    {
        var (builder, _, commands, _) = CreateBuilder();
        commands.Register(new FakeCommand("ping"));

        Assert.Throws<InvalidOperationException>(
            () => builder.RegisterCommand(new FakeCommand("ping")));
    }

    [Fact]
    public void RegisterHookObserver_AddsToHookRuntime()
    {
        var (builder, _, _, hooks) = CreateBuilder();
        var observer = new CountingObserver();

        builder.RegisterHookObserver(observer);

        // Observer should get called on session end.
        _ = hooks.OnSessionEndAsync(
            new SessionEndHookContext(
                sessionId: "session-end",
                workingDirectory: "/tmp",
                model: "claude-sonnet-4-6",
                metadata: new ConversationSessionMetadata(),
                messageCount: 0,
                dueToClear: false),
            CancellationToken.None);
        Assert.Equal(1, observer.SessionEndCalls);
    }

    [Fact]
    public void AppendSystemPromptFragment_AccumulatesInOrder()
    {
        var (builder, _, _, _) = CreateBuilder();

        builder.AppendSystemPromptFragment("first");
        builder.AppendSystemPromptFragment("second");

        Assert.Equal(["first", "second"], builder.PromptFragments);
    }

    [Fact]
    public void AppendSystemPromptFragment_IgnoresEmptyOrWhitespace()
    {
        var (builder, _, _, _) = CreateBuilder();

        builder.AppendSystemPromptFragment("");
        builder.AppendSystemPromptFragment("   ");
        builder.AppendSystemPromptFragment(null!);

        Assert.Empty(builder.PromptFragments);
    }

    private static (SessionBuilder Builder, ToolRegistry Tools, CommandRegistry Commands, HookRuntime Hooks) CreateBuilder()
    {
        var tools = new ToolRegistry();
        var commands = new CommandRegistry();
        var hooks = new HookRuntime();
        var builder = new SessionBuilder("/tmp", "claude-sonnet-4-6", tools, commands, hooks);
        return (builder, tools, commands, hooks);
    }

    private sealed class FakeCommand(string name) : ICommand
    {
        public string Name => name;
        public string Description => "fake command";
        public Task ExecuteAsync(string args, CommandContext context) => Task.CompletedTask;
    }

    private sealed class CountingObserver : HookObserver
    {
        public int SessionEndCalls { get; private set; }

        public override ValueTask OnSessionEndAsync(
            SessionEndHookContext context,
            CancellationToken cancellationToken = default)
        {
            SessionEndCalls++;
            return ValueTask.CompletedTask;
        }
    }
}

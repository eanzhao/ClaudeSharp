using Aexon.Core.Commands;
using Aexon.Core.Extensions;
using Aexon.Core.Hooks;
using Aexon.Core.Tools;

namespace Aexon.Cli;

/// <summary>
/// Concrete <see cref="IAexonSessionBuilder"/> that wires extension
/// registrations through to the CLI-owned registries. Discarded after all
/// extensions have configured the session.
/// </summary>
internal sealed class SessionBuilder : IAexonSessionBuilder
{
    private readonly ToolRegistry _tools;
    private readonly CommandRegistry _commands;
    private readonly HookRuntime _hooks;
    private readonly List<string> _promptFragments = [];

    public SessionBuilder(
        string workingDirectory,
        string model,
        ToolRegistry tools,
        CommandRegistry commands,
        HookRuntime hooks)
    {
        WorkingDirectory = workingDirectory ?? throw new ArgumentNullException(nameof(workingDirectory));
        Model = model ?? throw new ArgumentNullException(nameof(model));
        _tools = tools ?? throw new ArgumentNullException(nameof(tools));
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        _hooks = hooks ?? throw new ArgumentNullException(nameof(hooks));
    }

    public string WorkingDirectory { get; }

    public string Model { get; }

    public IReadOnlyList<string> PromptFragments => _promptFragments;

    public void RegisterTool(ITool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        if (_tools.GetRegisteredTools().Any(entry =>
                string.Equals(entry.Name, tool.Name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Tool '{tool.Name}' is already registered; extensions cannot override existing tools.");
        }

        _tools.Register(tool);
    }

    public void RegisterCommand(ICommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (_commands.Get(command.Name) != null)
        {
            throw new InvalidOperationException(
                $"Command '{command.Name}' is already registered; extensions cannot override existing commands.");
        }

        _commands.Register(command);
    }

    public void RegisterHookObserver(HookObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        _hooks.Register(observer);
    }

    public void AppendSystemPromptFragment(string fragment)
    {
        if (string.IsNullOrWhiteSpace(fragment))
            return;
        _promptFragments.Add(fragment);
    }
}

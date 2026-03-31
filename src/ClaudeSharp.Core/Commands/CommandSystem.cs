namespace ClaudeSharp.Core.Commands;

/// <summary>
/// 斜杠命令接口 — 对应 Claude Code 的 Command (commands.ts)
///
/// Claude Code 有 86+ 个命令，每个命令是独立模块
/// </summary>
public interface ICommand
{
    string Name { get; }
    string Description { get; }
    string[] Aliases => [];

    /// <summary>是否需要参数</summary>
    bool RequiresArgs => false;

    /// <summary>执行命令</summary>
    Task ExecuteAsync(string args, CommandContext context);
}

/// <summary>
/// 命令执行上下文
/// </summary>
public class CommandContext
{
    public required Action<string> WriteLine { get; init; }
    public required Tools.ToolRegistry Tools { get; init; }
    public required Query.QueryEngine QueryEngine { get; init; }
    public required Permissions.PermissionContext PermissionContext { get; init; }
    public required IReadOnlyList<ICommand> Commands { get; init; }
    public Action? RequestExit { get; init; }
    public Action? RequestClear { get; init; }
}

/// <summary>
/// 命令注册表 — 对应 Claude Code 的 commands.ts (getCommands)
/// </summary>
public class CommandRegistry
{
    private readonly Dictionary<string, ICommand> _commands = new(StringComparer.OrdinalIgnoreCase);

    public void Register(ICommand command)
    {
        _commands[command.Name] = command;
        foreach (var alias in command.Aliases)
            _commands[alias] = command;
    }

    public ICommand? Get(string name)
    {
        name = name.TrimStart('/');
        _commands.TryGetValue(name, out var command);
        return command;
    }

    public IReadOnlyList<ICommand> GetAll() =>
        _commands.Values.Distinct().OrderBy(c => c.Name).ToList();

    public bool IsCommand(string input) =>
        input.StartsWith('/') && Get(input.Split(' ')[0].TrimStart('/')) != null;
}

using ClaudeSharp.Core.Commands;
using ClaudeSharp.Core.Query;

namespace ClaudeSharp.Commands;

public class HelpCommand : ICommand
{
    public string Name => "help";
    public string Description => "Show available commands";

    public Task ExecuteAsync(string args, CommandContext context)
    {
        context.WriteLine("\n  Available commands:\n");
        foreach (var cmd in context.Commands)
        {
            var aliases = cmd.Aliases.Length > 0
                ? $" (aliases: {string.Join(", ", cmd.Aliases.Select(a => "/" + a))})"
                : "";
            context.WriteLine($"    /{cmd.Name,-16} {cmd.Description}{aliases}");
        }
        context.WriteLine("");
        return Task.CompletedTask;
    }
}

public class ClearCommand : ICommand
{
    public string Name => "clear";
    public string Description => "Clear conversation history";

    public Task ExecuteAsync(string args, CommandContext context)
    {
        context.QueryEngine.ClearMessages();
        context.RequestClear?.Invoke();
        context.WriteLine("  Conversation cleared.");
        return Task.CompletedTask;
    }
}

public class CostCommand : ICommand
{
    public string Name => "cost";
    public string Description => "Show token usage and estimated cost";

    public Task ExecuteAsync(string args, CommandContext context)
    {
        var usage = context.QueryEngine.TotalUsage;
        var messages = context.QueryEngine.Messages;

        // 简化的成本估算 (Sonnet 定价)
        var inputCost = usage.InputTokens * 3.0 / 1_000_000;
        var outputCost = usage.OutputTokens * 15.0 / 1_000_000;
        var cacheCost = usage.CacheReadInputTokens * 0.3 / 1_000_000;
        var totalCost = inputCost + outputCost + cacheCost;

        context.WriteLine($"""

          Token Usage:
            Input:       {usage.InputTokens,10:N0}  (${inputCost:F4})
            Output:      {usage.OutputTokens,10:N0}  (${outputCost:F4})
            Cache Read:  {usage.CacheReadInputTokens,10:N0}  (${cacheCost:F4})
            ───────────────────────────
            Total:       {usage.TotalTokens,10:N0}  (${totalCost:F4})
            Messages:    {messages.Count,10:N0}
        """);
        return Task.CompletedTask;
    }
}

public class ExitCommand : ICommand
{
    public string Name => "exit";
    public string Description => "Exit ClaudeSharp";
    public string[] Aliases => ["quit", "q"];

    public Task ExecuteAsync(string args, CommandContext context)
    {
        context.RequestExit?.Invoke();
        return Task.CompletedTask;
    }
}

public class ModelCommand : ICommand
{
    public string Name => "model";
    public string Description => "Show or switch the current model";

    public Task ExecuteAsync(string args, CommandContext context)
    {
        if (string.IsNullOrWhiteSpace(args))
        {
            context.WriteLine($"  Current model: {context.QueryEngine.CurrentModel}");
            context.WriteLine($"  Common aliases: {string.Join(", ", ClaudeModels.CommonAliases)}");
        }
        else
        {
            var resolved = context.QueryEngine.SetModel(args);
            context.WriteLine($"  Switched to: {resolved}");
        }
        return Task.CompletedTask;
    }
}

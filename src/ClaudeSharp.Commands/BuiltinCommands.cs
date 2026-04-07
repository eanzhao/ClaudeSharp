using ClaudeSharp.Core.Commands;
using ClaudeSharp.Core.Permissions;
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

    public async Task ExecuteAsync(string args, CommandContext context)
    {
        await context.QueryEngine.ClearMessagesAsync();
        context.RequestClear?.Invoke();
        context.WriteLine("  Conversation cleared.");
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

    public async Task ExecuteAsync(string args, CommandContext context)
    {
        if (string.IsNullOrWhiteSpace(args))
        {
            context.WriteLine($"  Current model: {context.QueryEngine.CurrentModel}");
            context.WriteLine($"  Common aliases: {string.Join(", ", ClaudeModels.CommonAliases)}");
        }
        else
        {
            var resolved = await context.QueryEngine.SetModelAsync(args);
            context.WriteLine($"  Switched to: {resolved}");
        }
    }
}

public class SessionCommand : ICommand
{
    public string Name => "session";
    public string Description => "Show current session metadata and transcript path";

    public Task ExecuteAsync(string args, CommandContext context)
    {
        var metadata = context.QueryEngine.SessionMetadata;
        var mode = metadata.Mode ?? context.PermissionContext.Mode;

        context.WriteLine($"  Session: {context.QueryEngine.SessionId ?? "(ephemeral)"}");
        if (!string.IsNullOrWhiteSpace(context.QueryEngine.TranscriptPath))
            context.WriteLine($"  Transcript: {context.QueryEngine.TranscriptPath}");
        context.WriteLine($"  Title: {metadata.Title ?? "(none)"}");
        context.WriteLine(
            metadata.Tags.Count == 0
                ? "  Tags: (none)"
                : $"  Tags: {string.Join(", ", metadata.Tags.OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase))}");
        context.WriteLine($"  Mode: {mode}");

        return Task.CompletedTask;
    }
}

public class ModeCommand : ICommand
{
    public string Name => "mode";
    public string Description => "Show or switch the current permission mode";

    public async Task ExecuteAsync(string args, CommandContext context)
    {
        if (string.IsNullOrWhiteSpace(args))
        {
            var current = context.QueryEngine.SessionMetadata.Mode ?? context.PermissionContext.Mode;
            context.WriteLine($"  Current mode: {current}");
            context.WriteLine("  Available modes: Default, Plan, Auto, Bypass");
            return;
        }

        if (!Enum.TryParse<PermissionMode>(args.Trim(), true, out var mode))
        {
            context.WriteLine($"  Unknown mode: {args.Trim()}");
            context.WriteLine("  Available modes: Default, Plan, Auto, Bypass");
            return;
        }

        await context.QueryEngine.SetPermissionModeAsync(mode);
        context.WriteLine($"  Switched permission mode to: {mode}");
    }
}

public class TitleCommand : ICommand
{
    public string Name => "title";
    public string Description => "Show, set, or clear the current session title";

    public async Task ExecuteAsync(string args, CommandContext context)
    {
        var trimmed = args.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            context.WriteLine($"  Current title: {context.QueryEngine.SessionMetadata.Title ?? "(none)"}");
            return;
        }

        if (string.Equals(trimmed, "clear", StringComparison.OrdinalIgnoreCase))
        {
            await context.QueryEngine.SetSessionTitleAsync(null);
            context.WriteLine("  Session title cleared.");
            return;
        }

        await context.QueryEngine.SetSessionTitleAsync(trimmed);
        context.WriteLine($"  Session title set to: {trimmed}");
    }
}

public class TagCommand : ICommand
{
    public string Name => "tag";
    public string Description => "Show or manage session tags";

    public async Task ExecuteAsync(string args, CommandContext context)
    {
        var trimmed = args.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            var tags = context.QueryEngine.SessionMetadata.Tags;
            context.WriteLine(
                tags.Count == 0
                    ? "  Tags: (none)"
                    : $"  Tags: {string.Join(", ", tags.OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase))}");
            context.WriteLine("  Usage: /tag add <name>, /tag remove <name>, /tag clear");
            return;
        }

        var parts = trimmed.Split(' ', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var action = parts[0];
        var value = parts.Length > 1 ? parts[1].Trim() : string.Empty;

        switch (action.ToLowerInvariant())
        {
            case "add":
                if (string.IsNullOrWhiteSpace(value))
                {
                    context.WriteLine("  Usage: /tag add <name>");
                    return;
                }

                await context.QueryEngine.AddSessionTagAsync(value);
                context.WriteLine($"  Added tag: {value}");
                break;

            case "remove":
            case "rm":
            case "delete":
                if (string.IsNullOrWhiteSpace(value))
                {
                    context.WriteLine("  Usage: /tag remove <name>");
                    return;
                }

                await context.QueryEngine.RemoveSessionTagAsync(value);
                context.WriteLine($"  Removed tag: {value}");
                break;

            case "clear":
                await context.QueryEngine.ClearSessionTagsAsync();
                context.WriteLine("  Cleared all session tags.");
                break;

            default:
                context.WriteLine("  Usage: /tag add <name>, /tag remove <name>, /tag clear");
                break;
        }
    }
}

public class CompactCommand : ICommand
{
    public string Name => "compact";
    public string Description => "Compact older conversation history into a resumable checkpoint";

    public async Task ExecuteAsync(string args, CommandContext context)
    {
        var preserveTailCount = 8;
        if (!string.IsNullOrWhiteSpace(args) &&
            !int.TryParse(args.Trim(), out preserveTailCount))
        {
            context.WriteLine("  Usage: /compact [preserveTailCount]");
            return;
        }

        var result = await context.QueryEngine.CompactAsync(preserveTailCount);
        if (result == null)
        {
            context.WriteLine("  Not enough history to compact yet.");
            return;
        }

        context.WriteLine(
            $"  Compacted {result.RemovedMessageCount} messages and kept {result.ActiveMessages.Count - 1} recent messages in full.");
    }
}

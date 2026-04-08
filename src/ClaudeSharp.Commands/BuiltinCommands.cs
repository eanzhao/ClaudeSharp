using ClaudeSharp.Core.Agents;
using ClaudeSharp.Core.Commands;
using ClaudeSharp.Core.Compaction;
using ClaudeSharp.Core.Permissions;
using ClaudeSharp.Core.Query;

namespace ClaudeSharp.Commands;

/// <summary>
/// Represents help command.
/// </summary>
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

/// <summary>
/// Represents clear command.
/// </summary>
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

/// <summary>
/// Represents cost command.
/// </summary>
public class CostCommand : ICommand
{
    public string Name => "cost";
    public string Description => "Show token usage and estimated cost";

    public Task ExecuteAsync(string args, CommandContext context)
    {
        var usage = context.QueryEngine.TotalUsage;
        var messages = context.QueryEngine.Messages;

        // Simplified cost estimate using Sonnet pricing.
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

/// <summary>
/// Represents exit command.
/// </summary>
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

/// <summary>
/// Represents agents command.
/// </summary>
public class AgentsCommand : ICommand
{
    public string Name => "agents";
    public string Description => "Show subagent work items and background runs";

    public Task ExecuteAsync(string args, CommandContext context)
    {
        var trimmed = args.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            context.WriteLine(AgentStatusFormatter.FormatOverview(context.AgentTaskRuntime));
            return Task.CompletedTask;
        }

        if (AgentStatusFormatter.TryFormatDetails(
                context.AgentTaskRuntime,
                trimmed,
                includeOutput: true,
                out var details))
        {
            context.WriteLine(details);
            return Task.CompletedTask;
        }

        context.WriteLine(details);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Represents model command.
/// </summary>
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

/// <summary>
/// Represents session command.
/// </summary>
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

/// <summary>
/// Represents mode command.
/// </summary>
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

/// <summary>
/// Represents title command.
/// </summary>
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

/// <summary>
/// Represents tag command.
/// </summary>
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

/// <summary>
/// Represents compact command.
/// </summary>
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

/// <summary>
/// Represents session memory compact command.
/// </summary>
public class SessionMemoryCompactCommand : ICommand
{
    public string Name => "session-memory";
    public string Description => "Fold older history into a session-memory summary while keeping recent messages verbatim";
    public string[] Aliases => ["smcompact"];

    public async Task ExecuteAsync(string args, CommandContext context)
    {
        var preserveTailCount = 8;
        if (!string.IsNullOrWhiteSpace(args) &&
            !int.TryParse(args.Trim(), out preserveTailCount))
        {
            context.WriteLine("  Usage: /session-memory [preserveTailCount]");
            return;
        }

        var result = await context.QueryEngine.SessionMemoryCompactAsync(preserveTailCount);
        if (result == null)
        {
            context.WriteLine("  Not enough history to build a session-memory checkpoint yet.");
            return;
        }

        var boundaryNote = result.RewriteResult.Boundary.WasAdjusted
            ? $" Boundary adjusted from {result.RewriteResult.Boundary.RequestedIndex} to {result.RewriteResult.Boundary.AppliedIndex} to keep tool protocol intact."
            : string.Empty;

        context.WriteLine(
            $"  Folded {result.FoldedMessageCount} older messages into session memory and kept {result.ActiveMessages.Count - 1} recent messages verbatim.{boundaryNote}");
    }
}

/// <summary>
/// Represents partial compact command.
/// </summary>
public class PartialCompactCommand : ICommand
{
    public string Name => "pcompact";
    public string Description => "Compact a selected message range with from/up_to boundaries";
    public string[] Aliases => ["partial-compact"];

    public async Task ExecuteAsync(string args, CommandContext context)
    {
        var parts = args.Split(' ', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !int.TryParse(parts[1], out var index))
        {
            context.WriteLine("  Usage: /pcompact <up_to|from> <index>");
            return;
        }

        ConversationCompactionResult? result = parts[0].ToLowerInvariant() switch
        {
            "up_to" or "upto" => await context.QueryEngine.CompactUpToAsync(index),
            "from" => await context.QueryEngine.CompactFromAsync(index),
            _ => null,
        };

        if (result == null)
        {
            if (parts[0].Equals("up_to", StringComparison.OrdinalIgnoreCase) ||
                parts[0].Equals("upto", StringComparison.OrdinalIgnoreCase) ||
                parts[0].Equals("from", StringComparison.OrdinalIgnoreCase))
            {
                context.WriteLine("  No messages were compacted for that boundary.");
            }
            else
            {
                context.WriteLine("  Usage: /pcompact <up_to|from> <index>");
            }

            return;
        }

        var boundary = result.RewriteResult?.Boundary;
        var adjusted = boundary?.WasAdjusted == true
            ? $" Boundary adjusted from {boundary.RequestedIndex} to {boundary.AppliedIndex} to preserve tool_use/tool_result pairs."
            : string.Empty;

        context.WriteLine(
            $"  Compacted {result.RemovedMessageCount} messages with {parts[0]}={index}.{adjusted}");
    }
}

/// <summary>
/// Represents microcompact command.
/// </summary>
public class MicrocompactCommand : ICommand
{
    public string Name => "microcompact";
    public string Description => "Clear old tool results and thinking blocks without rewriting the whole conversation";

    public async Task ExecuteAsync(string args, CommandContext context)
    {
        var preserveTailCount = 8;
        if (!string.IsNullOrWhiteSpace(args) &&
            !int.TryParse(args.Trim(), out preserveTailCount))
        {
            context.WriteLine("  Usage: /microcompact [preserveTailCount]");
            return;
        }

        var result = await context.QueryEngine.MicrocompactAsync(preserveTailCount);
        if (result == null)
        {
            context.WriteLine("  No old tool results or thinking blocks needed clearing.");
            return;
        }

        context.WriteLine(
            $"  Cleared {result.ClearedToolResultCount} tool-result messages and {result.ClearedThinkingBlockCount} thinking blocks.");
    }
}

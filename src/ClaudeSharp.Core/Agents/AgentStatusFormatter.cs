using System.Text;

namespace ClaudeSharp.Core.Agents;

/// <summary>
/// Formats agent work item and background run state for tools and commands.
/// </summary>
public static class AgentStatusFormatter
{
    public static string FormatOverview(IAgentTaskRuntime runtime)
    {
        var workItems = runtime.ListWorkItems()
            .OrderByDescending(item => item.UpdatedAt)
            .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var backgroundRuns = runtime.ListBackgroundRuns()
            .OrderByDescending(run => run.UpdatedAt)
            .ThenBy(run => run.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (workItems.Length == 0 && backgroundRuns.Length == 0)
            return "No agent work items or background runs exist yet.";

        var builder = new StringBuilder();

        if (workItems.Length > 0)
        {
            builder.AppendLine("Work items:");
            foreach (var item in workItems)
                builder.AppendLine($"- {item.Id} [{item.Status}] {item.Title}");
        }

        if (backgroundRuns.Length > 0)
        {
            if (builder.Length > 0)
                builder.AppendLine();

            builder.AppendLine("Background runs:");
            foreach (var run in backgroundRuns)
                builder.AppendLine($"- {run.Id} [{run.Status}] {run.Name}");
        }

        return builder.ToString().TrimEnd();
    }

    public static bool TryFormatDetails(
        IAgentTaskRuntime runtime,
        string id,
        bool includeOutput,
        int? outputOffset,
        int? outputLimit,
        out string details)
    {
        if (runtime.GetWorkItem(id) is { } workItem)
        {
            details = FormatWorkItem(workItem);
            return true;
        }

        if (runtime.GetBackgroundRun(id) is { } backgroundRun)
        {
            details = FormatBackgroundRun(
                backgroundRun,
                includeOutput,
                outputOffset,
                outputLimit);
            return true;
        }

        details = $"No agent work item or background run matched id '{id}'.";
        return false;
    }

    private static string FormatWorkItem(AgentWorkItem item)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Work item: {item.Id}");
        builder.AppendLine($"Title: {item.Title}");
        if (!string.IsNullOrWhiteSpace(item.Description))
            builder.AppendLine($"Description: {item.Description}");
        if (!string.IsNullOrWhiteSpace(item.Owner))
            builder.AppendLine($"Owner: {item.Owner}");
        builder.AppendLine($"Status: {item.Status}");
        builder.AppendLine($"Created: {item.CreatedAt:O}");
        builder.AppendLine($"Updated: {item.UpdatedAt:O}");

        if (item.Blocks.Count > 0)
            builder.AppendLine($"Blocks: {string.Join(", ", item.Blocks)}");
        if (item.BlockedBy.Count > 0)
            builder.AppendLine($"Blocked by: {string.Join(", ", item.BlockedBy)}");

        return builder.ToString().TrimEnd();
    }

    private static string FormatBackgroundRun(
        AgentBackgroundRun run,
        bool includeOutput,
        int? outputOffset,
        int? outputLimit)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Background run: {run.Id}");
        builder.AppendLine($"Name: {run.Name}");
        if (!string.IsNullOrWhiteSpace(run.Owner))
            builder.AppendLine($"Owner: {run.Owner}");
        builder.AppendLine($"Status: {run.Status}");
        builder.AppendLine($"Started: {run.StartedAt:O}");
        builder.AppendLine($"Updated: {run.UpdatedAt:O}");
        if (run.StoppedAt is not null)
            builder.AppendLine($"Stopped: {run.StoppedAt:O}");
        if (!string.IsNullOrWhiteSpace(run.StopReason))
            builder.AppendLine($"Stop reason: {run.StopReason}");

        if (includeOutput && run.Output.Count > 0)
        {
            var offset = Math.Max(0, outputOffset ?? 0);
            var limit = outputLimit.HasValue
                ? Math.Max(1, outputLimit.Value)
                : int.MaxValue;
            var page = run.Output.Skip(offset).Take(limit).ToArray();
            var start = page.Length == 0 ? 0 : offset + 1;
            var end = page.Length == 0 ? 0 : offset + page.Length;

            builder.AppendLine();
            builder.AppendLine("Output:");
            if (page.Length == 0)
            {
                builder.AppendLine(
                    $"No output entries at or after offset {offset}. Total output entries: {run.Output.Count}.");
            }
            else
            {
                builder.AppendLine(
                    page.Length == run.Output.Count && offset == 0
                        ? $"Showing all {run.Output.Count} output entr{(run.Output.Count == 1 ? "y" : "ies")}."
                        : $"Showing output entries {start}-{end} of {run.Output.Count}.");
            }

            foreach (var chunk in page)
            {
                builder.AppendLine("---");
                builder.AppendLine(chunk);
            }
        }

        return builder.ToString().TrimEnd();
    }
}

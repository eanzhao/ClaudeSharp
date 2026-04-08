using System.Text;

namespace ClaudeSharp.Core.Agents;

/// <summary>
/// Defines list kinds supported by agent status overviews.
/// </summary>
public enum AgentStatusOverviewKind
{
    All,
    WorkItems,
    BackgroundRuns,
}

/// <summary>
/// Represents overview filtering and pagination options for agent status listings.
/// </summary>
public sealed record AgentStatusOverviewOptions
{
    public AgentStatusOverviewKind Kind { get; init; } = AgentStatusOverviewKind.All;
    public string? Status { get; init; }
    public string? Owner { get; init; }
    public int Offset { get; init; }
    public int? Limit { get; init; }
}

/// <summary>
/// Formats agent work item and background run state for tools and commands.
/// </summary>
public static class AgentStatusFormatter
{
    public static string FormatOverview(
        IAgentTaskRuntime runtime,
        AgentStatusOverviewOptions? options = null)
    {
        var resolvedOptions = options ?? new AgentStatusOverviewOptions();
        var workItems = runtime.ListWorkItems()
            .OrderByDescending(item => item.UpdatedAt)
            .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .Where(item => MatchesOwner(item.Owner, resolvedOptions.Owner))
            .Where(item => MatchesStatus(item.Status, resolvedOptions.Status))
            .ToArray();
        var backgroundRuns = runtime.ListBackgroundRuns()
            .OrderByDescending(run => run.UpdatedAt)
            .ThenBy(run => run.Id, StringComparer.OrdinalIgnoreCase)
            .Where(run => MatchesOwner(run.Owner, resolvedOptions.Owner))
            .Where(run => MatchesStatus(run.Status, resolvedOptions.Status))
            .ToArray();

        var builder = new StringBuilder();
        var includeWorkItems = resolvedOptions.Kind is AgentStatusOverviewKind.All or AgentStatusOverviewKind.WorkItems;
        var includeBackgroundRuns = resolvedOptions.Kind is AgentStatusOverviewKind.All or AgentStatusOverviewKind.BackgroundRuns;

        if (includeWorkItems)
        {
            AppendWorkItems(builder, workItems, resolvedOptions);
        }

        if (includeBackgroundRuns)
        {
            if (builder.Length > 0)
                builder.AppendLine();

            AppendBackgroundRuns(builder, backgroundRuns, resolvedOptions);
        }

        if (builder.Length == 0)
        {
            return resolvedOptions.Kind switch
            {
                AgentStatusOverviewKind.WorkItems => "No work items matched the requested filters.",
                AgentStatusOverviewKind.BackgroundRuns => "No background runs matched the requested filters.",
                _ => "No agent work items or background runs matched the requested filters.",
            };
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
        if (!string.IsNullOrWhiteSpace(run.WorkItemId))
            builder.AppendLine($"Work item: {run.WorkItemId}");
        builder.AppendLine($"Status: {run.Status}");
        builder.AppendLine(
            $"{(run.Status == AgentBackgroundRunStatus.Queued ? "Queued" : "Started")}: {run.StartedAt:O}");
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

    public static bool TryParseOverviewKind(
        string? value,
        out AgentStatusOverviewKind kind)
    {
        kind = AgentStatusOverviewKind.All;
        if (string.IsNullOrWhiteSpace(value))
            return true;

        var normalized = Normalize(value);
        kind = normalized switch
        {
            "all" => AgentStatusOverviewKind.All,
            "workitems" or "workitem" or "items" => AgentStatusOverviewKind.WorkItems,
            "backgroundruns" or "backgroundrun" or "runs" or "run" => AgentStatusOverviewKind.BackgroundRuns,
            _ => default,
        };

        return normalized is
            "all" or
            "workitems" or "workitem" or "items" or
            "backgroundruns" or "backgroundrun" or "runs" or "run";
    }

    private static void AppendWorkItems(
        StringBuilder builder,
        IReadOnlyList<AgentWorkItem> items,
        AgentStatusOverviewOptions options)
    {
        if (items.Count == 0)
            return;

        var page = ApplyPagination(items, options.Offset, options.Limit);
        builder.AppendLine("Work items:");
        builder.AppendLine(FormatPaginationMessage("work item", items.Count, options.Offset, page.Count));
        foreach (var item in page)
            builder.AppendLine($"- {item.Id} [{item.Status}] {item.Title}");
    }

    private static void AppendBackgroundRuns(
        StringBuilder builder,
        IReadOnlyList<AgentBackgroundRun> runs,
        AgentStatusOverviewOptions options)
    {
        if (runs.Count == 0)
            return;

        var page = ApplyPagination(runs, options.Offset, options.Limit);
        builder.AppendLine("Background runs:");
        builder.AppendLine(FormatPaginationMessage("background run", runs.Count, options.Offset, page.Count));
        foreach (var run in page)
        {
            var workItemNote = string.IsNullOrWhiteSpace(run.WorkItemId)
                ? string.Empty
                : $" -> {run.WorkItemId}";
            builder.AppendLine($"- {run.Id} [{run.Status}] {run.Name}{workItemNote}");
        }
    }

    private static IReadOnlyList<T> ApplyPagination<T>(
        IReadOnlyList<T> source,
        int offset,
        int? limit)
    {
        var normalizedOffset = Math.Max(0, offset);
        var normalizedLimit = limit.HasValue
            ? Math.Max(1, limit.Value)
            : int.MaxValue;

        return source
            .Skip(normalizedOffset)
            .Take(normalizedLimit)
            .ToArray();
    }

    private static string FormatPaginationMessage(
        string itemLabel,
        int totalCount,
        int offset,
        int pageCount)
    {
        var normalizedOffset = Math.Max(0, offset);
        if (pageCount == 0)
            return $"No {Pluralize(itemLabel, totalCount)} at or after offset {normalizedOffset}. Total matching {Pluralize(itemLabel, totalCount)}: {totalCount}.";

        var start = normalizedOffset + 1;
        var end = normalizedOffset + pageCount;
        return $"Showing {Pluralize(itemLabel, totalCount)} {start}-{end} of {totalCount}.";
    }

    private static bool MatchesOwner(string? owner, string? filterOwner) =>
        string.IsNullOrWhiteSpace(filterOwner) ||
        string.Equals(owner, filterOwner.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool MatchesStatus<TStatus>(TStatus status, string? filterStatus)
        where TStatus : struct, Enum =>
        string.IsNullOrWhiteSpace(filterStatus) ||
        string.Equals(
            Normalize(status.ToString()),
            Normalize(filterStatus),
            StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return new string(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }

    private static string Pluralize(string label, int count) =>
        count == 1 ? label : $"{label}s";
}

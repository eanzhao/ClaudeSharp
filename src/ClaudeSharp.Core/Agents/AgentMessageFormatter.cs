using System.Text;

namespace ClaudeSharp.Core.Agents;

/// <summary>
/// Formats mailbox state for tools and CLI output.
/// </summary>
public static class AgentMessageFormatter
{
    public static string FormatInbox(
        string participant,
        IReadOnlyList<AgentMessage> messages)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Mailbox inbox: {participant}");
        builder.AppendLine($"  Messages: {messages.Count}");

        if (messages.Count == 0)
        {
            builder.AppendLine("  Items: (none)");
            return builder.ToString().TrimEnd();
        }

        builder.AppendLine("  Items:");
        foreach (var message in messages)
            builder.AppendLine($"  - {FormatSummaryLine(message)}");

        return builder.ToString().TrimEnd();
    }

    public static string FormatOutbox(
        string participant,
        IReadOnlyList<AgentMessage> messages)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Mailbox outbox: {participant}");
        builder.AppendLine($"  Messages: {messages.Count}");

        if (messages.Count == 0)
        {
            builder.AppendLine("  Items: (none)");
            return builder.ToString().TrimEnd();
        }

        builder.AppendLine("  Items:");
        foreach (var message in messages)
            builder.AppendLine($"  - {FormatSummaryLine(message)}");

        return builder.ToString().TrimEnd();
    }

    public static string FormatThread(
        string threadId,
        IReadOnlyList<AgentMessage> messages)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Mailbox thread: {threadId}");
        builder.AppendLine($"  Messages: {messages.Count}");

        if (messages.Count == 0)
        {
            builder.AppendLine("  Items: (none)");
            return builder.ToString().TrimEnd();
        }

        var subject = messages
            .Select(message => message.Subject)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        if (!string.IsNullOrWhiteSpace(subject))
            builder.AppendLine($"  Subject: {subject}");

        builder.AppendLine("  Timeline:");
        foreach (var message in messages)
        {
            builder.AppendLine(
                $"  - {message.CreatedAt:O} | {message.From} -> {message.To} | {message.Kind} | {message.Status}");
            builder.AppendLine($"    {message.Body.Replace(Environment.NewLine, $"{Environment.NewLine}    ", StringComparison.Ordinal)}");
        }

        return builder.ToString().TrimEnd();
    }

    public static string FormatOverview(
        IReadOnlyList<AgentMessage> messages,
        IReadOnlyDictionary<string, int> unreadCounts)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Mailbox:");
        builder.AppendLine($"  Messages: {messages.Count}");
        builder.AppendLine($"  Unread: {unreadCounts.Values.Sum()}");

        if (unreadCounts.Count > 0)
        {
            builder.AppendLine("  Unread by recipient:");
            foreach (var entry in unreadCounts.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
                builder.AppendLine($"  - {entry.Key}: {entry.Value}");
        }

        if (messages.Count == 0)
        {
            builder.AppendLine("  Recent: (none)");
            return builder.ToString().TrimEnd();
        }

        builder.AppendLine("  Recent:");
        foreach (var message in messages.Take(5))
            builder.AppendLine($"  - {FormatSummaryLine(message)}");

        return builder.ToString().TrimEnd();
    }

    public static string FormatList(IReadOnlyList<AgentMessage> messages)
    {
        if (messages.Count == 0)
            return "Mailbox: (none)";

        var builder = new StringBuilder();
        builder.AppendLine("Mailbox:");
        foreach (var message in messages)
            builder.AppendLine($"  - {FormatSummaryLine(message)}");

        return builder.ToString().TrimEnd();
    }

    public static string FormatSummary(AgentMessageSummary summary)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Mailbox summary:");
        builder.AppendLine($"  Total: {summary.TotalCount}");
        builder.AppendLine($"  Read: {summary.ReadCount}");
        builder.AppendLine($"  Unread: {summary.UnreadCount}");

        if (summary.UnreadCounts.Count > 0)
        {
            builder.AppendLine("  Unread by recipient:");
            foreach (var entry in summary.UnreadCounts.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
                builder.AppendLine($"  - {entry.Key}: {entry.Value}");
        }

        if (summary.RecentMessages.Count == 0)
        {
            builder.AppendLine("  Recent: (none)");
            return builder.ToString().TrimEnd();
        }

        builder.AppendLine("  Recent:");
        foreach (var message in summary.RecentMessages)
            builder.AppendLine($"  - {FormatSummaryLine(message)}");

        return builder.ToString().TrimEnd();
    }

    public static string FormatDetails(AgentMessage message)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Message: {message.Id}");
        builder.AppendLine($"  From: {message.From}");
        builder.AppendLine($"  To: {message.To}");
        builder.AppendLine($"  Kind: {message.Kind}");
        builder.AppendLine($"  Status: {message.Status}");
        if (!string.IsNullOrWhiteSpace(message.Subject))
            builder.AppendLine($"  Subject: {message.Subject}");
        if (!string.IsNullOrWhiteSpace(message.RelatedMessageId))
            builder.AppendLine($"  Related: {message.RelatedMessageId}");
        builder.AppendLine($"  Created: {message.CreatedAt:O}");
        builder.AppendLine("  Body:");
        builder.AppendLine($"  {message.Body.Replace(Environment.NewLine, $"{Environment.NewLine}  ", StringComparison.Ordinal)}");
        return builder.ToString().TrimEnd();
    }

    public static bool TryFormatDetails(
        IReadOnlyList<AgentMessage> messages,
        string messageId,
        out string details)
    {
        var message = messages.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, messageId, StringComparison.OrdinalIgnoreCase));
        if (message == null)
        {
            details = $"Message '{messageId}' was not found.";
            return false;
        }

        details = FormatDetails(message);
        return true;
    }

    public static string FormatSummaryLine(AgentMessage message)
    {
        var preview = message.Body.Length <= 48
            ? message.Body
            : $"{message.Body[..45]}...";
        return $"{message.Id} | {message.From} -> {message.To} | {message.Kind} | {message.Status} | {preview}";
    }
}

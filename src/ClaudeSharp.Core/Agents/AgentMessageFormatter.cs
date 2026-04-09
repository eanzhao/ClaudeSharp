using System.Text;

namespace ClaudeSharp.Core.Agents;

/// <summary>
/// Formats mailbox state for tools and CLI output.
/// </summary>
public static class AgentMessageFormatter
{
    public static string FormatPendingActions(
        string participant,
        IReadOnlyList<AgentMessageActionItem> items)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Mailbox pending actions: {participant}");
        builder.AppendLine($"  Pending: {items.Count}");

        if (items.Count == 0)
        {
            builder.AppendLine("  Items: (none)");
            return builder.ToString().TrimEnd();
        }

        builder.AppendLine("  Items:");
        foreach (var item in items)
        {
            var message = item.TriggerMessage;
            var decisions = string.Join(", ", item.Decisions);
            builder.AppendLine(
                $"  - {message.Id} | {item.ActionType} | from {message.From} | decisions: {decisions}");
            builder.AppendLine($"    {TruncatePreview(message.Body)}");
        }

        return builder.ToString().TrimEnd();
    }

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
            var protocolNote = FormatProtocolDetails(message.Protocol, prefix: "    ");
            if (!string.IsNullOrWhiteSpace(protocolNote))
                builder.AppendLine(protocolNote);
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
        if (!string.IsNullOrWhiteSpace(message.Protocol?.ActionName))
            builder.AppendLine($"  Action: {message.Protocol.ActionName}");
        if (message.Protocol?.RequiresResponse == true)
            builder.AppendLine("  Requires response: true");
        if (!string.IsNullOrWhiteSpace(message.Protocol?.ResumeReason))
            builder.AppendLine($"  Resume reason: {message.Protocol.ResumeReason}");
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
        var preview = TruncatePreview(message.Body);
        var protocolNote = FormatProtocolSummary(message.Protocol);
        return $"{message.Id} | {message.From} -> {message.To} | {message.Kind} | {message.Status}{protocolNote} | {preview}";
    }

    private static string TruncatePreview(string text) =>
        text.Length <= 48
            ? text
            : $"{text[..45]}...";

    private static string FormatProtocolSummary(AgentMessageProtocol? protocol)
    {
        if (protocol == null)
            return string.Empty;

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(protocol.ActionName))
            parts.Add($"action={protocol.ActionName}");
        if (protocol.RequiresResponse)
            parts.Add("reply=yes");
        if (!string.IsNullOrWhiteSpace(protocol.ResumeReason))
            parts.Add("resume");

        return parts.Count == 0
            ? string.Empty
            : $" | {string.Join(", ", parts)}";
    }

    private static string? FormatProtocolDetails(
        AgentMessageProtocol? protocol,
        string prefix)
    {
        if (protocol == null)
            return null;

        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(protocol.ActionName))
            lines.Add($"{prefix}Action: {protocol.ActionName}");
        if (protocol.RequiresResponse)
            lines.Add($"{prefix}Requires response");
        if (!string.IsNullOrWhiteSpace(protocol.ResumeReason))
            lines.Add($"{prefix}Resume reason: {protocol.ResumeReason}");

        return lines.Count == 0
            ? null
            : string.Join(Environment.NewLine, lines);
    }
}

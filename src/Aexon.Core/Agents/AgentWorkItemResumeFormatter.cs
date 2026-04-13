namespace Aexon.Core.Agents;

/// <summary>
/// Formats approved-work-item resume results for CLI and tool output.
/// </summary>
public static class AgentWorkItemResumeFormatter
{
    public static string Format(AgentWorkItemResumeResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(result.Message))
            lines.Add(result.Message);

        if (!string.IsNullOrWhiteSpace(result.ApprovalRequestId))
            lines.Add($"- Approval request: {result.ApprovalRequestId}");
        if (!string.IsNullOrWhiteSpace(result.ApprovalResponseId))
            lines.Add($"- Approval response: {result.ApprovalResponseId}");

        if (result.Activation != null)
            lines.Add(FormatActivation(result.Activation));

        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatActivation(AgentMessageActivationResult result)
    {
        return result.Status switch
        {
            AgentMessageActivationStatus.Reactivated =>
                $"- Reactivated {result.Owner} as {result.BackgroundRunId} ({result.WorkItemId})." +
                FormatActivationMessageSuffix(result),
            AgentMessageActivationStatus.AlreadyActive =>
                $"- {result.Owner} already has an active background run." +
                FormatActivationMessageSuffix(result),
            AgentMessageActivationStatus.Failed =>
                $"- Failed to reactivate {result.Owner}: {result.Message}",
            _ => $"- No activation handler is registered for {result.Owner}.",
        };
    }

    private static string FormatActivationMessageSuffix(AgentMessageActivationResult result) =>
        string.IsNullOrWhiteSpace(result.Message) ? string.Empty : $" {result.Message}";
}

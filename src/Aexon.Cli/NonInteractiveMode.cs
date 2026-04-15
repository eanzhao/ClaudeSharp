using System.Text.Json;
using Aexon.Core.Messages;

namespace Aexon.Cli;

internal enum NonInteractiveOutputFormat
{
    Text,
    Markdown,
    Json,
}

internal enum NonInteractiveApprovalMode
{
    Deny,
    Allow,
}

internal sealed record NonInteractiveRunOptions(
    string Prompt,
    NonInteractiveOutputFormat OutputFormat,
    NonInteractiveApprovalMode ApprovalMode);

internal sealed record NonInteractiveRunResult(
    bool Success,
    string Output,
    string? ErrorMessage,
    bool PermissionDenied,
    int TurnCount,
    TimeSpan Duration,
    TokenUsage TotalUsage)
{
    public int ExitCode => PermissionDenied ? 2 : Success ? 0 : 1;

    public string ToJson()
    {
        var payload = new NonInteractiveJsonPayload(
            Success,
            Output,
            ErrorMessage,
            PermissionDenied,
            TurnCount,
            (long)Duration.TotalMilliseconds,
            TotalUsage);

        return JsonSerializer.Serialize(
            payload,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true,
            });
    }

    private sealed record NonInteractiveJsonPayload(
        bool Success,
        string Output,
        string? Error,
        bool PermissionDenied,
        int TurnCount,
        long DurationMs,
        TokenUsage TotalUsage);
}

internal static class NonInteractivePromptBuilder
{
    public static string? Compose(string? explicitPrompt, string? stdinContent)
    {
        var prompt = explicitPrompt?.Trim();
        var stdin = stdinContent?.TrimEnd();

        if (string.IsNullOrWhiteSpace(prompt))
            return string.IsNullOrWhiteSpace(stdin) ? null : stdin;

        if (string.IsNullOrWhiteSpace(stdin))
            return prompt;

        return $$"""
            {{prompt}}

            <stdin>
            {{stdin}}
            </stdin>
            """;
    }
}

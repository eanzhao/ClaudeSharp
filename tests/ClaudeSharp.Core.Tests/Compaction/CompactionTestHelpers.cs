using System.Text.Json;
using ClaudeSharp.Core.Messages;

namespace ClaudeSharp.Core.Tests.Compaction;

/// <summary>
/// Provides shared helpers for compaction tests.
/// </summary>
internal static class CompactionTestHelpers
{
    public static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);

    public static SystemMessage SystemText(string text) =>
        new()
        {
            Content = text,
        };

    public static UserMessage UserText(string text, bool isMeta = false) =>
        new()
        {
            Content = [new TextBlock(text)],
            IsMeta = isMeta,
        };

    public static UserMessage UserToolResult(
        string toolUseId,
        string content,
        bool isError = false) =>
        UserMessage.FromToolResult(toolUseId, content, isError);

    public static AssistantMessage AssistantText(string text) =>
        new()
        {
            Content = [new TextBlock(text)],
        };

    public static AssistantMessage AssistantThinking(
        string text,
        string? signature = null) =>
        new()
        {
            Content = [new ThinkingBlock(text, signature)],
        };

    public static AssistantMessage AssistantToolUse(
        string toolUseId,
        string name,
        object input) =>
        new()
        {
            Content =
            [
                new ToolUseBlock
                {
                    ToolUseId = toolUseId,
                    Name = name,
                    Input = Json(input),
                },
            ],
        };
}

using System.Text.Json;
using ClaudeSharp.Core.Commands;
using ClaudeSharp.Core.Messages;
using ClaudeSharp.Core.Permissions;
using ClaudeSharp.Core.Tools;

namespace ClaudeSharp.Core.Tests.Foundations;

internal static class FoundationTestHelpers
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

    public static ITool Tool(
        string name,
        string[]? aliases = null) =>
        new FakeToolAdapter(name, aliases ?? []);

    public static ToolExecutionContext ToolContext(
        PermissionContext? permissionContext = null) =>
        new()
        {
            WorkingDirectory = "/tmp",
            PermissionContext = permissionContext ?? new PermissionContext(),
            Tools = [],
            Messages = [],
            CancellationToken = CancellationToken.None,
        };

    private sealed class FakeToolAdapter : ITool
    {
        public FakeToolAdapter(string name, string[] aliases)
        {
            Name = name;
            Aliases = aliases;
        }

        public string Name { get; }
        public string[] Aliases { get; }

        public Task<string> GetDescriptionAsync() => Task.FromResult(Name);

        public JsonElement GetInputSchema() =>
            JsonSerializer.SerializeToElement(new { type = "object" });

        public Task<string> GetPromptAsync(ToolPromptContext context) =>
            Task.FromResult(Name);

        public Task<ToolResult> ExecuteAsync(
            JsonElement input,
            ToolExecutionContext context,
            IProgress<ToolProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ToolResult.Success(Name));
    }
}

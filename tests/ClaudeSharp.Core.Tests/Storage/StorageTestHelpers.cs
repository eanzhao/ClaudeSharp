using System.Text.Json;
using ClaudeSharp.Core.Messages;
using ClaudeSharp.Core.Storage;

namespace ClaudeSharp.Core.Tests.Storage;

/// <summary>
/// Represents temp directory scope.
/// </summary>
internal sealed class TempDirectoryScope : IDisposable
{
    public TempDirectoryScope(string? name = null)
    {
        var suffix = string.IsNullOrWhiteSpace(name) ? "storage" : name.Trim();
        RootPath = Path.Combine(
            Path.GetTempPath(),
            "claudesharp-core-tests",
            suffix,
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(RootPath);
    }

    public string RootPath { get; }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(RootPath))
                Directory.Delete(RootPath, recursive: true);
        }
        catch
        {
            // Best effort cleanup for transient test directories.
        }
    }
}

/// <summary>
/// Represents storage test data.
/// </summary>
internal static class StorageTestData
{
    public static JsonElement Json(object value) =>
        JsonSerializer.SerializeToElement(value);

    public static TranscriptMetadataEntry Metadata(
        string eventType,
        object? payload = null,
        DateTimeOffset? recordedAt = null) =>
        new(eventType, payload is null ? null : Json(payload), recordedAt);

    public static UserMessage User(
        string id,
        params ContentBlock[] content) =>
        new()
        {
            Id = id,
            Content = content,
        };

    public static UserMessage UserText(
        string id,
        string text) =>
        User(id, new TextBlock(text));

    public static UserMessage ToolResult(
        string id,
        string toolUseId,
        string content,
        bool isError = false) =>
        new()
        {
            Id = id,
            Content = [new ToolResultBlock(toolUseId, content, isError)],
            ToolUseResult = content,
        };

    public static AssistantMessage Assistant(
        string id,
        params ContentBlock[] content) =>
        new()
        {
            Id = id,
            Content = content,
        };

    public static AssistantMessage ThinkingAssistant(
        string id,
        string thought,
        string? signature = null) =>
        Assistant(id, new ThinkingBlock(thought, signature));

    public static SystemMessage System(
        string id,
        string content) =>
        new()
        {
            Id = id,
            Content = content,
        };
}

using System.Text;
using System.Text.Json;
using ClaudeSharp.Core.Permissions;
using ClaudeSharp.Core.Tools;

namespace ClaudeSharp.Tools;

/// <summary>
/// FileEditTool — 对应 Claude Code 的 tools/FileEditTool/
///
/// Claude Code 的核心设计之一：通过精确的 "search and replace" 实现文件编辑
/// 不是整文件覆盖，而是找到精确文本并替换，保留文件其余部分不变
///
/// 这个模式的优点：
/// 1. 最小化变更范围
/// 2. 保留原文件格式和缩进
/// 3. 减少 token 消耗（只发送变化部分）
/// 4. 用户更容易 review 变更
/// </summary>
public class FileEditTool : ITool
{
    public string Name => "Edit";

    public Task<string> GetDescriptionAsync() =>
        Task.FromResult("Edit a file by searching for exact text and replacing it.");

    public JsonElement GetInputSchema()
    {
        var schema = """
        {
            "type": "object",
            "properties": {
                "file_path": {
                    "type": "string",
                    "description": "Absolute path to the file to edit"
                },
                "old_string": {
                    "type": "string",
                    "description": "The exact text to search for (must match exactly)"
                },
                "new_string": {
                    "type": "string",
                    "description": "The text to replace old_string with"
                }
            },
            "required": ["file_path", "old_string", "new_string"],
            "additionalProperties": false
        }
        """;
        return JsonDocument.Parse(schema).RootElement.Clone();
    }

    public Task<string> GetPromptAsync(ToolPromptContext context)
    {
        return Task.FromResult("""
            Edit a file by searching for an exact string and replacing it with new content.

            Rules:
            - old_string MUST match exactly (including whitespace and indentation)
            - old_string must be unique in the file (appear exactly once)
            - Both old_string and new_string should include enough context lines for unique matching
            - For creating new files, use the Write tool instead
            - For large rewrites, use the Write tool instead
            """);
    }

    public async Task<ToolResult> ExecuteAsync(
        JsonElement input,
        ToolExecutionContext context,
        IProgress<ToolProgress>? progress = null,
        CancellationToken ct = default)
    {
        var filePath = input.GetProperty("file_path").GetString();
        var oldString = input.GetProperty("old_string").GetString();
        var newString = input.GetProperty("new_string").GetString();

        if (string.IsNullOrWhiteSpace(filePath))
            return ToolResult.Error("file_path is required.");
        if (oldString == null)
            return ToolResult.Error("old_string is required.");
        if (newString == null)
            return ToolResult.Error("new_string is required.");

        filePath = Path.IsPathRooted(filePath)
            ? filePath
            : Path.GetFullPath(Path.Combine(context.WorkingDirectory, filePath));

        if (!File.Exists(filePath))
            return ToolResult.Error($"File not found: {filePath}");

        try
        {
            var content = await File.ReadAllTextAsync(filePath, ct);

            // 计算匹配次数
            var count = CountOccurrences(content, oldString);
            if (count == 0)
                return ToolResult.Error(
                    $"old_string not found in {filePath}. Make sure it matches exactly, " +
                    "including whitespace and indentation.");

            if (count > 1)
                return ToolResult.Error(
                    $"old_string found {count} times in {filePath}. " +
                    "Include more context to make the match unique.");

            // 执行替换
            var newContent = content.Replace(oldString, newString);
            await File.WriteAllTextAsync(filePath, newContent, ct);

            // 生成简短的 diff 摘要
            var oldLines = oldString.Split('\n').Length;
            var newLines = newString.Split('\n').Length;
            var diffSummary = oldLines == newLines
                ? $"Modified {oldLines} line(s)"
                : $"Replaced {oldLines} line(s) with {newLines} line(s)";

            return ToolResult.Success($"Edited {filePath}: {diffSummary}");
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Error editing file: {ex.Message}");
        }
    }

    public Task<PermissionResult> CheckPermissionsAsync(
        JsonElement input, ToolExecutionContext context)
    {
        var filePath = input.TryGetProperty("file_path", out var fp)
            ? fp.GetString() ?? ""
            : "";
        return Task.FromResult(PermissionResult.Ask($"Allow editing: {filePath}"));
    }

    public string? GetActivityDescription(JsonElement? input)
    {
        if (input?.TryGetProperty("file_path", out var fp) == true)
            return $"Editing {Path.GetFileName(fp.GetString() ?? "")}";
        return "Editing file";
    }

    private static int CountOccurrences(string content, string search)
    {
        int count = 0;
        int index = 0;
        while ((index = content.IndexOf(search, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += search.Length;
        }
        return count;
    }
}

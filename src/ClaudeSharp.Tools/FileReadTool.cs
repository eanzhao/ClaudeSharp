using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClaudeSharp.Core.Permissions;
using ClaudeSharp.Core.Tools;

namespace ClaudeSharp.Tools;

/// <summary>
/// FileReadTool — 对应 Claude Code 的 tools/FileReadTool/FileReadTool.ts
///
/// 功能:
/// 1. 读取文本文件 (带行号，类似 cat -n)
/// 2. 支持 offset/limit 分段读取
/// 3. 支持图片文件 (返回 base64)
/// 4. 只读工具，可并行执行
/// </summary>
public class FileReadTool : ITool
{
    private const int MaxLinesToRead = 2000;

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".svg", ".ico"
    };

    public string Name => "Read";

    public Task<string> GetDescriptionAsync() =>
        Task.FromResult("Read a file from the local filesystem.");

    public JsonElement GetInputSchema()
    {
        var schema = """
        {
            "type": "object",
            "properties": {
                "file_path": {
                    "type": "string",
                    "description": "Absolute path to the file to read"
                },
                "offset": {
                    "type": "integer",
                    "description": "Line offset to start reading from (0-based)"
                },
                "limit": {
                    "type": "integer",
                    "description": "Maximum number of lines to read"
                }
            },
            "required": ["file_path"],
            "additionalProperties": false
        }
        """;
        return JsonDocument.Parse(schema).RootElement.Clone();
    }

    public Task<string> GetPromptAsync(ToolPromptContext context)
    {
        return Task.FromResult($"""
            Reads a file from the local filesystem. You can access any file directly.

            Usage:
            - The file_path parameter must be an absolute path
            - By default, reads up to {MaxLinesToRead} lines
            - Results are returned with line numbers (cat -n format)
            - You can specify offset and limit for partial reads
            - This tool can read image files (PNG, JPG, etc.)
            - This tool can only read files, not directories
            """);
    }

    public async Task<ToolResult> ExecuteAsync(
        JsonElement input,
        ToolExecutionContext context,
        IProgress<ToolProgress>? progress = null,
        CancellationToken ct = default)
    {
        var filePath = input.GetProperty("file_path").GetString();
        if (string.IsNullOrWhiteSpace(filePath))
            return ToolResult.Error("file_path is required.");

        // 解析路径
        filePath = ResolvePath(filePath, context.WorkingDirectory);

        if (!File.Exists(filePath))
            return ToolResult.Error($"File not found: {filePath}");

        // 图片文件 → base64
        if (IsImageFile(filePath))
        {
            var bytes = await File.ReadAllBytesAsync(filePath, ct);
            var base64 = Convert.ToBase64String(bytes);
            var ext = Path.GetExtension(filePath).TrimStart('.');
            return ToolResult.Success($"[Image file: {filePath}, {bytes.Length} bytes, {ext}]\nBase64: {base64[..Math.Min(200, base64.Length)]}...");
        }

        // 文本文件读取
        var offset = input.TryGetProperty("offset", out var offsetProp) ? offsetProp.GetInt32() : 0;
        var limit = input.TryGetProperty("limit", out var limitProp) ? limitProp.GetInt32() : MaxLinesToRead;
        limit = Math.Min(limit, MaxLinesToRead);

        try
        {
            var lines = await File.ReadAllLinesAsync(filePath, ct);
            var totalLines = lines.Length;

            if (lines.Length == 0)
                return ToolResult.Success($"File {filePath} is empty (0 lines).");

            var selected = lines.Skip(offset).Take(limit).ToArray();
            var sb = new StringBuilder();

            for (int i = 0; i < selected.Length; i++)
            {
                var lineNum = offset + i + 1;
                sb.AppendLine($"{lineNum,6}: {selected[i]}");
            }

            // 添加信息头
            var header = $"File: {filePath} ({totalLines} lines total)";
            if (offset > 0 || selected.Length < totalLines)
            {
                header += $" — showing lines {offset + 1}-{offset + selected.Length}";
            }

            return ToolResult.Success($"{header}\n{sb}");
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Error reading file: {ex.Message}");
        }
    }

    // Read 是只读工具
    public bool IsReadOnly(JsonElement input) => true;
    public bool IsConcurrencySafe(JsonElement input) => true;
    public int MaxResultSizeChars => int.MaxValue; // Claude Code 中 Read 设为 Infinity

    public string? GetActivityDescription(JsonElement? input)
    {
        if (input?.TryGetProperty("file_path", out var fp) == true)
            return $"Reading {Path.GetFileName(fp.GetString() ?? "")}";
        return "Reading file";
    }

    private static string ResolvePath(string path, string workingDirectory)
    {
        if (Path.IsPathRooted(path)) return path;
        return Path.GetFullPath(Path.Combine(workingDirectory, path));
    }

    private static bool IsImageFile(string path) =>
        ImageExtensions.Contains(Path.GetExtension(path));
}

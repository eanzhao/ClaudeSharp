using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClaudeSharp.Core.Tools;
using ClaudeSharp.Tools.Search;

namespace ClaudeSharp.Tools;

/// <summary>
/// GrepTool — 对应 Claude Code 的 tools/GrepTool/
///
/// 在文件中按正则表达式搜索内容
/// Claude Code 使用 ripgrep (rg) 作为后端，C# 版使用内置正则引擎
/// </summary>
public class GrepTool : ITool
{
    private const int MaxResults = 50;

    public string Name => "Grep";

    public Task<string> GetDescriptionAsync() =>
        Task.FromResult("Search for a pattern in files using regex.");

    public JsonElement GetInputSchema()
    {
        var schema = """
        {
            "type": "object",
            "properties": {
                "pattern": {
                    "type": "string",
                    "description": "Regex pattern to search for"
                },
                "path": {
                    "type": "string",
                    "description": "Directory or file to search in (absolute path)"
                },
                "include": {
                    "type": "string",
                    "description": "Glob pattern for files to include (e.g. *.cs)"
                }
            },
            "required": ["pattern"],
            "additionalProperties": false
        }
        """;
        return JsonDocument.Parse(schema).RootElement.Clone();
    }

    public Task<string> GetPromptAsync(ToolPromptContext context)
    {
        return Task.FromResult("""
            Search for a regex pattern across files in a directory.

            Usage:
            - pattern: Regular expression to match
            - path: Directory or file to search (defaults to working directory)
            - include: Glob pattern to filter files (e.g. "*.cs", "*.ts")
            - Results are capped at 50 matches
            - Shows file name, line number, and matching line
            """);
    }

    public async Task<ToolResult> ExecuteAsync(
        JsonElement input,
        ToolExecutionContext context,
        IProgress<ToolProgress>? progress = null,
        CancellationToken ct = default)
    {
        var pattern = input.GetProperty("pattern").GetString();
        if (string.IsNullOrWhiteSpace(pattern))
            return ToolResult.Error("pattern is required.");

        var searchPath = input.TryGetProperty("path", out var pathProp)
            ? pathProp.GetString() ?? context.WorkingDirectory
            : context.WorkingDirectory;

        searchPath = SearchPathUtilities.ResolvePath(searchPath, context.WorkingDirectory);

        var includeGlob = input.TryGetProperty("include", out var incProp)
            ? incProp.GetString()
            : null;

        Regex regex;
        try
        {
            regex = new Regex(pattern, RegexOptions.Compiled | RegexOptions.Multiline,
                TimeSpan.FromSeconds(5));
        }
        catch (RegexParseException ex)
        {
            return ToolResult.Error($"Invalid regex pattern: {ex.Message}");
        }

        try
        {
            var results = new List<string>();
            IEnumerable<string> files;

            if (File.Exists(searchPath))
            {
                files = new[] { searchPath };
            }
            else if (Directory.Exists(searchPath))
            {
                var searchPattern = includeGlob ?? "*.*";
                files = Directory.EnumerateFiles(searchPath, searchPattern,
                    new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        IgnoreInaccessible = true,
                        MatchCasing = MatchCasing.CaseSensitive,
                    });
            }
            else
            {
                return ToolResult.Error($"Path not found: {searchPath}");
            }

            foreach (var file in files)
            {
                if (ct.IsCancellationRequested) break;
                if (results.Count >= MaxResults) break;

                // Skip binary files and common non-text dirs
                if (IsBinaryFile(file) || ShouldSkipPath(file)) continue;

                try
                {
                    var lines = await File.ReadAllLinesAsync(file, ct);
                    for (int i = 0; i < lines.Length && results.Count < MaxResults; i++)
                    {
                        if (regex.IsMatch(lines[i]))
                        {
                            var relPath = Path.GetRelativePath(
                                context.WorkingDirectory, file);
                            results.Add($"{relPath}:{i + 1}: {lines[i].TrimEnd()}");
                        }
                    }
                }
                catch
                {
                    // Skip files that can't be read
                }
            }

            if (results.Count == 0)
                return ToolResult.Success($"No matches found for pattern: {pattern}");

            var sb = new StringBuilder();
            sb.AppendLine($"Found {results.Count} match(es):");
            foreach (var r in results)
                sb.AppendLine(r);

            if (results.Count >= MaxResults)
                sb.AppendLine($"\n(Results capped at {MaxResults}. Narrow your search.)");

            return ToolResult.Success(sb.ToString());
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Search error: {ex.Message}");
        }
    }

    public bool IsReadOnly(JsonElement input) => true;
    public bool IsConcurrencySafe(JsonElement input) => true;

    public string GetUserFacingName(JsonElement? input = null) => "Search";

    public string? GetActivityDescription(JsonElement? input)
    {
        if (input?.TryGetProperty("pattern", out var p) == true)
            return $"Searching for \"{p.GetString()}\"";
        return "Searching";
    }

    private static bool IsBinaryFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".exe" or ".dll" or ".bin" or ".zip" or ".tar" or ".gz"
            or ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp"
            or ".pdf" or ".woff" or ".woff2" or ".ttf" or ".otf"
            or ".mp3" or ".mp4" or ".avi" or ".mov" or ".so" or ".dylib";
    }

    private static bool ShouldSkipPath(string path)
    {
        return SearchPathUtilities.ShouldSkipPath(path);
    }
}

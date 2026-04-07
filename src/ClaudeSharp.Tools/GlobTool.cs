using System.Text;
using System.Text.Json;
using ClaudeSharp.Core.Tools;
using ClaudeSharp.Tools.Search;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

namespace ClaudeSharp.Tools;

/// <summary>
/// Provides glob tool.
/// </summary>
public class GlobTool : ITool
{
    private const int DefaultLimit = 100;
    private const int MaxLimit = 200;

    public string Name => "Glob";

    public Task<string> GetDescriptionAsync() =>
        Task.FromResult("Find files by glob pattern.");

    public JsonElement GetInputSchema()
    {
        var schema = """
        {
            "type": "object",
            "properties": {
                "pattern": {
                    "type": "string",
                    "description": "Glob pattern to match files, for example **/*.cs"
                },
                "path": {
                    "type": "string",
                    "description": "Directory to search in. Defaults to the working directory."
                },
                "limit": {
                    "type": "integer",
                    "description": "Maximum number of files to return"
                },
                "offset": {
                    "type": "integer",
                    "description": "Number of matched files to skip before returning results"
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
            Search for files by name using a glob pattern.

            Usage:
            - Prefer this tool over bash find/rg when the goal is to discover matching files
            - pattern accepts common glob syntax like **/*.cs, src/**/*.md, tests/*Spec.cs
            - path optionally narrows the search root
            - Results are returned as relative file paths
            - Use Grep when you need to search inside file contents instead of file names
            """);
    }

    public Task<ValidationResult> ValidateInputAsync(JsonElement input, ToolExecutionContext context)
    {
        var pattern = input.TryGetProperty("pattern", out var patternProp)
            ? patternProp.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(pattern))
            return Task.FromResult(ValidationResult.Invalid("pattern is required."));

        var searchRoot = SearchPathUtilities.ResolvePath(
            input.TryGetProperty("path", out var pathProp) ? pathProp.GetString() : null,
            context.WorkingDirectory);

        if (!Directory.Exists(searchRoot))
        {
            return Task.FromResult(
                ValidationResult.Invalid($"Search directory not found: {searchRoot}"));
        }

        return Task.FromResult(ValidationResult.Valid());
    }

    public Task<ToolResult> ExecuteAsync(
        JsonElement input,
        ToolExecutionContext context,
        IProgress<ToolProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var pattern = input.GetProperty("pattern").GetString()!;
        var searchRoot = SearchPathUtilities.ResolvePath(
            input.TryGetProperty("path", out var pathProp) ? pathProp.GetString() : null,
            context.WorkingDirectory);

        var limit = input.TryGetProperty("limit", out var limitProp)
            ? Math.Clamp(limitProp.GetInt32(), 1, MaxLimit)
            : DefaultLimit;
        var offset = input.TryGetProperty("offset", out var offsetProp)
            ? Math.Max(0, offsetProp.GetInt32())
            : 0;

        try
        {
            var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
            matcher.AddInclude(NormalizePattern(pattern));

            var directory = new DirectoryInfo(searchRoot);
            var matched = matcher.Execute(new DirectoryInfoWrapper(directory))
                .Files
                .Select(file => Path.GetFullPath(Path.Combine(searchRoot, file.Path)))
                .Where(path => !SearchPathUtilities.ShouldSkipPath(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(SearchPathUtilities.GetLastWriteTimeUtcSafe)
                .ToList();

            if (matched.Count == 0)
                return Task.FromResult(ToolResult.Success(
                    $"No files matched pattern \"{pattern}\" under {searchRoot}."));

            var page = matched.Skip(offset).Take(limit).ToList();
            var start = offset + 1;
            var end = offset + page.Count;
            var truncated = offset + page.Count < matched.Count;

            var sb = new StringBuilder();
            sb.AppendLine(
                $"Found {matched.Count} file(s) matching \"{pattern}\" under {searchRoot}.");
            sb.AppendLine($"Showing {start}-{end}:");

            foreach (var path in page)
                sb.AppendLine(SearchPathUtilities.ToDisplayPath(context.WorkingDirectory, path));

            if (truncated)
                sb.AppendLine($"\nResults truncated. Use offset to continue from item {end + 1}.");

            return Task.FromResult(ToolResult.Success(sb.ToString().TrimEnd()));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Glob search failed: {ex.Message}"));
        }
    }

    public bool IsReadOnly(JsonElement input) => true;

    public bool IsConcurrencySafe(JsonElement input) => true;

    public string GetUserFacingName(JsonElement? input = null) => "Search";

    public string? GetActivityDescription(JsonElement? input)
    {
        if (input?.TryGetProperty("pattern", out var pattern) == true)
            return $"Searching files for \"{pattern.GetString()}\"";
        return "Searching files";
    }

    private static string NormalizePattern(string pattern) =>
        pattern.Replace('\\', '/');
}

using ClaudeSharp.Core.Markdown;

namespace ClaudeSharp.Core.Context;

/// <summary>
/// Provides memory instruction scanner.
/// </summary>
public static class MemoryInstructionScanner
{
    public static async Task<IReadOnlyList<MemoryInstructionFile>> ScanAsync(
        string workingDirectory,
        CancellationToken ct = default)
    {
        var results = new List<MemoryInstructionFile>();

        foreach (var directory in EnumerateDirectoriesFromRoot(workingDirectory))
        {
            await AddIfExistsAsync(results, Path.Combine(directory, "CLAUDE.md"), ct);
            await AddIfExistsAsync(results, Path.Combine(directory, ".claude", "CLAUDE.md"), ct);
            await AddRuleFilesAsync(results, directory, ct);
            await AddIfExistsAsync(results, Path.Combine(directory, "CLAUDE.local.md"), ct);
        }

        return results;
    }

    private static IEnumerable<string> EnumerateDirectoriesFromRoot(string workingDirectory)
    {
        var chain = new List<string>();
        var current = Path.GetFullPath(workingDirectory);

        while (!string.IsNullOrEmpty(current))
        {
            chain.Add(current);

            var parent = Directory.GetParent(current)?.FullName;
            if (string.IsNullOrEmpty(parent) || parent == current)
                break;

            current = parent;
        }

        chain.Reverse();
        return chain;
    }

    private static async Task AddIfExistsAsync(
        ICollection<MemoryInstructionFile> files,
        string path,
        CancellationToken ct)
    {
        if (!File.Exists(path))
            return;

        var raw = await File.ReadAllTextAsync(path, ct);
        if (string.IsNullOrWhiteSpace(raw))
            return;

        var parsed = FrontmatterParser.Parse(raw);
        var content = parsed.Content.Trim();
        if (string.IsNullOrWhiteSpace(content))
            return;

        files.Add(new MemoryInstructionFile(path, content, parsed.Frontmatter));
    }

    private static async Task AddRuleFilesAsync(
        ICollection<MemoryInstructionFile> files,
        string directory,
        CancellationToken ct)
    {
        var rulesDir = Path.Combine(directory, ".claude", "rules");
        if (!Directory.Exists(rulesDir))
            return;

        foreach (var path in Directory.EnumerateFiles(rulesDir, "*.md", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            await AddIfExistsAsync(files, path, ct);
        }
    }
}

/// <summary>
/// Represents memory instruction file.
/// </summary>
public sealed record MemoryInstructionFile(
    string Path,
    string Content,
    IReadOnlyDictionary<string, object?> Frontmatter);

using Aexon.Core.Markdown;
using Aexon.Core.Permissions;

namespace Aexon.Core.Context;

/// <summary>
/// Provides memory instruction scanner.
/// </summary>
public static class MemoryInstructionScanner
{
    public static async Task<IReadOnlyList<MemoryInstructionFile>> ScanAsync(
        string workingDirectory,
        CancellationToken ct = default)
    {
        var result = await ScanAsync(
            new MemoryInstructionScanOptions
            {
                WorkingDirectory = workingDirectory,
            },
            ct);
        return result.Files;
    }

    public static async Task<MemoryInstructionScanResult> ScanAsync(
        MemoryInstructionScanOptions options,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var results = new List<MemoryInstructionFile>();
        var diagnostics = new List<MemoryInstructionDiagnostic>();

        await AddSystemScopeAsync(results, diagnostics, options, ct);
        await AddUserScopeAsync(results, diagnostics, options, ct);
        await AddProjectScopeAsync(results, diagnostics, options.WorkingDirectory, ct);

        return new MemoryInstructionScanResult(results, diagnostics);
    }

    private static async Task AddUserScopeAsync(
        ICollection<MemoryInstructionFile> files,
        ICollection<MemoryInstructionDiagnostic> diagnostics,
        MemoryInstructionScanOptions options,
        CancellationToken ct)
    {
        var root = options.UserClaudeDirectory;
        if (string.IsNullOrWhiteSpace(root))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(home))
                root = Path.Combine(home, ".claude");
        }

        if (string.IsNullOrWhiteSpace(root))
            return;

        var fullRoot = Path.GetFullPath(root);
        await AddScopeRootAsync(
            files,
            diagnostics,
            fullRoot,
            MemoryInstructionScope.User,
            includeNestedClaudeDirectory: false,
            ct);
    }

    private static async Task AddSystemScopeAsync(
        ICollection<MemoryInstructionFile> files,
        ICollection<MemoryInstructionDiagnostic> diagnostics,
        MemoryInstructionScanOptions options,
        CancellationToken ct)
    {
        var root = string.IsNullOrWhiteSpace(options.SystemClaudeDirectory)
            ? AppContext.BaseDirectory
            : options.SystemClaudeDirectory;
        if (string.IsNullOrWhiteSpace(root))
            return;

        await AddScopeRootAsync(
            files,
            diagnostics,
            Path.GetFullPath(root),
            MemoryInstructionScope.System,
            includeNestedClaudeDirectory: true,
            ct);
    }

    private static async Task AddScopeRootAsync(
        ICollection<MemoryInstructionFile> files,
        ICollection<MemoryInstructionDiagnostic> diagnostics,
        string root,
        MemoryInstructionScope scope,
        bool includeNestedClaudeDirectory,
        CancellationToken ct)
    {
        if (!Directory.Exists(root))
            return;

        await AddIfExistsAsync(files, diagnostics, Path.Combine(root, "CLAUDE.md"), scope, ct);
        await AddRuleFilesAsync(files, diagnostics, Path.Combine(root, "rules"), scope, ct);

        if (!includeNestedClaudeDirectory)
            return;

        await AddIfExistsAsync(
            files,
            diagnostics,
            Path.Combine(root, ".claude", "CLAUDE.md"),
            scope,
            ct);
        await AddRuleFilesAsync(files, diagnostics, Path.Combine(root, ".claude", "rules"), scope, ct);
    }

    private static async Task AddProjectScopeAsync(
        ICollection<MemoryInstructionFile> files,
        ICollection<MemoryInstructionDiagnostic> diagnostics,
        string workingDirectory,
        CancellationToken ct)
    {
        var ignoreRules = new List<ClaudeIgnoreRule>();

        foreach (var directory in EnumerateDirectoriesFromProjectRoot(workingDirectory))
        {
            if (IsIgnored(directory, ignoreRules))
                continue;

            await AddIgnoreRulesAsync(ignoreRules, diagnostics, directory, ct);
            await AddIfExistsAsync(
                files,
                diagnostics,
                Path.Combine(directory, "CLAUDE.md"),
                MemoryInstructionScope.Project,
                ct,
                path => IsIgnored(path, ignoreRules));
            await AddIfExistsAsync(
                files,
                diagnostics,
                Path.Combine(directory, ".claude", "CLAUDE.md"),
                MemoryInstructionScope.Project,
                ct,
                path => IsIgnored(path, ignoreRules));
            await AddRuleFilesAsync(
                files,
                diagnostics,
                Path.Combine(directory, ".claude", "rules"),
                MemoryInstructionScope.Project,
                ct,
                path => IsIgnored(path, ignoreRules));
            await AddIfExistsAsync(
                files,
                diagnostics,
                Path.Combine(directory, "CLAUDE.local.md"),
                MemoryInstructionScope.Project,
                ct,
                path => IsIgnored(path, ignoreRules));
        }
    }

    private static IEnumerable<string> EnumerateDirectoriesFromProjectRoot(string workingDirectory)
    {
        var current = Path.GetFullPath(workingDirectory);
        var projectRoot = FindGitRoot(current);
        var chain = new List<string>();

        while (!string.IsNullOrEmpty(current))
        {
            chain.Add(current);

            if (PathsEqual(current, projectRoot))
                break;

            var parent = Directory.GetParent(current)?.FullName;
            if (string.IsNullOrEmpty(parent) || PathsEqual(parent, current))
                break;

            current = parent;
        }

        chain.Reverse();
        return chain;
    }

    private static string? FindGitRoot(string workingDirectory)
    {
        var current = Path.GetFullPath(workingDirectory);
        while (!string.IsNullOrEmpty(current))
        {
            var dotGitDirectory = Path.Combine(current, ".git");
            if (Directory.Exists(dotGitDirectory) || File.Exists(dotGitDirectory))
                return current;

            var parent = Directory.GetParent(current)?.FullName;
            if (string.IsNullOrEmpty(parent) || PathsEqual(parent, current))
                break;

            current = parent;
        }

        return null;
    }

    private static async Task AddIgnoreRulesAsync(
        ICollection<ClaudeIgnoreRule> rules,
        ICollection<MemoryInstructionDiagnostic> diagnostics,
        string directory,
        CancellationToken ct)
    {
        var path = Path.Combine(directory, ".claudeignore");
        if (!File.Exists(path))
            return;

        var lines = await File.ReadAllLinesAsync(path, ct);
        for (var index = 0; index < lines.Length; index++)
        {
            var trimmed = lines[index].Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#'))
                continue;

            if (trimmed.StartsWith("!", StringComparison.Ordinal))
            {
                diagnostics.Add(new MemoryInstructionDiagnostic(
                    path,
                    $"Line {index + 1}: negated patterns are not supported and were ignored."));
                continue;
            }

            var directoryOnly = trimmed.EndsWith("/", StringComparison.Ordinal);
            var normalizedPattern = NormalizeRelativePattern(
                directoryOnly ? trimmed[..^1] : trimmed);
            if (string.IsNullOrWhiteSpace(normalizedPattern))
                continue;

            rules.Add(new ClaudeIgnoreRule(directory, normalizedPattern, directoryOnly));
        }
    }

    private static bool IsIgnored(
        string path,
        IReadOnlyList<ClaudeIgnoreRule> rules)
    {
        foreach (var rule in rules)
        {
            if (rule.Matches(path))
                return true;
        }

        return false;
    }

    private static async Task AddIfExistsAsync(
        ICollection<MemoryInstructionFile> files,
        ICollection<MemoryInstructionDiagnostic> diagnostics,
        string path,
        MemoryInstructionScope scope,
        CancellationToken ct,
        Func<string, bool>? isIgnored = null)
    {
        if (isIgnored?.Invoke(path) == true || !File.Exists(path))
            return;

        var raw = await File.ReadAllTextAsync(path, ct);
        if (string.IsNullOrWhiteSpace(raw))
            return;

        var parsed = FrontmatterParser.Parse(raw);
        if (parsed.HadInvalidFrontmatter)
        {
            diagnostics.Add(new MemoryInstructionDiagnostic(
                path,
                "Invalid YAML frontmatter detected. Ignoring frontmatter and loading the markdown body only."));
        }

        var content = parsed.Content.Trim();
        if (string.IsNullOrWhiteSpace(content))
            return;

        files.Add(new MemoryInstructionFile(path, content, parsed.Frontmatter, scope));
    }

    private static async Task AddRuleFilesAsync(
        ICollection<MemoryInstructionFile> files,
        ICollection<MemoryInstructionDiagnostic> diagnostics,
        string rulesDirectory,
        MemoryInstructionScope scope,
        CancellationToken ct,
        Func<string, bool>? isIgnored = null)
    {
        if (isIgnored?.Invoke(rulesDirectory) == true || !Directory.Exists(rulesDirectory))
            return;

        foreach (var path in Directory.EnumerateFiles(rulesDirectory, "*.md", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            await AddIfExistsAsync(files, diagnostics, path, scope, ct, isIgnored);
        }
    }

    private static bool PathsEqual(string? left, string? right) =>
        string.Equals(
            left?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            right?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);

    private static string NormalizeRelativePattern(string pattern) =>
        pattern.Trim()
            .TrimStart('/')
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');

    private sealed record ClaudeIgnoreRule(
        string BaseDirectory,
        string Pattern,
        bool DirectoryOnly)
    {
        public bool Matches(string candidatePath)
        {
            var relative = Path.GetRelativePath(BaseDirectory, candidatePath)
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/')
                .Trim();
            if (string.IsNullOrWhiteSpace(relative) || relative.StartsWith("..", StringComparison.Ordinal))
                return false;

            if (DirectoryOnly)
            {
                return string.Equals(relative, Pattern, Comparison) ||
                       relative.StartsWith(Pattern + "/", Comparison);
            }

            if (!PermissionRuleMatcher.HasWildcards(Pattern))
            {
                return string.Equals(relative, Pattern, Comparison) ||
                       relative.StartsWith(Pattern + "/", Comparison);
            }

            return PermissionRuleMatcher.MatchWildcardPattern(Pattern, relative, caseInsensitive: OperatingSystem.IsWindows());
        }

        private static StringComparison Comparison =>
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
    }
}

/// <summary>
/// Options for scanning CLAUDE.md-style memory files.
/// </summary>
public sealed class MemoryInstructionScanOptions
{
    public string WorkingDirectory { get; init; } = Environment.CurrentDirectory;
    public string? UserClaudeDirectory { get; init; }
    public string? SystemClaudeDirectory { get; init; }
}

/// <summary>
/// Represents the outcome of a CLAUDE.md scan.
/// </summary>
public sealed record MemoryInstructionScanResult(
    IReadOnlyList<MemoryInstructionFile> Files,
    IReadOnlyList<MemoryInstructionDiagnostic> Diagnostics);

/// <summary>
/// Represents a CLAUDE.md validation or loading warning.
/// </summary>
public sealed record MemoryInstructionDiagnostic(
    string Path,
    string Message);

/// <summary>
/// Identifies the source scope for a memory instruction file.
/// </summary>
public enum MemoryInstructionScope
{
    System,
    User,
    Project,
}

/// <summary>
/// Represents memory instruction file.
/// </summary>
public sealed record MemoryInstructionFile(
    string Path,
    string Content,
    IReadOnlyDictionary<string, object?> Frontmatter,
    MemoryInstructionScope Scope);

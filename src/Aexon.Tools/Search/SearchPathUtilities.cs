namespace Aexon.Tools.Search;

/// <summary>
/// Represents search path utilities.
/// </summary>
internal static class SearchPathUtilities
{
    private static readonly HashSet<string> IgnoredDirectoryNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".git",
            "node_modules",
            "bin",
            "obj",
        };

    public static string ResolvePath(string? path, string workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(path))
            return workingDirectory;

        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(workingDirectory, path));
    }

    public static bool ShouldSkipPath(string path)
    {
        var normalized = path.Replace('\\', '/');
        return IgnoredDirectoryNames.Any(name =>
            normalized.Contains($"/{name}/", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith($"/{name}", StringComparison.OrdinalIgnoreCase));
    }

    public static bool ShouldSkipDirectory(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        return IgnoredDirectoryNames.Contains(name);
    }

    public static string ToDisplayPath(string workingDirectory, string path)
    {
        try
        {
            if (Path.IsPathFullyQualified(path))
                return Path.GetRelativePath(workingDirectory, path);
        }
        catch
        {
            // Fallback to original path below.
        }

        return path;
    }

    public static DateTime GetLastWriteTimeUtcSafe(string path)
    {
        try
        {
            return File.GetLastWriteTimeUtc(path);
        }
        catch
        {
            return DateTime.MinValue;
        }
    }
}

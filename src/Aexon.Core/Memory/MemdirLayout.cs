using System.Security.Cryptography;
using System.Text;

namespace Aexon.Core.Memory;

/// <summary>
/// Represents memdir layout.
/// </summary>
public sealed record MemdirLayout
{
    public required string MemoryBaseDirectory { get; init; }
    public required string ProjectRootDirectory { get; init; }

    public string ProjectId => BuildProjectId(ProjectRootDirectory);

    public string ProjectMemoryDirectory =>
        Path.Combine(MemoryBaseDirectory, "projects", ProjectId, "memory");

    public string MemoryIndexPath =>
        Path.Combine(ProjectMemoryDirectory, "MEMORY.md");

    public string SessionMemoryDirectory =>
        Path.Combine(ProjectMemoryDirectory, "sessions");

    public string TeamMemoryDirectory =>
        Path.Combine(ProjectMemoryDirectory, "team");

    public string AutoDreamDirectory =>
        Path.Combine(ProjectMemoryDirectory, "autodream");

    public string AutoDreamLockPath =>
        Path.Combine(AutoDreamDirectory, "consolidation.lock");

    public string GetSessionMemoryPath(string sessionId) =>
        Path.Combine(SessionMemoryDirectory, SanitizeSegment(sessionId), "SESSION_MEMORY.md");

    public string GetTeamMemoryPath(string teamName) =>
        Path.Combine(TeamMemoryDirectory, SanitizeSegment(teamName), "TEAM_MEMORY.md");

    public TeamMemoryFile CreateTeamMemoryFile(string teamName) =>
        new(GetTeamMemoryPath(teamName), teamName, ProjectRootDirectory);

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(ProjectMemoryDirectory);
        Directory.CreateDirectory(SessionMemoryDirectory);
        Directory.CreateDirectory(TeamMemoryDirectory);
        Directory.CreateDirectory(AutoDreamDirectory);
    }

    public SessionMemoryFile CreateSessionMemoryFile(string sessionId) =>
        new(GetSessionMemoryPath(sessionId), sessionId, ProjectRootDirectory);

    private static string BuildProjectId(string projectRootDirectory)
    {
        var normalizedRoot = NormalizePath(projectRootDirectory);
        var rootLeaf = GetLeafSegment(normalizedRoot);
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(normalizedRoot)))
            .ToLowerInvariant()[..12];

        return $"{SanitizeSegment(rootLeaf)}-{hash}";
    }

    private static string NormalizePath(string path)
    {
        var fullPath = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return fullPath.Replace('\\', '/').ToLowerInvariant();
    }

    private static string GetLeafSegment(string normalizedPath)
    {
        var leaf = normalizedPath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault();

        return string.IsNullOrWhiteSpace(leaf) ? "project" : leaf;
    }

    private static string SanitizeSegment(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.Trim())
        {
            if (char.IsLetterOrDigit(ch) || ch is '-' or '_')
                builder.Append(char.ToLowerInvariant(ch));
            else if (builder.Length == 0 || builder[^1] != '-')
                builder.Append('-');
        }

        var sanitized = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(sanitized) ? "memory" : sanitized;
    }
}

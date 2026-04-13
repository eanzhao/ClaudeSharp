using System.Text;

namespace Aexon.Core.Memory;

/// <summary>
/// Represents the result of a consolidation run.
/// </summary>
public sealed record MemoryConsolidationResult(
    bool AcquiredLock,
    bool Consolidated,
    string? SessionId,
    string? TeamName,
    string? TeamMemoryPath,
    string? ProjectMemoryPath,
    string? SummaryExcerpt,
    string? Message);

/// <summary>
/// Consolidates session memory into team and project memory files.
/// </summary>
public sealed class MemoryConsolidationService
{
    private readonly MemdirLayout _layout;
    private readonly string _teamName;

    public MemoryConsolidationService(
        MemdirLayout layout,
        string teamName = "default")
    {
        _layout = layout;
        _teamName = string.IsNullOrWhiteSpace(teamName) ? "default" : teamName.Trim();
    }

    public async Task<MemoryConsolidationResult> ConsolidateSessionAsync(
        SessionMemoryFile sessionMemoryFile,
        CancellationToken cancellationToken = default)
    {
        var teamMemoryFile = _layout.CreateTeamMemoryFile(_teamName);
        var info = new AutoDreamConsolidationJobInfo(
            _layout.ProjectId,
            _teamName,
            sessionMemoryFile.SessionId,
            "session-memory consolidation",
            DateTimeOffset.UtcNow,
            Environment.MachineName);

        await using var job = await AutoDreamConsolidationJob.TryAcquireAsync(
            _layout.AutoDreamLockPath,
            info,
            cancellationToken);

        if (job == null)
        {
            return new MemoryConsolidationResult(
                AcquiredLock: false,
                Consolidated: false,
                sessionMemoryFile.SessionId,
                _teamName,
                teamMemoryFile.Path,
                _layout.MemoryIndexPath,
                SummaryExcerpt: null,
                Message: "Another autodream consolidation job is already running.");
        }

        var sessionContent = await sessionMemoryFile.LoadAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(sessionContent))
        {
            return new MemoryConsolidationResult(
                AcquiredLock: true,
                Consolidated: false,
                sessionMemoryFile.SessionId,
                _teamName,
                teamMemoryFile.Path,
                _layout.MemoryIndexPath,
                SummaryExcerpt: null,
                Message: "Session memory is empty.");
        }

        var normalizedContent = NormalizeContent(sessionContent);
        var summaryExcerpt = BuildSummaryExcerpt(normalizedContent);
        var timestamp = DateTimeOffset.UtcNow;

        var teamBlock = BuildTeamBlock(sessionMemoryFile.SessionId, timestamp, normalizedContent);
        await teamMemoryFile.AppendAsync(teamBlock, cancellationToken);

        var projectBlock = BuildProjectBlock(
            sessionMemoryFile.SessionId,
            _teamName,
            timestamp,
            summaryExcerpt);
        await AppendProjectMemoryAsync(projectBlock, cancellationToken);

        return new MemoryConsolidationResult(
            AcquiredLock: true,
            Consolidated: true,
            sessionMemoryFile.SessionId,
            _teamName,
            teamMemoryFile.Path,
            _layout.MemoryIndexPath,
            summaryExcerpt,
            Message: "Session memory consolidated into team and project memory.");
    }

    public async Task<MemoryConsolidationResult> ConsolidateSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var sessionMemoryFile = _layout.CreateSessionMemoryFile(sessionId);
        return await ConsolidateSessionAsync(sessionMemoryFile, cancellationToken);
    }

    private async Task AppendProjectMemoryAsync(
        string block,
        CancellationToken cancellationToken)
    {
        var directory = System.IO.Path.GetDirectoryName(_layout.MemoryIndexPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        await File.AppendAllTextAsync(_layout.MemoryIndexPath, block, cancellationToken);
    }

    private static string NormalizeContent(string content)
    {
        var builder = new StringBuilder(content.Length);
        using var reader = new StringReader(content);
        string? line;
        var first = true;
        while ((line = reader.ReadLine()) != null)
        {
            var trimmed = line.TrimEnd();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                if (!first && builder.Length > 0 && builder[^1] != '\n')
                    builder.AppendLine();
                first = false;
                continue;
            }

            if (builder.Length > 0)
                builder.AppendLine();
            builder.Append(trimmed);
            first = false;
        }

        return builder.ToString().Trim();
    }

    private static string BuildSummaryExcerpt(string content)
    {
        var collapsed = string.Join(
            " ",
            content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        if (collapsed.Length <= 240)
            return collapsed;

        return collapsed[..237] + "...";
    }

    private static string BuildTeamBlock(
        string sessionId,
        DateTimeOffset timestamp,
        string content)
    {
        return $"""

            ## Session {sessionId}
            - Consolidated at: {timestamp:yyyy-MM-dd HH:mm:ss zzz}

            {content}
            """;
    }

    private static string BuildProjectBlock(
        string sessionId,
        string teamName,
        DateTimeOffset timestamp,
        string summaryExcerpt)
    {
        return $"""

            ## Consolidation {timestamp:yyyy-MM-dd HH:mm:ss zzz}
            - Session: {sessionId}
            - Team: {teamName}
            - Summary: {summaryExcerpt}
            """;
    }
}

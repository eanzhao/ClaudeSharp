using ClaudeSharp.Core.Messages;

namespace ClaudeSharp.Core.Storage;

/// <summary>
/// Represents options for resume.
/// </summary>
public sealed class ResumeOptions
{
    public string? WorkingDirectoryOverride { get; init; }
    public string? ModelOverride { get; init; }
    public bool ForkSession { get; init; }
}

/// <summary>
/// Represents processed resume.
/// </summary>
public sealed class ProcessedResume
{
    public required TranscriptSession SourceSession { get; init; }
    public required IReadOnlyList<ConversationMessage> Messages { get; init; }
    public required TokenUsage TotalUsage { get; init; }
    public required ConversationSessionMetadata Metadata { get; init; }
    public required string WorkingDirectory { get; init; }
    public required string Model { get; init; }
    public required bool ContinueExistingSession { get; init; }
}

/// <summary>
/// Defines the contract for session restore pipeline.
/// </summary>
public interface ISessionRestorePipeline
{
    Task<ProcessedResume> RestoreAsync(
        ResumeLoadResult result,
        ResumeOptions options,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Provides session restore pipeline.
/// </summary>
public sealed class SessionRestorePipeline : ISessionRestorePipeline
{
    public Task<ProcessedResume> RestoreAsync(
        ResumeLoadResult result,
        ResumeOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new ProcessedResume
        {
            SourceSession = result.Session,
            Messages = result.Messages,
            TotalUsage = result.TotalUsage,
            Metadata = result.Metadata.Clone(),
            WorkingDirectory = options.WorkingDirectoryOverride ?? result.Session.WorkingDirectory,
            Model = options.ModelOverride ?? result.Session.Model,
            ContinueExistingSession = !options.ForkSession,
        });
    }
}

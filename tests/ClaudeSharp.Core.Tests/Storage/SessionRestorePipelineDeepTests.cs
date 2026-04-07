using ClaudeSharp.Core.Messages;
using ClaudeSharp.Core.Storage;

namespace ClaudeSharp.Core.Tests.Storage;

/// <summary>
/// Contains tests for session Restore Pipeline Deep.
/// </summary>
public sealed class SessionRestorePipelineDeepTests
{
    [Fact]
    public async Task RestoreAsyncThrowsWhenCancellationIsRequested()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = new ResumeLoadResult
        {
            Session = new TranscriptSession
            {
                SessionId = "session-1",
                SessionDirectory = "/work/session-1",
                TranscriptPath = "/work/session-1/transcript.jsonl",
                ManifestPath = "/work/session-1/manifest.json",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                WorkingDirectory = "/work/original",
                Model = "sonnet",
                Metadata = new ConversationSessionMetadata(),
            },
            Messages = [StorageTestData.UserText("msg-1", "hello")],
            TotalUsage = TokenUsage.Empty,
            Metadata = new ConversationSessionMetadata(),
        };

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            new SessionRestorePipeline().RestoreAsync(result, new ResumeOptions(), cts.Token));
    }
}

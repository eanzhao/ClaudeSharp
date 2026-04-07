using ClaudeSharp.Core.Messages;
using ClaudeSharp.Core.Permissions;
using ClaudeSharp.Core.Storage;

namespace ClaudeSharp.Core.Tests.Storage;

public sealed class SessionRestorePipelineTests
{
    [Fact]
    public async Task RestoreAsyncAppliesOverridesAndForkSemantics()
    {
        var metadata = new ConversationSessionMetadata
        {
            Title = "Original title",
            Mode = PermissionMode.Plan,
        };
        metadata.Tags.Add("alpha");

        var session = new TranscriptSession
        {
            SessionId = "session-1",
            SessionDirectory = "/work/session-1",
            TranscriptPath = "/work/session-1/transcript.jsonl",
            ManifestPath = "/work/session-1/manifest.json",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            WorkingDirectory = "/work/original",
            Model = "sonnet",
            Metadata = metadata,
            CurrentLeafMessageId = "msg-2",
        };

        var loadResult = new ResumeLoadResult
        {
            Session = session,
            Messages =
            [
                StorageTestData.UserText("msg-1", "hello"),
                StorageTestData.Assistant("msg-2", new TextBlock("world")),
            ],
            TotalUsage = new TokenUsage
            {
                InputTokens = 12,
                OutputTokens = 5,
            },
            Metadata = metadata.Clone(),
        };

        var restored = await new SessionRestorePipeline().RestoreAsync(
            loadResult,
            new ResumeOptions
            {
                WorkingDirectoryOverride = "/work/override",
                ModelOverride = "opus",
                ForkSession = true,
            });

        Assert.Same(session, restored.SourceSession);
        Assert.Equal("/work/override", restored.WorkingDirectory);
        Assert.Equal("opus", restored.Model);
        Assert.False(restored.ContinueExistingSession);
        Assert.Equal(loadResult.Messages, restored.Messages);
        Assert.Equal(loadResult.TotalUsage, restored.TotalUsage);
        Assert.Equal(loadResult.Metadata.Title, restored.Metadata.Title);
        Assert.Equal(loadResult.Metadata.Mode, restored.Metadata.Mode);
        Assert.Contains("alpha", restored.Metadata.Tags);
    }

    [Fact]
    public async Task RestoreAsyncKeepsOriginalSessionWhenNoOverridesAreSupplied()
    {
        var session = new TranscriptSession
        {
            SessionId = "session-2",
            SessionDirectory = "/work/session-2",
            TranscriptPath = "/work/session-2/transcript.jsonl",
            ManifestPath = "/work/session-2/manifest.json",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            WorkingDirectory = "/work/original",
            Model = "sonnet",
            Metadata = new ConversationSessionMetadata(),
        };

        var loadResult = new ResumeLoadResult
        {
            Session = session,
            Messages = [StorageTestData.UserText("msg-1", "hello")],
            TotalUsage = TokenUsage.Empty,
            Metadata = session.Metadata.Clone(),
        };

        var restored = await new SessionRestorePipeline().RestoreAsync(
            loadResult,
            new ResumeOptions());

        Assert.True(restored.ContinueExistingSession);
        Assert.Equal(session.WorkingDirectory, restored.WorkingDirectory);
        Assert.Equal(session.Model, restored.Model);
        Assert.NotSame(loadResult.Metadata, restored.Metadata);
    }
}

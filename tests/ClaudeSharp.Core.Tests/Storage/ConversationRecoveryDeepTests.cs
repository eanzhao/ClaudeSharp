using System.Text.Json;
using ClaudeSharp.Core.Compaction;
using ClaudeSharp.Core.Messages;
using ClaudeSharp.Core.Storage;

namespace ClaudeSharp.Core.Tests.Storage;

/// <summary>
/// Contains tests for conversation Recovery Deep.
/// </summary>
public sealed class ConversationRecoveryDeepTests
{
    [Fact]
    public void Recover_FallsBackToLatestLeafWhenCurrentLeafIsMissing()
    {
        var user1 = StorageTestData.UserText("user-1", "first");
        var assistant1 = StorageTestData.Assistant("assistant-1", new TextBlock("second"));
        var projection = BuildProjection(
            [user1, assistant1],
            [null, user1.Id],
            currentLeafMessageId: "missing");

        var recovered = new ConversationRecovery().Recover(projection);

        Assert.Equal(["user-1", "assistant-1"], recovered.Messages.Select(message => message.Id));
    }

    [Fact]
    public void Recover_UsesCheckpointChainWhenCurrentLeafIsBlank()
    {
        var user1 = StorageTestData.ToolResult("user-1", "tool-1", "raw tool output");
        var assistant1 = StorageTestData.ThinkingAssistant("assistant-1", "old thinking", "sig-1");
        var tailUser = StorageTestData.UserText("user-2", "tail");

        var checkpoint = new ConversationCheckpoint
        {
            ActiveMessageIds = [user1.Id, assistant1.Id],
        };

        var projection = BuildProjection(
            [user1, assistant1, tailUser],
            [null, user1.Id, assistant1.Id],
            currentLeafMessageId: null,
            metadataEntries:
            [
                new TranscriptMetadataEntry(
                    "conversation-checkpoint",
                    checkpoint.ToMetadataPayload()),
            ]);

        var recovered = new ConversationRecovery().Recover(projection);

        Assert.Equal(["user-1", "assistant-1"], recovered.Messages.Select(message => message.Id));
    }

    [Fact]
    public void Recover_ReplaysMicrocompactStateAndClearsItAfterResetHead()
    {
        var user1 = StorageTestData.ToolResult("user-1", "tool-1", "raw tool output");
        var assistant1 = StorageTestData.ThinkingAssistant("assistant-1", "old thinking", "sig-1") with
        {
            Usage = new TokenUsage { InputTokens = 2, OutputTokens = 3 },
        };
        var user2 = StorageTestData.UserText("user-2", "tail");

        var projection = BuildProjection(
            [user1, assistant1, user2],
            [null, user1.Id, assistant1.Id],
            currentLeafMessageId: user2.Id,
            metadataEntries:
            [
                StorageTestData.Metadata(
                    MicrocompactRecord.EventType,
                    new
                    {
                        edits = new[]
                        {
                            new { message_id = user1.Id, clear_tool_result = true },
                        },
                    }),
                StorageTestData.Metadata("reset-head"),
                StorageTestData.Metadata(
                    MicrocompactRecord.EventType,
                    new
                    {
                        edits = new[]
                        {
                            new { message_id = assistant1.Id, clear_thinking = true },
                        },
                    }),
            ]);

        var recovered = new ConversationRecovery().Recover(projection);

        var recoveredUser = Assert.IsType<UserMessage>(recovered.Messages[0]);
        Assert.Equal("raw tool output", Assert.Single(recoveredUser.Content.OfType<ToolResultBlock>()).Content);

        var recoveredAssistant = Assert.IsType<AssistantMessage>(recovered.Messages[1]);
        Assert.Equal(MicrocompactPlaceholders.OldThinking, Assert.Single(recoveredAssistant.Content.OfType<ThinkingBlock>()).Text);
        Assert.Equal(assistant1.Usage, recovered.TotalUsage);
    }

    [Fact]
    public void Recover_PrunesEmptyAssistantMessagesAndStopsOnCycles()
    {
        var user1 = StorageTestData.UserText("user-1", "first");
        var emptyAssistant = StorageTestData.Assistant("assistant-empty");
        var user2 = StorageTestData.UserText("user-2", "tail");
        var projection = BuildProjection(
            [user1, emptyAssistant, user2],
            [user2.Id, user1.Id, emptyAssistant.Id],
            currentLeafMessageId: user2.Id);

        var recovered = new ConversationRecovery().Recover(projection);

        Assert.Equal(["user-1", "user-2"], recovered.Messages.Select(message => message.Id));
    }

    private static TranscriptProjection BuildProjection(
        IReadOnlyList<ConversationMessage> messages,
        IReadOnlyList<string?> parentMessageIds,
        string? currentLeafMessageId,
        IReadOnlyList<TranscriptMetadataEntry>? metadataEntries = null)
    {
        var session = new TranscriptSession
        {
            SessionId = "session-1",
            SessionDirectory = "/tmp/session-1",
            TranscriptPath = "/tmp/session-1/transcript.jsonl",
            ManifestPath = "/tmp/session-1/manifest.json",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            WorkingDirectory = "/work/project",
            Model = "sonnet",
            CurrentLeafMessageId = currentLeafMessageId,
            Metadata = new ConversationSessionMetadata(),
        };

        var projectedMessages = messages
            .Select((message, index) => new StoredTranscriptMessage(
                message,
                parentMessageIds[index],
                index + 1))
            .ToDictionary(message => message.Message.Id, message => message, StringComparer.Ordinal);

        return new TranscriptProjection
        {
            Session = session,
            MessagesById = projectedMessages,
            MetadataEntries = metadataEntries ?? [],
        };
    }
}

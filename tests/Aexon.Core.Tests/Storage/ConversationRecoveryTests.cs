using System.Text.Json;
using Aexon.Core.Compaction;
using Aexon.Core.Messages;
using Aexon.Core.Permissions;
using Aexon.Core.Storage;

namespace Aexon.Core.Tests.Storage;

/// <summary>
/// Contains tests for conversation Recovery.
/// </summary>
public sealed class ConversationRecoveryTests
{
    [Fact]
    public void Recover_UsesLatestLeafWhenNoCheckpointIsPresent()
    {
        var user1 = StorageTestData.UserText("user-1", "first");
        var assistant1 = StorageTestData.Assistant("assistant-1", new TextBlock("second"));
        var projection = BuildProjection(
            [user1, assistant1],
            [null, user1.Id],
            currentLeafMessageId: null);

        var recovered = new ConversationRecovery().Recover(projection);

        Assert.Equal(["user-1", "assistant-1"], recovered.Messages.Select(message => message.Id));
        Assert.Null(projection.Session.CurrentLeafMessageId);
    }

    [Fact]
    public void Recover_ReturnsEmptyAfterResetHeadWithNoNewLeaf()
    {
        var projection = BuildProjection(
            [StorageTestData.UserText("user-1", "before reset")],
            [null],
            currentLeafMessageId: null,
            metadataEntries:
            [
                StorageTestData.Metadata("reset-head"),
            ]);

        var recovered = new ConversationRecovery().Recover(projection);

        Assert.Empty(recovered.Messages);
    }

    [Fact]
    public void Recover_RespectsCheckpointTailAndMicrocompactState()
    {
        var firstUser = StorageTestData.ToolResult("user-1", "tool-1", "raw tool output");
        var firstAssistant = StorageTestData.ThinkingAssistant("assistant-1", "old thinking", "sig-1") with
        {
            Usage = new TokenUsage { InputTokens = 2, OutputTokens = 3 },
        };
        var blankAssistant = StorageTestData.Assistant("assistant-blank", new TextBlock("   "));
        var tailUser = StorageTestData.UserText("user-2", "tail");

        var checkpoint = new ConversationCheckpoint
        {
            ActiveMessageIds = [firstUser.Id, firstAssistant.Id],
        };
        var microcompact = new MicrocompactRecord
        {
            Edits =
            [
                new MicrocompactEdit
                {
                    MessageId = firstUser.Id,
                    ClearToolResult = true,
                },
                new MicrocompactEdit
                {
                    MessageId = firstAssistant.Id,
                    ClearThinking = true,
                },
            ],
        };

        var projection = BuildProjection(
            [firstUser, firstAssistant, blankAssistant, tailUser],
            [null, firstUser.Id, firstAssistant.Id, blankAssistant.Id],
            currentLeafMessageId: tailUser.Id,
            metadataEntries:
            [
                new TranscriptMetadataEntry(
                    ConversationCheckpointRecordName,
                    checkpoint.ToMetadataPayload()),
                new TranscriptMetadataEntry(
                    MicrocompactRecord.EventType,
                    microcompact.ToMetadataPayload()),
            ]);

        var recovered = new ConversationRecovery().Recover(projection);

        Assert.Equal(["user-1", "assistant-1", "user-2"], recovered.Messages.Select(message => message.Id));
        Assert.Equal(firstAssistant.Usage, recovered.TotalUsage);

        var recoveredUser = Assert.IsType<UserMessage>(recovered.Messages[0]);
        Assert.Equal(MicrocompactPlaceholders.OldToolResult, Assert.Single(recoveredUser.Content.OfType<ToolResultBlock>()).Content);

        var recoveredAssistant = Assert.IsType<AssistantMessage>(recovered.Messages[1]);
        Assert.Equal(MicrocompactPlaceholders.OldThinking, Assert.Single(recoveredAssistant.Content.OfType<ThinkingBlock>()).Text);

        var recoveredTail = Assert.IsType<UserMessage>(recovered.Messages[2]);
        Assert.Equal("tail", Assert.Single(recoveredTail.Content.OfType<TextBlock>()).Text);
    }

    [Fact]
    public void ConversationCheckpointRoundTripsThroughMetadataPayload()
    {
        var checkpoint = new ConversationCheckpoint
        {
            ActiveMessageIds = ["msg-a", "msg-b", "msg-c"],
        };

        var entry = new TranscriptMetadataEntry(
            ConversationCheckpointRecordName,
            checkpoint.ToMetadataPayload());

        Assert.True(ConversationCheckpoint.TryParse(entry, out var parsed));
        Assert.NotNull(parsed);
        Assert.Equal(checkpoint.ActiveMessageIds, parsed!.ActiveMessageIds);
        Assert.Equal("msg-c", parsed.LeafMessageId);
    }

    [Fact]
    public void ConversationCheckpointAcceptsPascalCasePayload()
    {
        var entry = new TranscriptMetadataEntry(
            ConversationCheckpointRecordName,
            JsonDocument.Parse(
                """
                {
                  "MessageIds": ["left", "right"]
                }
                """).RootElement.Clone());

        Assert.True(ConversationCheckpoint.TryParse(entry, out var parsed));
        Assert.NotNull(parsed);
        Assert.Equal(["left", "right"], parsed!.ActiveMessageIds);
    }

    [Fact]
    public void MicrocompactRecordRoundTripsThroughMetadataPayload()
    {
        var record = new MicrocompactRecord
        {
            Edits =
            [
                new MicrocompactEdit
                {
                    MessageId = "msg-a",
                    ClearToolResult = true,
                    ClearThinking = false,
                },
            ],
        };

        var entry = new TranscriptMetadataEntry(
            MicrocompactRecord.EventType,
            record.ToMetadataPayload());

        Assert.True(MicrocompactRecord.TryParse(entry, out var parsed));
        Assert.NotNull(parsed);
        Assert.Equal(record.Edits[0].MessageId, parsed!.Edits[0].MessageId);
        Assert.True(parsed.Edits[0].ClearToolResult);
    }

    [Fact]
    public void MicrocompactRecordAcceptsPascalCasePayload()
    {
        var entry = new TranscriptMetadataEntry(
            MicrocompactRecord.EventType,
            JsonDocument.Parse(
                """
                {
                  "Edits": [
                    {
                      "MessageId": "msg-a",
                      "ClearToolResult": true,
                      "ClearThinking": true
                    }
                  ]
                }
                """).RootElement.Clone());

        Assert.True(MicrocompactRecord.TryParse(entry, out var parsed));
        Assert.NotNull(parsed);
        Assert.Single(parsed!.Edits);
        Assert.Equal("msg-a", parsed.Edits[0].MessageId);
        Assert.True(parsed.Edits[0].ClearThinking);
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

    private const string ConversationCheckpointRecordName = "conversation-checkpoint";
}

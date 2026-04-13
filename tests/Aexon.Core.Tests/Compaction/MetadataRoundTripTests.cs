using System.Text.Json;
using Aexon.Core.Compaction;
using Aexon.Core.Storage;

namespace Aexon.Core.Tests.Compaction;

/// <summary>
/// Contains tests for metadata Round Trip.
/// </summary>
public sealed class MetadataRoundTripTests
{
    [Fact]
    public void MicrocompactRecord_RoundTripsCamelCasePayload()
    {
        var record = new MicrocompactRecord
        {
            Edits =
            [
                new MicrocompactEdit
                {
                    MessageId = "m-1",
                    ClearToolResult = true,
                    ClearThinking = false,
                },
                new MicrocompactEdit
                {
                    MessageId = "m-2",
                    ClearToolResult = false,
                    ClearThinking = true,
                },
            ],
        };

        var payload = record.ToMetadataPayload();
        var entry = new TranscriptMetadataEntry(MicrocompactRecord.EventType, payload);

        Assert.True(MicrocompactRecord.TryParse(entry, out var parsed));
        Assert.NotNull(parsed);
        Assert.Equal(2, parsed!.Edits.Count);
        Assert.Equal("m-1", parsed.Edits[0].MessageId);
        Assert.True(parsed.Edits[0].ClearToolResult);
        Assert.False(parsed.Edits[0].ClearThinking);
        Assert.Equal("m-2", parsed.Edits[1].MessageId);
        Assert.False(parsed.Edits[1].ClearToolResult);
        Assert.True(parsed.Edits[1].ClearThinking);
    }

    [Fact]
    public void MicrocompactRecord_TryParseAcceptsPascalCasePayload()
    {
        using var doc = JsonDocument.Parse("""
        {
          "Edits": [
            {
              "MessageId": "legacy-1",
              "ClearToolResult": true,
              "ClearThinking": false
            }
          ]
        }
        """);

        var entry = new TranscriptMetadataEntry(
            MicrocompactRecord.EventType,
            doc.RootElement.Clone());

        Assert.True(MicrocompactRecord.TryParse(entry, out var parsed));
        Assert.NotNull(parsed);
        Assert.Single(parsed!.Edits);
        Assert.Equal("legacy-1", parsed.Edits[0].MessageId);
    }

    [Fact]
    public void MicrocompactRecord_TryParseRejectsWrongEventType()
    {
        var entry = new TranscriptMetadataEntry(
            "other",
            CompactionTestHelpers.Json(new { edits = Array.Empty<object>() }));

        Assert.False(MicrocompactRecord.TryParse(entry, out var parsed));
        Assert.Null(parsed);
    }

    [Fact]
    public void ConversationCheckpoint_RoundTripsCamelCasePayload()
    {
        var checkpoint = new ConversationCheckpoint
        {
            ActiveMessageIds = ["m-1", "m-2", "m-3"],
        };

        var entry = new TranscriptMetadataEntry(
            "conversation-checkpoint",
            checkpoint.ToMetadataPayload());

        Assert.True(ConversationCheckpoint.TryParse(entry, out var parsed));
        Assert.NotNull(parsed);
        Assert.Equal(["m-1", "m-2", "m-3"], parsed!.ActiveMessageIds);
        Assert.Equal("m-3", parsed.LeafMessageId);
    }

    [Fact]
    public void ConversationCheckpoint_TryParseAcceptsPascalCasePayload()
    {
        using var doc = JsonDocument.Parse("""
        {
          "MessageIds": ["legacy-1", "legacy-2"]
        }
        """);

        var entry = new TranscriptMetadataEntry(
            "conversation-checkpoint",
            doc.RootElement.Clone());

        Assert.True(ConversationCheckpoint.TryParse(entry, out var parsed));
        Assert.NotNull(parsed);
        Assert.Equal(["legacy-1", "legacy-2"], parsed!.ActiveMessageIds);
    }

    [Fact]
    public void ConversationCheckpoint_TryParseRejectsWrongEventType()
    {
        var entry = new TranscriptMetadataEntry(
            "other",
            CompactionTestHelpers.Json(new { message_ids = new[] { "m-1" } }));

        Assert.False(ConversationCheckpoint.TryParse(entry, out var parsed));
        Assert.Null(parsed);
    }
}

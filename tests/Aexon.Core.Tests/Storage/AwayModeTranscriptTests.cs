using Aexon.Core.Messages;
using Aexon.Core.Storage;

namespace Aexon.Core.Tests.Storage;

public sealed class AwayModeTranscriptTests
{
    [Fact]
    public async Task SystemAwaySummaryMessage_RoundTripsThroughTranscript()
    {
        using var temp = new TempDirectoryScope(nameof(SystemAwaySummaryMessage_RoundTripsThroughTranscript));
        var store = new JsonlTranscriptStore(temp.RootPath);
        var session = await store.CreateSessionAsync("/work", "sonnet");
        var journal = new ConversationJournal(store, session);

        var enteredAt = DateTimeOffset.UtcNow.AddMinutes(-15);
        var exitedAt = DateTimeOffset.UtcNow;
        var summary = new SystemAwaySummaryMessage
        {
            AwayEnteredAt = enteredAt,
            AwayExitedAt = exitedAt,
            TriggerReason = "lunch",
            SummaryText = "User was away for 15m. Reason: lunch.",
        };

        await journal.AppendMessageAsync(summary, "/work", "sonnet");

        var projection = await store.LoadProjectionAsync(session, new TranscriptLoadOptions());
        var loaded = projection.MessagesById[summary.Id].Message;

        Assert.IsType<SystemAwaySummaryMessage>(loaded);
        var roundTripped = (SystemAwaySummaryMessage)loaded;
        Assert.Equal("lunch", roundTripped.TriggerReason);
        Assert.Equal("User was away for 15m. Reason: lunch.", roundTripped.SummaryText);
        Assert.Equal(enteredAt, roundTripped.AwayEnteredAt);
        Assert.Equal(exitedAt, roundTripped.AwayExitedAt);
    }

    [Fact]
    public async Task AwayMetadataEntries_RestoreAwayState()
    {
        using var temp = new TempDirectoryScope(nameof(AwayMetadataEntries_RestoreAwayState));
        var store = new JsonlTranscriptStore(temp.RootPath);
        var session = await store.CreateSessionAsync("/work", "sonnet");
        var journal = new ConversationJournal(store, session);

        var now = DateTimeOffset.UtcNow;
        await journal.AppendMetadataEntryAsync(
            new TranscriptMetadataEntry(
                "away-enter",
                System.Text.Json.JsonSerializer.SerializeToElement(new
                {
                    trigger = "meeting",
                    entered_at = now,
                }),
                now));

        var projection = await store.LoadProjectionAsync(session, new TranscriptLoadOptions());
        Assert.Equal(now, projection.Session.Metadata.AwayEnteredAt);
        Assert.Equal("meeting", projection.Session.Metadata.AwayTriggerReason);

        await journal.AppendMetadataEntryAsync(
            new TranscriptMetadataEntry("away-exit", null, now));

        var projection2 = await store.LoadProjectionAsync(session, new TranscriptLoadOptions());
        Assert.Null(projection2.Session.Metadata.AwayEnteredAt);
        Assert.Null(projection2.Session.Metadata.AwayTriggerReason);
    }

    [Fact]
    public async Task ManifestPersistsAwayState()
    {
        using var temp = new TempDirectoryScope(nameof(ManifestPersistsAwayState));
        var store = new JsonlTranscriptStore(temp.RootPath);
        var session = await store.CreateSessionAsync("/work", "sonnet");

        var now = DateTimeOffset.UtcNow;
        session.Metadata.AwayEnteredAt = now;
        session.Metadata.AwayTriggerReason = "bio break";
        await store.UpdateSessionAsync(session);

        var restored = await store.FindSessionAsync(session.SessionId);
        Assert.NotNull(restored);
        Assert.Equal(now, restored!.Metadata.AwayEnteredAt);
        Assert.Equal("bio break", restored.Metadata.AwayTriggerReason);
    }
}

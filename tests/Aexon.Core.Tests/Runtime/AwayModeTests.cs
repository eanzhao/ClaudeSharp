using Aexon.Core.Messages;
using Aexon.Core.Permissions;
using Aexon.Core.Query;
using Aexon.Core.Storage;
using Aexon.Core.Tools;

namespace Aexon.Core.Tests.Runtime;

public sealed class AwayModeTests
{
    [Fact]
    public async Task EnterAndExitAwayMode_ProducesAwaySummaryMessage()
    {
        using var temp = new TempDirectory();
        var journal = new RecordingJournal();
        var engine = CreateEngine(temp.Root, journal);

        var entered = await engine.EnterAwayModeAsync("lunch break");

        Assert.True(entered);
        Assert.True(engine.IsAwayModeActive);
        Assert.NotNull(engine.AwayEnteredAt);
        Assert.Equal("lunch break", engine.AwayTriggerReason);

        var summary = await engine.ExitAwayModeAsync();

        Assert.NotNull(summary);
        Assert.False(engine.IsAwayModeActive);
        Assert.Null(engine.AwayEnteredAt);
        Assert.Null(engine.AwayTriggerReason);
        Assert.Equal("lunch break", summary.TriggerReason);
        Assert.Contains("lunch break", summary.SummaryText, StringComparison.Ordinal);
        Assert.True(summary.AwayDuration >= TimeSpan.Zero);
    }

    [Fact]
    public async Task EnterAwayMode_WhenAlreadyActive_ReturnsFalse()
    {
        using var temp = new TempDirectory();
        var journal = new RecordingJournal();
        var engine = CreateEngine(temp.Root, journal);

        var first = await engine.EnterAwayModeAsync("first");
        var second = await engine.EnterAwayModeAsync("second");

        Assert.True(first);
        Assert.False(second);
        Assert.Equal("first", engine.AwayTriggerReason);
    }

    [Fact]
    public async Task ExitAwayMode_WhenNotActive_ReturnsNull()
    {
        using var temp = new TempDirectory();
        var journal = new RecordingJournal();
        var engine = CreateEngine(temp.Root, journal);

        var summary = await engine.ExitAwayModeAsync();

        Assert.Null(summary);
    }

    [Fact]
    public async Task AwayMode_AppendsMetadataEntriesToJournal()
    {
        using var temp = new TempDirectory();
        var journal = new RecordingJournal();
        var engine = CreateEngine(temp.Root, journal);

        await engine.EnterAwayModeAsync("meeting");
        await engine.ExitAwayModeAsync();

        Assert.Contains(journal.MetadataEntries, e => e.EventType == "away-enter");
        Assert.Contains(journal.MetadataEntries, e => e.EventType == "away-exit");
    }

    [Fact]
    public async Task AwayMode_ExitAppendsSummaryMessageToConversation()
    {
        using var temp = new TempDirectory();
        var journal = new RecordingJournal();
        var engine = CreateEngine(temp.Root, journal);

        await engine.EnterAwayModeAsync("coffee");
        await engine.ExitAwayModeAsync();

        Assert.Single(engine.Messages);
        Assert.IsType<SystemAwaySummaryMessage>(engine.Messages[0]);
        var msg = (SystemAwaySummaryMessage)engine.Messages[0];
        Assert.Equal("coffee", msg.TriggerReason);
    }

    [Fact]
    public void AwayModeRestoredFromMetadata()
    {
        using var temp = new TempDirectory();
        var enteredAt = DateTimeOffset.UtcNow.AddMinutes(-30);
        var metadata = new ConversationSessionMetadata
        {
            AwayEnteredAt = enteredAt,
            AwayTriggerReason = "restored",
        };

        var engine = CreateEngine(temp.Root, new RecordingJournal(), initialMetadata: metadata);

        Assert.True(engine.IsAwayModeActive);
        Assert.Equal(enteredAt, engine.AwayEnteredAt);
        Assert.Equal("restored", engine.AwayTriggerReason);
    }

    private static QueryEngine CreateEngine(
        string workingDirectory,
        RecordingJournal journal,
        ConversationSessionMetadata? initialMetadata = null)
    {
        var provider = new Context.ContextProvider
        {
            WorkingDirectory = workingDirectory,
        };

        return TestSupport.CreateQueryEngine(
            TestSupport.CreateChatClient(new FakeAnthropicHandler()),
            new ToolRegistry(),
            provider,
            new DefaultPermissionChecker(),
            new QueryEngineConfig(),
            journal: journal,
            initialMetadata: initialMetadata);
    }
}

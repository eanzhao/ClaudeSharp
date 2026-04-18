using Aexon.Cli;
using Aexon.Core.Commands;

namespace Aexon.Core.Tests.Cli;

public sealed class LineEditorHistoryTests
{
    [Fact]
    public void MovePreviousAndNext_RestoresDraftInput()
    {
        var history = new LineEditorHistory(["/help", "status"]);

        Assert.Equal("status", history.MovePrevious("draft message"));
        Assert.Equal("/help", history.MovePrevious("status"));
        Assert.Equal("status", history.MoveNext("/help"));
        Assert.Equal("draft message", history.MoveNext("status"));
    }

    [Fact]
    public void AddEntry_TrimsToMaximumHistorySize()
    {
        var entries = Enumerable.Range(0, LineEditor.MaxHistoryEntries + 5)
            .Select(index => $"cmd-{index}")
            .ToList();
        var history = new LineEditorHistory(entries);

        history.AddEntry("cmd-tail");

        Assert.Equal(LineEditor.MaxHistoryEntries, history.Entries.Count);
        Assert.Equal("cmd-6", history.Entries[0]);
        Assert.Equal("cmd-tail", history.Entries[^1]);
    }
}

public sealed class LineEditorWordDeletionTests
{
    [Fact]
    public void DeleteWordBack_RemovesPreviousWord()
    {
        var buffer = new LineEditorBuffer("alpha beta gamma");

        var deleted = buffer.DeleteWordBack();

        Assert.True(deleted);
        Assert.Equal("alpha beta ", buffer.Text);
        Assert.Equal(buffer.Text.Length, buffer.Cursor);
    }

    [Fact]
    public void DeleteWordBack_RemovesTrailingWhitespaceBeforeWord()
    {
        var buffer = new LineEditorBuffer("alpha beta   ");

        var deleted = buffer.DeleteWordBack();

        Assert.True(deleted);
        Assert.Equal("alpha ", buffer.Text);
        Assert.Equal(buffer.Text.Length, buffer.Cursor);
    }
}

public sealed class LineEditorKillToEndTests
{
    [Fact]
    public void DeleteToEnd_RemovesEverythingAfterCursor()
    {
        var buffer = new LineEditorBuffer("hello world", cursor: 5);

        var deleted = buffer.DeleteToEnd();

        Assert.True(deleted);
        Assert.Equal("hello", buffer.Text);
        Assert.Equal(5, buffer.Cursor);
    }

    [Fact]
    public void DeleteToEnd_AtEndOfBuffer_IsNoOp()
    {
        var buffer = new LineEditorBuffer("hello");

        var deleted = buffer.DeleteToEnd();

        Assert.False(deleted);
        Assert.Equal("hello", buffer.Text);
        Assert.Equal(5, buffer.Cursor);
    }
}

public sealed class LineEditorWordNavigationTests
{
    [Fact]
    public void MoveWordLeft_JumpsToStartOfPreviousWord()
    {
        var buffer = new LineEditorBuffer("alpha beta gamma");

        var moved = buffer.MoveWordLeft();

        Assert.True(moved);
        Assert.Equal("alpha beta ".Length, buffer.Cursor);
    }

    [Fact]
    public void MoveWordLeft_InMiddleOfWord_JumpsToWordStart()
    {
        var buffer = new LineEditorBuffer("alpha beta gamma", cursor: 8);

        var moved = buffer.MoveWordLeft();

        Assert.True(moved);
        Assert.Equal(6, buffer.Cursor);
    }

    [Fact]
    public void MoveWordLeft_AtStart_IsNoOp()
    {
        var buffer = new LineEditorBuffer("alpha", cursor: 0);

        var moved = buffer.MoveWordLeft();

        Assert.False(moved);
        Assert.Equal(0, buffer.Cursor);
    }

    [Fact]
    public void MoveWordRight_JumpsToEndOfCurrentWord()
    {
        var buffer = new LineEditorBuffer("alpha beta gamma", cursor: 0);

        var moved = buffer.MoveWordRight();

        Assert.True(moved);
        Assert.Equal(5, buffer.Cursor);
    }

    [Fact]
    public void MoveWordRight_FromWhitespace_JumpsToEndOfNextWord()
    {
        var buffer = new LineEditorBuffer("alpha beta gamma", cursor: 5);

        var moved = buffer.MoveWordRight();

        Assert.True(moved);
        Assert.Equal("alpha beta".Length, buffer.Cursor);
    }

    [Fact]
    public void MoveWordRight_AtEnd_IsNoOp()
    {
        var buffer = new LineEditorBuffer("alpha");

        var moved = buffer.MoveWordRight();

        Assert.False(moved);
        Assert.Equal(5, buffer.Cursor);
    }
}

public sealed class LineEditorHistorySearchTests
{
    [Fact]
    public void FindMatchIndex_EmptyQuery_ReturnsMinusOne()
    {
        var entries = new[] { "foo", "bar", "baz" };

        Assert.Equal(-1, LineEditorHistorySearch.FindMatchIndex(entries, "", occurrence: 0));
    }

    [Fact]
    public void FindMatchIndex_NoMatch_ReturnsMinusOne()
    {
        var entries = new[] { "foo", "bar" };

        Assert.Equal(-1, LineEditorHistorySearch.FindMatchIndex(entries, "xyz", occurrence: 0));
    }

    [Fact]
    public void FindMatchIndex_OccurrenceZero_ReturnsMostRecentMatch()
    {
        var entries = new[] { "git status", "git push", "npm test" };

        Assert.Equal(1, LineEditorHistorySearch.FindMatchIndex(entries, "git", occurrence: 0));
    }

    [Fact]
    public void FindMatchIndex_OccurrenceOne_ReturnsOlderMatch()
    {
        var entries = new[] { "git status", "git push", "npm test" };

        Assert.Equal(0, LineEditorHistorySearch.FindMatchIndex(entries, "git", occurrence: 1));
    }

    [Fact]
    public void FindMatchIndex_OccurrenceBeyondMatches_ReturnsMinusOne()
    {
        var entries = new[] { "git status", "npm test" };

        Assert.Equal(-1, LineEditorHistorySearch.FindMatchIndex(entries, "git", occurrence: 1));
    }

    [Fact]
    public void FindMatchIndex_IsCaseInsensitive()
    {
        var entries = new[] { "Git Status" };

        Assert.Equal(0, LineEditorHistorySearch.FindMatchIndex(entries, "gIt", occurrence: 0));
    }
}

public sealed class LineEditorHintFormatTests
{
    [Fact]
    public void Format_SingleMatch_ReturnsEmpty()
    {
        Assert.Equal(
            string.Empty,
            LineEditorHintFormatter.Format(["/help"], selectedIndex: 0, maxWidth: 80));
    }

    [Fact]
    public void Format_MultipleMatches_HighlightsSelected()
    {
        var formatted = LineEditorHintFormatter.Format(
            ["/help", "/history"],
            selectedIndex: 1,
            maxWidth: 80);

        Assert.Contains("/help", formatted);
        Assert.Contains("[/history]", formatted);
    }

    [Fact]
    public void Format_TruncatesWithEllipsisWhenOverflowingWidth()
    {
        var matches = Enumerable.Range(0, 10)
            .Select(index => $"/command-{index:D2}")
            .ToList();

        var formatted = LineEditorHintFormatter.Format(matches, selectedIndex: 0, maxWidth: 30);

        Assert.True(formatted.Length <= 30, $"Expected <=30 chars, got {formatted.Length}: {formatted}");
        Assert.EndsWith("…", formatted);
    }

    [Fact]
    public void Format_EmptyMatches_ReturnsEmpty()
    {
        Assert.Equal(
            string.Empty,
            LineEditorHintFormatter.Format([], selectedIndex: -1, maxWidth: 80));
    }
}

public sealed class LineEditorKeyBindingTests
{
    [Fact]
    public void CtrlK_DeletesToEndOfBuffer()
    {
        var (editor, buffer) = CreateEditor("hello world", cursor: 5);

        var outcome = editor.InvokeHandleKeyForTest(
            new ConsoleKeyInfo('\0', ConsoleKey.K, shift: false, alt: false, control: true),
            buffer);

        Assert.Equal(LineEditorOutcome.Render, outcome);
        Assert.Equal("hello", buffer.Text);
        Assert.Equal(5, buffer.Cursor);
    }

    [Fact]
    public void AltLeftArrow_MovesByWord()
    {
        var (editor, buffer) = CreateEditor("alpha beta gamma");

        var outcome = editor.InvokeHandleKeyForTest(
            new ConsoleKeyInfo('\0', ConsoleKey.LeftArrow, shift: false, alt: true, control: false),
            buffer);

        Assert.Equal(LineEditorOutcome.Render, outcome);
        Assert.Equal("alpha beta ".Length, buffer.Cursor);
    }

    [Fact]
    public void AltRightArrow_MovesByWord()
    {
        var (editor, buffer) = CreateEditor("alpha beta gamma", cursor: 0);

        var outcome = editor.InvokeHandleKeyForTest(
            new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, shift: false, alt: true, control: false),
            buffer);

        Assert.Equal(LineEditorOutcome.Render, outcome);
        Assert.Equal(5, buffer.Cursor);
    }

    [Fact]
    public void AltBackspace_DeletesPreviousWord()
    {
        var (editor, buffer) = CreateEditor("alpha beta gamma");

        var outcome = editor.InvokeHandleKeyForTest(
            new ConsoleKeyInfo('\u0008', ConsoleKey.Backspace, shift: false, alt: true, control: false),
            buffer);

        Assert.Equal(LineEditorOutcome.Render, outcome);
        Assert.Equal("alpha beta ", buffer.Text);
    }

    private static (LineEditor Editor, LineEditorBuffer Buffer) CreateEditor(string text, int? cursor = null)
    {
        var editor = new LineEditor(new CommandRegistry(), [], Environment.CurrentDirectory);
        var buffer = new LineEditorBuffer(text, cursor);
        return (editor, buffer);
    }
}

public sealed class LineEditorCompletionTests
{
    [Fact]
    public void MatchCommands_ReturnsPrefixMatchesInOrder()
    {
        var matches = LineEditorCompletion.MatchCommands(
            "/m",
            ["/model", "/mode", "/help"]);

        Assert.Equal(["/mode", "/model"], matches);
    }

    [Fact]
    public void TryCreateCommandCompletion_UsesTheLeadingSlashToken()
    {
        var completion = LineEditorCompletion.TryCreateCommandCompletion(
            "/mo fast",
            3,
            ["/model", "/mode", "/help"]);

        Assert.True(completion.HasValue);
        Assert.Equal(0, completion.Value.Start);
        Assert.Equal(3, completion.Value.Length);
        Assert.Equal(["/mode", "/model"], completion.Value.Matches);
    }

    [Fact]
    public void MatchPaths_CompletesRelativeFilesAndDirectories()
    {
        var tempDirectory = Path.Combine(
            Path.GetTempPath(),
            $"aexon-line-editor-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        var srcDirectory = Path.Combine(tempDirectory, "src");
        var cliDirectory = Path.Combine(srcDirectory, "Aexon.Cli");
        Directory.CreateDirectory(cliDirectory);
        File.WriteAllText(Path.Combine(srcDirectory, "Aexon.Core.cs"), string.Empty);

        try
        {
            var separator = Path.DirectorySeparatorChar;
            var matches = LineEditorCompletion.MatchPaths($"src{separator}Ae", tempDirectory);

            Assert.Equal(
            [
                $"src{separator}Aexon.Cli{separator}",
                $"src{separator}Aexon.Core.cs",
            ],
                matches);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }
}

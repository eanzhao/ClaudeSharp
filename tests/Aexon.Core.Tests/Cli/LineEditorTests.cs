using Aexon.Core.Interactive;

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

public sealed class TerminalWidthTests
{
    [Fact]
    public void CharColumns_AsciiReturnsOne()
    {
        Assert.Equal(1, TerminalWidth.CharColumns('a'));
        Assert.Equal(1, TerminalWidth.CharColumns(' '));
    }

    [Fact]
    public void CharColumns_CjkReturnsTwo()
    {
        Assert.Equal(2, TerminalWidth.CharColumns('你'));
        Assert.Equal(2, TerminalWidth.CharColumns('好'));
        Assert.Equal(2, TerminalWidth.CharColumns('凤'));
    }

    [Fact]
    public void CharColumns_ControlCharReturnsZero()
    {
        Assert.Equal(0, TerminalWidth.CharColumns('\0'));
        Assert.Equal(0, TerminalWidth.CharColumns('\t'));
    }

    [Fact]
    public void StringColumns_MixedAsciiAndCjk()
    {
        // "fdsaf反反复复凤飞飞" — 5 ASCII cols + 7 CJK chars × 2 cols = 19 cols.
        Assert.Equal(19, TerminalWidth.StringColumns("fdsaf反反复复凤飞飞"));
    }

    [Fact]
    public void StringColumns_EmojiCountsAsTwo()
    {
        Assert.Equal(2, TerminalWidth.StringColumns("😀"));
    }

    [Fact]
    public void StringColumns_CombiningMarksCountZero()
    {
        // Base 'e' (1) + combining acute (0) = 1 column, not 2.
        Assert.Equal(1, TerminalWidth.StringColumns("e\u0301"));
    }
}

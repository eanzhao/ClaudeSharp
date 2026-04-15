using System.Text.Json;
using Aexon.Core.Permissions;
using Aexon.Core.Tests.Runtime;
using Aexon.Core.Tools;
using Aexon.Tools;
using Aexon.Tools.Shell;

namespace Aexon.Core.Tests.Tools;

/// <summary>
/// Contains tests for bash tooling support.
/// </summary>
public sealed class BashToolingTests
{
    [Theory]
    [InlineData("pwd", BashCommandCategory.ReadOnly, "pwd")]
    [InlineData("mkdir output", BashCommandCategory.Write, "mkdir")]
    [InlineData("rm -rf output", BashCommandCategory.Destructive, "rm")]
    [InlineData("echo ok > file.txt", BashCommandCategory.Write, "redirection")]
    [InlineData("sed -i 's/a/b/' file.txt", BashCommandCategory.Write, "sed")]
    [InlineData("FOO=bar env pwd", BashCommandCategory.ReadOnly, "pwd")]
    [InlineData("sudo ls", BashCommandCategory.Destructive, "sudo")]
    [InlineData("totally-unknown", BashCommandCategory.Unknown, "totally-unknown")]
    public void BashCommandClassifier_ClassifiesCommonCommands(
        string command,
        BashCommandCategory expectedCategory,
        string expectedBaseCommand)
    {
        var classification = BashCommandClassifier.Classify(command);

        Assert.Equal(expectedCategory, classification.Category);
        Assert.Equal(expectedBaseCommand, classification.BaseCommand);
    }

    [Theory]
    [InlineData("git branch --all", BashCommandCategory.ReadOnly)]
    [InlineData("git branch feature", BashCommandCategory.Write)]
    [InlineData("git branch -D feature", BashCommandCategory.Destructive)]
    [InlineData("git reset --hard HEAD~1", BashCommandCategory.Destructive)]
    [InlineData("git checkout -- file.txt", BashCommandCategory.Destructive)]
    [InlineData("git remote show origin", BashCommandCategory.ReadOnly)]
    [InlineData("git stash pop", BashCommandCategory.Destructive)]
    [InlineData("git config --get user.name", BashCommandCategory.ReadOnly)]
    [InlineData("git config user.name codex", BashCommandCategory.Write)]
    [InlineData("gh pr view 123", BashCommandCategory.ReadOnly)]
    [InlineData("gh pr create", BashCommandCategory.Unknown)]
    public void BashCommandClassifier_ClassifiesGitAndGhCommands(
        string command,
        BashCommandCategory expectedCategory)
    {
        var classification = BashCommandClassifier.Classify(command);

        Assert.Equal(expectedCategory, classification.Category);
        Assert.Equal("git" is var _ && command.StartsWith("gh ", StringComparison.Ordinal) ? "gh" : "git", classification.BaseCommand);
    }

    [Theory]
    [InlineData("grep needle file.txt", 1, false, "No matches found")]
    [InlineData("rg needle file.txt", 2, true, null)]
    [InlineData("find . -name foo", 1, false, "Some directories were inaccessible")]
    [InlineData("diff a b", 1, false, "Files differ")]
    [InlineData("test -f nope", 1, false, "Condition is false")]
    [InlineData("[ -f nope ]", 2, true, null)]
    [InlineData("customcmd", 3, true, "Command failed with exit code 3")]
    public void CommandSemantics_Interpret_UsesCommandSpecificRules(
        string command,
        int exitCode,
        bool expectedError,
        string? expectedMessage)
    {
        var interpretation = CommandSemantics.Interpret(command, exitCode, "", "");

        Assert.Equal(expectedError, interpretation.IsError);
        Assert.Equal(expectedMessage, interpretation.Message);
    }

    [Fact]
    public void CommandSemantics_Interpret_IgnoresQuotedSeparatorsWhenFindingBaseCommand()
    {
        var interpretation = CommandSemantics.Interpret(
            "echo \"a && b\" && grep needle file.txt",
            1,
            "",
            "");

        Assert.False(interpretation.IsError);
        Assert.Equal("No matches found", interpretation.Message);
    }

    [Fact]
    public async Task BashTool_ExecuteAsync_StreamsOutputAndReturnsSuccess()
    {
        using var temp = new TempDirectory();
        var tool = new BashTool();
        var progressMessages = new List<string>();
        var progress = new CollectingProgress(progressMessages);

        var result = await tool.ExecuteAsync(
            Json(new { command = "printf 'hello\\n'" }),
            CreateContext(temp.Root),
            progress);

        Assert.False(result.IsError);
        Assert.Equal("hello", result.Data);
        Assert.Contains("hello", progressMessages, StringComparer.Ordinal);
    }

    [Fact]
    public async Task BashTool_ExecuteAsync_TreatsSemanticNonErrorsAsSuccess()
    {
        using var temp = new TempDirectory();
        var tool = new BashTool();

        var result = await tool.ExecuteAsync(
            Json(new { command = "grep needle /dev/null" }),
            CreateContext(temp.Root));

        Assert.False(result.IsError);
        Assert.Contains("Note: No matches found", result.Data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BashTool_ExecuteAsync_FormatsErrorsAndTimeouts()
    {
        using var temp = new TempDirectory();
        var tool = new BashTool();

        var errorCommand = OperatingSystem.IsWindows()
            ? "echo oops 1>&2 & exit /b 3"
            : "printf 'oops\\n' 1>&2; exit 3";
        var slowCommand = OperatingSystem.IsWindows()
            ? "ping 127.0.0.1 -n 10 >nul"
            : "sleep 1";

        var failed = await tool.ExecuteAsync(
            Json(new { command = errorCommand }),
            CreateContext(temp.Root));
        var timedOut = await tool.ExecuteAsync(
            Json(new { command = slowCommand, timeout = 50 }),
            CreateContext(temp.Root));

        Assert.True(failed.IsError);
        Assert.Contains("STDERR:", failed.Data, StringComparison.Ordinal);
        Assert.Contains("oops", failed.Data, StringComparison.Ordinal);
        Assert.Contains("Exit code: 3", failed.Data, StringComparison.Ordinal);
        Assert.True(timedOut.IsError);
        Assert.Contains("Command timed out after", timedOut.Data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BashTool_PermissionAndDisplayHelpers_ReflectCommandClassification()
    {
        var tool = new BashTool();
        var context = CreateContext("/tmp");

        var readPermission = await tool.CheckPermissionsAsync(
            Json(new { command = "pwd" }),
            context);
        var writePermission = await tool.CheckPermissionsAsync(
            Json(new { command = "mkdir output" }),
            context);
        var destructivePermission = await tool.CheckPermissionsAsync(
            Json(new { command = "rm -rf output" }),
            context);

        Assert.Equal(PermissionBehavior.Allow, readPermission.Behavior);
        Assert.Equal(PermissionBehavior.Ask, writePermission.Behavior);
        Assert.Contains("modify files or git state", writePermission.Message, StringComparison.Ordinal);
        Assert.Equal(PermissionBehavior.Ask, destructivePermission.Behavior);
        Assert.Contains("Potentially destructive command", destructivePermission.Message, StringComparison.Ordinal);
        Assert.True(tool.IsReadOnly(Json(new { command = "pwd" })));
        Assert.True(tool.IsConcurrencySafe(Json(new { command = "pwd" })));
        Assert.False(tool.IsReadOnly(Json(new { command = "mkdir output" })));
        Assert.Equal(
            "Running pwd",
            tool.GetActivityDescription(Json(new { command = "pwd" })));

        var longName = tool.GetUserFacingName(
            Json(new { command = new string('x', 70) }));
        Assert.StartsWith("Bash: ", longName, StringComparison.Ordinal);
        Assert.EndsWith("...", longName, StringComparison.Ordinal);
        Assert.Equal(200_000, tool.MaxResultSizeChars);
    }

    private static JsonElement Json(object value) =>
        JsonSerializer.SerializeToElement(value);

    private static ToolExecutionContext CreateContext(string workingDirectory) =>
        new()
        {
            WorkingDirectory = workingDirectory,
            PermissionContext = new PermissionContext
            {
                WorkingDirectory = workingDirectory,
            },
            Tools = [],
            Messages = [],
            CancellationToken = CancellationToken.None,
        };

    private sealed class CollectingProgress(List<string> messages) : IProgress<ToolProgress>
    {
        public void Report(ToolProgress value)
        {
            if (!string.IsNullOrWhiteSpace(value.Message))
                messages.Add(value.Message);
        }
    }
}

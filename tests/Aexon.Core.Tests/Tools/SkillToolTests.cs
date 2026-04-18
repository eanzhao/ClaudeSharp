using System.Text.Json;
using Aexon.Core.Permissions;
using Aexon.Core.Skills;
using Aexon.Core.Tests.Runtime;
using Aexon.Core.Tools;
using Aexon.Tools;

namespace Aexon.Core.Tests.Tools;

public sealed class SkillToolTests
{
    [Fact]
    public async Task SkillTool_ExecuteAsync_ReturnsSkillBodyWhenFound()
    {
        using var temp = new TempDirectory();
        var home = temp.CreateDirectory("home");
        var repo = temp.CreateDirectory("repo");
        temp.WriteFile("repo/.aexon/skills/commit.md", """
        ---
        name: commit
        description: Commit changes cleanly
        ---
        Run git status first.
        """);
        var tool = new SkillTool(new SkillLoader(home), repo);

        var result = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new { name = "commit" }),
            CreateContext(repo));

        Assert.False(result.IsError);
        Assert.Equal("Run git status first.", result.Data.Trim());
        Assert.True(tool.IsReadOnly(default));
        Assert.True(tool.IsConcurrencySafe(default));
        Assert.Equal(
            "Loading skill commit",
            tool.GetActivityDescription(JsonSerializer.SerializeToElement(new { name = "commit" })));
    }

    [Fact]
    public async Task SkillTool_ExecuteAsync_ReturnsAvailableSkillNamesWhenMissing()
    {
        using var temp = new TempDirectory();
        var home = temp.CreateDirectory("home");
        var repo = temp.CreateDirectory("repo");
        temp.WriteFile("home/.aexon/skills/commit.md", """
        ---
        name: commit
        description: Commit changes cleanly
        ---
        Run git status first.
        """);
        var tool = new SkillTool(new SkillLoader(home), repo);

        var result = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new { name = "review" }),
            CreateContext(repo));

        Assert.True(result.IsError);
        Assert.Contains("review", result.Data, StringComparison.Ordinal);
        Assert.Contains("Available skills: commit", result.Data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SkillTool_WorkingDirectorySkillOverridesHomeSkill()
    {
        using var temp = new TempDirectory();
        var home = temp.CreateDirectory("home");
        var repo = temp.CreateDirectory("repo");
        temp.WriteFile("home/.claude/skills/review.md", """
        ---
        name: review
        description: Home review skill
        ---
        HOME BODY
        """);
        temp.WriteFile("repo/.aexon/skills/review.md", """
        ---
        name: review
        description: Repo review skill
        ---
        REPO BODY
        """);
        var tool = new SkillTool(new SkillLoader(home), repo);

        var result = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new { name = "review" }),
            CreateContext(repo));

        Assert.False(result.IsError);
        Assert.Equal("REPO BODY", result.Data.Trim());
    }

    [Fact]
    public async Task SkillTool_ExecuteAsync_TrimsSkillName()
    {
        using var temp = new TempDirectory();
        var home = temp.CreateDirectory("home");
        var repo = temp.CreateDirectory("repo");
        temp.WriteFile("home/.aexon/skills/commit.md", """
        ---
        name: commit
        description: Commit changes cleanly
        ---
        body-of-commit
        """);
        var tool = new SkillTool(new SkillLoader(home), repo);

        var result = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new { name = "  commit  " }),
            CreateContext(repo));

        Assert.False(result.IsError);
        Assert.Equal("body-of-commit", result.Data.Trim());
    }

    [Fact]
    public async Task SkillTool_ExecuteAsync_MissingNameReturnsError()
    {
        using var temp = new TempDirectory();
        var home = temp.CreateDirectory("home");
        var repo = temp.CreateDirectory("repo");
        var tool = new SkillTool(new SkillLoader(home), repo);

        var emptyName = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new { name = "" }),
            CreateContext(repo));
        var missingField = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new { }),
            CreateContext(repo));

        Assert.True(emptyName.IsError);
        Assert.Contains("name is required", emptyName.Data, StringComparison.Ordinal);
        Assert.True(missingField.IsError);
        Assert.Contains("name is required", missingField.Data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SkillTool_ExecuteAsync_MissingSkillReportsNoneWhenEmpty()
    {
        using var temp = new TempDirectory();
        var home = temp.CreateDirectory("home");
        var repo = temp.CreateDirectory("repo");
        var tool = new SkillTool(new SkillLoader(home), repo);

        var result = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new { name = "missing" }),
            CreateContext(repo));

        Assert.True(result.IsError);
        Assert.Contains("Available skills: (none)", result.Data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SkillTool_ValidateInputAsync_RejectsBlankName()
    {
        using var temp = new TempDirectory();
        var home = temp.CreateDirectory("home");
        var repo = temp.CreateDirectory("repo");
        var tool = new SkillTool(new SkillLoader(home), repo);

        var invalid = await tool.ValidateInputAsync(
            JsonSerializer.SerializeToElement(new { name = "   " }),
            CreateContext(repo));
        var valid = await tool.ValidateInputAsync(
            JsonSerializer.SerializeToElement(new { name = "anything" }),
            CreateContext(repo));

        Assert.False(invalid.IsValid);
        Assert.Contains("name is required", invalid.Message, StringComparison.Ordinal);
        Assert.True(valid.IsValid);
    }

    [Fact]
    public async Task SkillTool_GetDescriptionAsync_ReportsNoneWhenDirectoriesMissing()
    {
        using var temp = new TempDirectory();
        var home = temp.CreateDirectory("home");
        var repo = temp.CreateDirectory("repo");
        var tool = new SkillTool(new SkillLoader(home), repo);

        var description = await tool.GetDescriptionAsync();

        Assert.Contains("Available skills: (none)", description, StringComparison.Ordinal);
    }

    [Fact]
    public void SkillTool_GetActivityDescription_UsesSkillNameWhenPresent()
    {
        using var temp = new TempDirectory();
        var home = temp.CreateDirectory("home");
        var repo = temp.CreateDirectory("repo");
        var tool = new SkillTool(new SkillLoader(home), repo);

        var blank = tool.GetActivityDescription(JsonSerializer.SerializeToElement(new { name = "   " }));
        var withName = tool.GetActivityDescription(JsonSerializer.SerializeToElement(new { name = "review" }));
        var noProperty = tool.GetActivityDescription(JsonSerializer.SerializeToElement(new { }));

        Assert.Equal("Loading skill", blank);
        Assert.Equal("Loading skill review", withName);
        Assert.Equal("Loading skill", noProperty);
    }

    [Fact]
    public void SkillTool_ExposesAliasAndName()
    {
        using var temp = new TempDirectory();
        var tool = new SkillTool(new SkillLoader(temp.CreateDirectory("home")), temp.CreateDirectory("repo"));

        Assert.Equal("SkillTool", tool.Name);
        Assert.Contains("Skill", tool.Aliases);
    }

    [Fact]
    public async Task SkillTool_ExecuteAsync_InvalidSkillNameListsAvailableNamesAlphabetically()
    {
        using var temp = new TempDirectory();
        var home = temp.CreateDirectory("home");
        var repo = temp.CreateDirectory("repo");
        temp.WriteFile("home/.claude/skills/zebra.md", """
        ---
        name: zebra
        description: zebra skill
        ---
        z body
        """);
        temp.WriteFile("home/.claude/skills/alpha.md", """
        ---
        name: alpha
        description: alpha skill
        ---
        a body
        """);
        var tool = new SkillTool(new SkillLoader(home), repo);

        var result = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new { name = "missing" }),
            CreateContext(repo));

        Assert.True(result.IsError);
        Assert.Contains("Available skills: alpha, zebra", result.Data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SkillTool_GetDescriptionAsync_ListsAvailableSkills()
    {
        using var temp = new TempDirectory();
        var home = temp.CreateDirectory("home");
        var repo = temp.CreateDirectory("repo");
        temp.WriteFile("home/.aexon/skills/commit.md", """
        ---
        name: commit
        description: Commit changes cleanly
        ---
        body
        """);
        temp.WriteFile("home/.claude/skills/review.md", """
        ---
        name: review
        description: Review the diff
        ---
        body
        """);
        var tool = new SkillTool(new SkillLoader(home), repo);

        var description = await tool.GetDescriptionAsync();

        Assert.Contains("Available skills:", description, StringComparison.Ordinal);
        Assert.Contains("- commit: Commit changes cleanly", description, StringComparison.Ordinal);
        Assert.Contains("- review: Review the diff", description, StringComparison.Ordinal);
    }

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
}

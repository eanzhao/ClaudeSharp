using System.Reflection;
using System.Text.Json;
using ClaudeSharp.Core.Permissions;
using ClaudeSharp.Core.Tests.Runtime;
using ClaudeSharp.Core.Tools;
using ClaudeSharp.Tools;

namespace ClaudeSharp.Core.Tests.Tools;

/// <summary>
/// Contains tests for search-oriented tools.
/// </summary>
public sealed class SearchToolsTests
{
    [Fact]
    public async Task GlobTool_ValidateInput_RequiresPatternAndExistingDirectory()
    {
        using var temp = new TempDirectory();
        var tool = new GlobTool();
        var context = CreateContext(temp.Root);

        var missingPattern = await tool.ValidateInputAsync(Json(new { path = temp.Root }), context);
        var missingDirectory = await tool.ValidateInputAsync(
            Json(new { pattern = "**/*.cs", path = temp.FullPath("missing") }),
            context);

        Assert.False(missingPattern.IsValid);
        Assert.Equal("pattern is required.", missingPattern.Message);
        Assert.False(missingDirectory.IsValid);
        Assert.Contains("Search directory not found", missingDirectory.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GlobTool_ExecuteAsync_PaginatesResultsAndSkipsIgnoredDirectories()
    {
        using var temp = new TempDirectory();
        var older = temp.WriteFile("src/old.cs", "class Old {}");
        var newer = temp.WriteFile("src/new.cs", "class New {}");
        temp.WriteFile("src/third.cs", "class Third {}");
        temp.WriteFile(".git/ignored.cs", "class Ignored {}");
        temp.WriteFile("node_modules/ignored.cs", "class Ignored {}");
        File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddMinutes(-10));
        File.SetLastWriteTimeUtc(newer, DateTime.UtcNow);

        var tool = new GlobTool();
        var result = await tool.ExecuteAsync(
            Json(new
            {
                pattern = "**/*.cs",
                path = temp.Root,
                offset = 1,
                limit = 1,
            }),
            CreateContext(temp.Root));

        Assert.False(result.IsError);
        Assert.Contains("Found 3 file(s)", result.Data, StringComparison.Ordinal);
        Assert.Contains("Showing 2-2:", result.Data, StringComparison.Ordinal);
        Assert.Contains("src", result.Data, StringComparison.Ordinal);
        Assert.DoesNotContain(".git", result.Data, StringComparison.Ordinal);
        Assert.DoesNotContain("node_modules", result.Data, StringComparison.Ordinal);
        Assert.Contains("Results truncated.", result.Data, StringComparison.Ordinal);
        Assert.Equal("Search", tool.GetUserFacingName());
        Assert.Equal(
            "Searching files for \"**/*.cs\"",
            tool.GetActivityDescription(Json(new { pattern = "**/*.cs" })));
    }

    [Fact]
    public async Task GlobTool_ExecuteAsync_ReturnsFriendlyMessageWhenNothingMatches()
    {
        using var temp = new TempDirectory();
        var tool = new GlobTool();

        var result = await tool.ExecuteAsync(
            Json(new { pattern = "**/*.md", path = temp.Root }),
            CreateContext(temp.Root));

        Assert.False(result.IsError);
        Assert.Contains("No files matched pattern", result.Data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GrepTool_ExecuteAsync_SearchesDirectoryWithIncludeFilter()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("src/app.cs", "one\nneedle\nthree\n");
        temp.WriteFile("src/app.txt", "needle\n");

        var tool = new GrepTool();
        var result = await tool.ExecuteAsync(
            Json(new
            {
                pattern = "needle",
                path = temp.Root,
                include = "*.cs",
            }),
            CreateContext(temp.Root));

        Assert.False(result.IsError);
        Assert.Contains("Found 1 match(es):", result.Data, StringComparison.Ordinal);
        Assert.Contains(Path.Combine("src", "app.cs") + ":2: needle", result.Data, StringComparison.Ordinal);
        Assert.DoesNotContain("app.txt", result.Data, StringComparison.Ordinal);
        Assert.Equal("Search", tool.GetUserFacingName());
        Assert.Equal(
            "Searching for \"needle\"",
            tool.GetActivityDescription(Json(new { pattern = "needle" })));
    }

    [Fact]
    public async Task GrepTool_ExecuteAsync_ReportsInvalidRegexAndMissingPath()
    {
        using var temp = new TempDirectory();
        var tool = new GrepTool();

        var invalidRegex = await tool.ExecuteAsync(
            Json(new { pattern = "(" }),
            CreateContext(temp.Root));
        var missingPath = await tool.ExecuteAsync(
            Json(new { pattern = "needle", path = temp.FullPath("missing") }),
            CreateContext(temp.Root));

        Assert.True(invalidRegex.IsError);
        Assert.Contains("Invalid regex pattern", invalidRegex.Data, StringComparison.Ordinal);
        Assert.True(missingPath.IsError);
        Assert.Contains("Path not found", missingPath.Data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GrepTool_ExecuteAsync_SkipsBinaryFiles()
    {
        using var temp = new TempDirectory();
        var binaryPath = temp.FullPath("image.png");
        await File.WriteAllTextAsync(binaryPath, "needle");

        var tool = new GrepTool();
        var result = await tool.ExecuteAsync(
            Json(new { pattern = "needle", path = binaryPath }),
            CreateContext(temp.Root));

        Assert.False(result.IsError);
        Assert.Contains("No matches found", result.Data, StringComparison.Ordinal);
    }

    [Fact]
    public void SearchPathUtilities_ShouldSkipIgnoredDirectories()
    {
        var utilityType = typeof(GlobTool).Assembly.GetType("ClaudeSharp.Tools.Search.SearchPathUtilities");
        Assert.NotNull(utilityType);

        var shouldSkipDirectory = utilityType!.GetMethod(
            "ShouldSkipDirectory",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(shouldSkipDirectory);

        var ignored = Assert.IsType<bool>(shouldSkipDirectory!.Invoke(null, ["/tmp/node_modules"])!);
        var kept = Assert.IsType<bool>(shouldSkipDirectory.Invoke(null, ["/tmp/src"])!);

        Assert.True(ignored);
        Assert.False(kept);
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
}

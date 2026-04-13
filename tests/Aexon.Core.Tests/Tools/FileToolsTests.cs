using System.Text.Json;
using Aexon.Core.Permissions;
using Aexon.Core.Tests.Runtime;
using Aexon.Core.Tools;
using Aexon.Tools;

namespace Aexon.Core.Tests.Tools;

/// <summary>
/// Contains tests for file tools.
/// </summary>
public sealed class FileToolsTests
{
    [Fact]
    public async Task FileReadTool_ExecuteAsync_ReadsRequestedLineRangeFromRelativePath()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("notes.txt", "alpha\nbeta\ngamma\n");

        var tool = new FileReadTool();
        var result = await tool.ExecuteAsync(
            Json(new { file_path = "notes.txt", offset = 1, limit = 1 }),
            CreateContext(temp.Root));

        Assert.False(result.IsError);
        Assert.Contains("notes.txt", result.Data, StringComparison.Ordinal);
        Assert.Contains("showing lines 2-2", result.Data, StringComparison.Ordinal);
        Assert.Contains("2: beta", result.Data, StringComparison.Ordinal);
        Assert.DoesNotContain("1: alpha", result.Data, StringComparison.Ordinal);
        Assert.True(tool.IsReadOnly(default));
        Assert.True(tool.IsConcurrencySafe(default));
        Assert.Equal(int.MaxValue, tool.MaxResultSizeChars);
        Assert.Equal("Reading notes.txt", tool.GetActivityDescription(Json(new { file_path = "notes.txt" })));
    }

    [Fact]
    public async Task FileReadTool_ExecuteAsync_ReturnsImagePreviewAndEmptyFileMessage()
    {
        using var temp = new TempDirectory();
        var imagePath = temp.FullPath("assets", "logo.png");
        Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
        await File.WriteAllBytesAsync(imagePath, [0x89, 0x50, 0x4E, 0x47]);
        temp.WriteFile("empty.txt", "");

        var tool = new FileReadTool();

        var imageResult = await tool.ExecuteAsync(
            Json(new { file_path = imagePath }),
            CreateContext(temp.Root));
        var emptyResult = await tool.ExecuteAsync(
            Json(new { file_path = temp.FullPath("empty.txt") }),
            CreateContext(temp.Root));

        Assert.False(imageResult.IsError);
        Assert.Contains("[Image file:", imageResult.Data, StringComparison.Ordinal);
        Assert.Contains("Base64:", imageResult.Data, StringComparison.Ordinal);
        Assert.False(emptyResult.IsError);
        Assert.Contains("is empty (0 lines)", emptyResult.Data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FileReadTool_ExecuteAsync_ReturnsErrorWhenFileIsMissing()
    {
        var tool = new FileReadTool();

        var result = await tool.ExecuteAsync(
            Json(new { file_path = "/tmp/does-not-exist.txt" }),
            CreateContext("/tmp"));

        Assert.True(result.IsError);
        Assert.Contains("File not found", result.Data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FileWriteTool_ExecuteAsync_CreatesUpdatesAndReportsPermissions()
    {
        using var temp = new TempDirectory();
        var tool = new FileWriteTool();
        var relativeInput = Json(new
        {
            file_path = Path.Combine("nested", "sample.txt"),
            content = "alpha\nbeta",
        });

        var created = await tool.ExecuteAsync(relativeInput, CreateContext(temp.Root));
        var fullPath = temp.FullPath("nested", "sample.txt");
        var updated = await tool.ExecuteAsync(
            Json(new
            {
                file_path = fullPath,
                content = "updated",
            }),
            CreateContext(temp.Root));
        var permission = await tool.CheckPermissionsAsync(
            Json(new { file_path = fullPath, content = "updated" }),
            CreateContext(temp.Root));

        Assert.False(created.IsError);
        Assert.Contains("Created file", created.Data, StringComparison.Ordinal);
        Assert.Contains("(2 lines)", created.Data, StringComparison.Ordinal);
        Assert.False(updated.IsError);
        Assert.Contains("Updated file", updated.Data, StringComparison.Ordinal);
        Assert.Equal("updated", await File.ReadAllTextAsync(fullPath));
        Assert.Equal(PermissionBehavior.Ask, permission.Behavior);
        Assert.Contains(fullPath, permission.Message, StringComparison.Ordinal);
        Assert.True(tool.IsDestructive(Json(new { file_path = fullPath })));
        Assert.Equal("Writing sample.txt", tool.GetActivityDescription(Json(new { file_path = fullPath })));
    }

    [Fact]
    public async Task FileEditTool_ExecuteAsync_ReplacesUniqueContentAndReportsLineCounts()
    {
        using var temp = new TempDirectory();
        var tool = new FileEditTool();
        var filePath = temp.WriteFile("edit.txt", "alpha\nbeta\ngamma\n");

        var modified = await tool.ExecuteAsync(
            Json(new
            {
                file_path = filePath,
                old_string = "beta",
                new_string = "delta",
            }),
            CreateContext(temp.Root));
        var replaced = await tool.ExecuteAsync(
            Json(new
            {
                file_path = filePath,
                old_string = "delta",
                new_string = "line-one\nline-two",
            }),
            CreateContext(temp.Root));
        var permission = await tool.CheckPermissionsAsync(
            Json(new { file_path = filePath, old_string = "alpha", new_string = "omega" }),
            CreateContext(temp.Root));

        Assert.False(modified.IsError);
        Assert.Contains("Modified 1 line(s)", modified.Data, StringComparison.Ordinal);
        Assert.False(replaced.IsError);
        Assert.Contains("Replaced 1 line(s) with 2 line(s)", replaced.Data, StringComparison.Ordinal);
        var content = await File.ReadAllTextAsync(filePath);
        Assert.Contains("line-one", content, StringComparison.Ordinal);
        Assert.Contains("line-two", content, StringComparison.Ordinal);
        Assert.Equal(PermissionBehavior.Ask, permission.Behavior);
        Assert.Contains(filePath, permission.Message, StringComparison.Ordinal);
        Assert.Equal("Editing edit.txt", tool.GetActivityDescription(Json(new { file_path = filePath })));
    }

    [Fact]
    public async Task FileEditTool_ExecuteAsync_RejectsMissingAmbiguousAndEmptyMatches()
    {
        using var temp = new TempDirectory();
        var tool = new FileEditTool();
        var filePath = temp.WriteFile("edit.txt", "repeat\nrepeat\n");

        var missingFile = await tool.ExecuteAsync(
            Json(new
            {
                file_path = temp.FullPath("missing.txt"),
                old_string = "repeat",
                new_string = "x",
            }),
            CreateContext(temp.Root));
        var ambiguous = await tool.ExecuteAsync(
            Json(new
            {
                file_path = filePath,
                old_string = "repeat",
                new_string = "x",
            }),
            CreateContext(temp.Root));
        var notFound = await tool.ExecuteAsync(
            Json(new
            {
                file_path = filePath,
                old_string = "nope",
                new_string = "x",
            }),
            CreateContext(temp.Root));
        var empty = await tool.ExecuteAsync(
            Json(new
            {
                file_path = filePath,
                old_string = "",
                new_string = "x",
            }),
            CreateContext(temp.Root));

        Assert.True(missingFile.IsError);
        Assert.Contains("File not found", missingFile.Data, StringComparison.Ordinal);
        Assert.True(ambiguous.IsError);
        Assert.Contains("found 2 times", ambiguous.Data, StringComparison.Ordinal);
        Assert.True(notFound.IsError);
        Assert.Contains("old_string not found", notFound.Data, StringComparison.Ordinal);
        Assert.True(empty.IsError);
        Assert.Contains("must not be empty", empty.Data, StringComparison.Ordinal);
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

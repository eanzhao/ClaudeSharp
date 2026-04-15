using System.Text.Json;
using Aexon.Core.Permissions;
using Aexon.Core.Tools;
using Aexon.Tools;

namespace Aexon.Core.Tests.Tools;

/// <summary>
/// Contains tests for AskUserQuestionTool.
/// </summary>
public sealed class AskUserQuestionToolTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsUserAnswerFromInteractiveCallback()
    {
        var tool = new AskUserQuestionTool();
        UserQuestionRequest? request = null;

        var result = await tool.ExecuteAsync(
            Json(new
            {
                question = "继续发布吗？",
                options = new[] { "继续", "先等等" },
            }),
            CreateContext(
                askUserQuestion: (prompt, _) =>
                {
                    request = prompt;
                    return Task.FromResult(new UserQuestionResponse("继续"));
                }));

        var permission = await tool.CheckPermissionsAsync(
            Json(new { question = "继续发布吗？" }),
            CreateContext());

        Assert.False(result.IsError);
        Assert.Equal("继续", result.Data);
        Assert.NotNull(request);
        Assert.Equal("继续发布吗？", request!.Question);
        Assert.Equal(["继续", "先等等"], request.Options);
        Assert.Equal(PermissionBehavior.Allow, permission.Behavior);
        Assert.True(tool.IsReadOnly(default));
        Assert.Equal("Waiting for user input", tool.GetActivityDescription(null));
    }

    [Fact]
    public async Task ValidateInputAsync_RejectsBlankQuestionAndDuplicateOptions()
    {
        var tool = new AskUserQuestionTool();

        var blankQuestion = await tool.ValidateInputAsync(
            Json(new { question = "   " }),
            CreateContext());
        var duplicateOptions = await tool.ValidateInputAsync(
            Json(new
            {
                question = "选哪个？",
                options = new[] { "Alpha", "alpha" },
            }),
            CreateContext());

        Assert.False(blankQuestion.IsValid);
        Assert.Contains("question is required", blankQuestion.Message, StringComparison.Ordinal);
        Assert.False(duplicateOptions.IsValid);
        Assert.Contains("options must be unique", duplicateOptions.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsErrorWhenSessionIsNonInteractive()
    {
        var tool = new AskUserQuestionTool();

        var result = await tool.ExecuteAsync(
            Json(new { question = "继续吗？" }),
            CreateContext(isNonInteractive: true));

        Assert.True(result.IsError);
        Assert.Contains("不是交互式模式", result.Data, StringComparison.Ordinal);
    }

    private static JsonElement Json(object value) =>
        JsonSerializer.SerializeToElement(value);

    private static ToolExecutionContext CreateContext(
        AskUserQuestionHandler? askUserQuestion = null,
        bool isNonInteractive = false) =>
        new()
        {
            WorkingDirectory = "/tmp",
            PermissionContext = new PermissionContext
            {
                WorkingDirectory = "/tmp",
            },
            Tools = [],
            Messages = [],
            CancellationToken = CancellationToken.None,
            AskUserQuestionAsync = askUserQuestion,
            IsNonInteractiveSession = isNonInteractive,
        };
}

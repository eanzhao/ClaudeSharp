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

    [Fact]
    public async Task ExecuteAsync_ReturnsErrorWhenHandlerNotProvided()
    {
        var tool = new AskUserQuestionTool();

        var result = await tool.ExecuteAsync(
            Json(new { question = "继续吗？" }),
            CreateContext(askUserQuestion: null, isNonInteractive: false));

        Assert.True(result.IsError);
        Assert.Contains("不是交互式模式", result.Data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsErrorWhenUserAnswerIsBlank()
    {
        var tool = new AskUserQuestionTool();

        var result = await tool.ExecuteAsync(
            Json(new { question = "Choose one" }),
            CreateContext(askUserQuestion: (_, _) => Task.FromResult(new UserQuestionResponse("   "))));

        Assert.True(result.IsError);
        Assert.Contains("用户没有提供有效回答", result.Data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_TrimsQuestionAndAnswer()
    {
        var tool = new AskUserQuestionTool();
        UserQuestionRequest? captured = null;

        var result = await tool.ExecuteAsync(
            Json(new { question = "   Pick one?   " }),
            CreateContext(askUserQuestion: (req, _) =>
            {
                captured = req;
                return Task.FromResult(new UserQuestionResponse("  yes  "));
            }));

        Assert.False(result.IsError);
        Assert.Equal("yes", result.Data);
        Assert.NotNull(captured);
        Assert.Equal("Pick one?", captured!.Question);
    }

    [Fact]
    public async Task ExecuteAsync_TrimsProvidedOptionsBeforeForwarding()
    {
        var tool = new AskUserQuestionTool();
        UserQuestionRequest? captured = null;

        var result = await tool.ExecuteAsync(
            Json(new
            {
                question = "Go?",
                options = new[] { "  Yes  ", "No" },
            }),
            CreateContext(askUserQuestion: (req, _) =>
            {
                captured = req;
                return Task.FromResult(new UserQuestionResponse("Yes"));
            }));

        Assert.False(result.IsError);
        Assert.NotNull(captured);
        Assert.Equal(["Yes", "No"], captured!.Options);
    }

    [Fact]
    public async Task ExecuteAsync_CancellationPropagatesToHandler()
    {
        var tool = new AskUserQuestionTool();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var observedToken = CancellationToken.None;

        await Assert.ThrowsAsync<TaskCanceledException>(() => tool.ExecuteAsync(
            Json(new { question = "继续？" }),
            CreateContext(askUserQuestion: (_, token) =>
            {
                observedToken = token;
                return Task.FromCanceled<UserQuestionResponse>(token);
            }),
            cancellationToken: cts.Token));

        Assert.True(observedToken.IsCancellationRequested);
    }

    [Fact]
    public async Task ValidateInputAsync_AcceptsMissingAndNonEmptyOptions()
    {
        var tool = new AskUserQuestionTool();

        var noOptions = await tool.ValidateInputAsync(
            Json(new { question = "Choose" }),
            CreateContext());
        var withOptions = await tool.ValidateInputAsync(
            Json(new { question = "Choose", options = new[] { "A", "B" } }),
            CreateContext());
        var emptyOptions = await tool.ValidateInputAsync(
            Json(new { question = "Choose", options = Array.Empty<string>() }),
            CreateContext());
        var blankOption = await tool.ValidateInputAsync(
            Json(new { question = "Choose", options = new[] { "A", "  " } }),
            CreateContext());

        Assert.True(noOptions.IsValid);
        Assert.True(withOptions.IsValid);
        Assert.False(emptyOptions.IsValid);
        Assert.Contains("at least one entry", emptyOptions.Message, StringComparison.Ordinal);
        Assert.False(blankOption.IsValid);
        Assert.Contains("blank values", blankOption.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateInputAsync_ReturnsJsonErrorForMalformedPayload()
    {
        var tool = new AskUserQuestionTool();

        var result = await tool.ValidateInputAsync(
            JsonDocument.Parse("{\"question\": 42}").RootElement,
            CreateContext());

        Assert.False(result.IsValid);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    public void GetUserFacingName_TruncatesLongQuestions()
    {
        var tool = new AskUserQuestionTool();
        var longQuestion = new string('长', 200);

        var withQuestion = tool.GetUserFacingName(Json(new { question = longQuestion }));
        var noQuestion = tool.GetUserFacingName(null);
        var shortQuestion = tool.GetUserFacingName(Json(new { question = "短问题" }));

        Assert.StartsWith("Ask user: ", withQuestion);
        Assert.EndsWith("...", withQuestion);
        Assert.Equal("Ask user a question", noQuestion);
        Assert.Equal("Ask user: 短问题", shortQuestion);
    }

    [Fact]
    public void IsReadOnly_IsTrueForAnyInput()
    {
        var tool = new AskUserQuestionTool();

        Assert.True(tool.IsReadOnly(Json(new { question = "anything" })));
        Assert.True(tool.IsReadOnly(default));
    }

    [Fact]
    public async Task GetDescriptionAsync_AndGetPromptAsync_ReturnNonEmptyContent()
    {
        var tool = new AskUserQuestionTool();

        var description = await tool.GetDescriptionAsync();
        var prompt = await tool.GetPromptAsync(new ToolPromptContext
        {
            PermissionContext = new PermissionContext(),
            Tools = [],
        });

        Assert.False(string.IsNullOrWhiteSpace(description));
        Assert.False(string.IsNullOrWhiteSpace(prompt));
        Assert.Contains("options", prompt, StringComparison.Ordinal);
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

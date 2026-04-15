using System.Text.Json;
using Aexon.Core.Permissions;
using Aexon.Core.Todos;
using Aexon.Core.Tools;
using Aexon.Tools;

namespace Aexon.Core.Tests.Tools;

/// <summary>
/// Contains tests for TodoWriteTool.
/// </summary>
public sealed class TodoWriteToolTests
{
    [Fact]
    public async Task ExecuteAsync_ManagesSessionTodos()
    {
        var runtime = new InMemoryTodoRuntime();
        var tool = new TodoWriteTool(runtime);
        var context = CreateContext();

        var invalid = await tool.ValidateInputAsync(
            Json(new
            {
                operation = "create",
                todos = new[]
                {
                    new
                    {
                        id = "plan",
                    },
                },
            }),
            context);
        var created = await tool.ExecuteAsync(
            Json(new
            {
                operation = "create",
                todos = new[]
                {
                    new
                    {
                        id = "plan",
                        title = "Plan work",
                        description = "Break the task into steps",
                    },
                },
            }),
            context);
        var updated = await tool.ExecuteAsync(
            Json(new
            {
                operation = "update",
                todos = new[]
                {
                    new
                    {
                        id = "plan",
                        status = "in_progress",
                        description = "Implementing now",
                    },
                },
            }),
            context);
        var listed = await tool.ExecuteAsync(
            Json(new
            {
                operation = "list",
                todos = Array.Empty<object>(),
            }),
            context);
        var deleted = await tool.ExecuteAsync(
            Json(new
            {
                operation = "delete",
                todos = new[]
                {
                    new
                    {
                        id = "plan",
                    },
                },
            }),
            context);

        Assert.False(invalid.IsValid);
        Assert.Equal("todos[0].title is required for create.", invalid.Message);
        Assert.False(created.IsError);
        Assert.Contains("Created 1 todo(s)", created.Data, StringComparison.Ordinal);
        Assert.Contains("plan [pending] Plan work", created.Data, StringComparison.Ordinal);
        Assert.False(updated.IsError);
        Assert.Contains("Updated 1 todo(s)", updated.Data, StringComparison.Ordinal);
        Assert.Contains("plan [in_progress] Plan work", updated.Data, StringComparison.Ordinal);
        Assert.False(listed.IsError);
        Assert.Contains("Current todos", listed.Data, StringComparison.Ordinal);
        Assert.Contains("Implementing now", listed.Data, StringComparison.Ordinal);
        Assert.False(deleted.IsError);
        Assert.Contains("Deleted 1 todo(s)", deleted.Data, StringComparison.Ordinal);
        Assert.Contains("(none)", deleted.Data, StringComparison.Ordinal);
        Assert.True(tool.IsReadOnly(Json(new { operation = "list", todos = Array.Empty<object>() })));
        Assert.True(tool.IsConcurrencySafe(Json(new { operation = "list", todos = Array.Empty<object>() })));
        Assert.False(tool.IsReadOnly(Json(new { operation = "create" })));
        Assert.Equal("Creating todos", tool.GetActivityDescription(Json(new { operation = "create" })));
        Assert.Equal("Reading todos", tool.GetActivityDescription(Json(new { operation = "list" })));
    }

    [Fact]
    public async Task GetPromptAsync_IncludesCurrentTodoList()
    {
        var runtime = new InMemoryTodoRuntime();
        runtime.CreateTodo("issue-4", "Implement TodoWrite", TodoStatus.InProgress, "Wire runtime");
        var tool = new TodoWriteTool(runtime);

        var prompt = await tool.GetPromptAsync(new ToolPromptContext
        {
            PermissionContext = new PermissionContext(),
            Tools = [],
        });

        Assert.Contains("Current todo list:", prompt, StringComparison.Ordinal);
        Assert.Contains("issue-4 [in_progress] Implement TodoWrite", prompt, StringComparison.Ordinal);
        Assert.Contains("Wire runtime", prompt, StringComparison.Ordinal);
    }

    private static JsonElement Json(object value) =>
        JsonSerializer.SerializeToElement(value);

    private static ToolExecutionContext CreateContext() =>
        new()
        {
            WorkingDirectory = "/tmp",
            PermissionContext = new PermissionContext(),
            Tools = [],
            Messages = [],
            CancellationToken = CancellationToken.None,
        };
}

using System.Text.Json;
using Aexon.Core.Cron;
using Aexon.Core.Permissions;
using Aexon.Core.Tools;
using Aexon.Tools;

namespace Aexon.Core.Tests.Tools;

public sealed class CronToolsTests
{
    [Fact]
    public async Task CronCreateTool_CreatesJob()
    {
        var runtime = new InMemoryCronRuntime();
        var tool = new CronCreateTool(runtime);
        var context = CreateContext();

        var result = await tool.ExecuteAsync(
            Json(new
            {
                id = "backup",
                schedule = "0 2 * * *",
                command = "tar czf /tmp/backup.tar.gz /data",
                description = "Nightly backup",
            }),
            context);

        Assert.False(result.IsError);
        Assert.Contains("Created cron job", result.Data, StringComparison.Ordinal);
        Assert.Contains("backup", result.Data, StringComparison.Ordinal);
        Assert.Contains("0 2 * * *", result.Data, StringComparison.Ordinal);
        Assert.Single(runtime.ListJobs());
    }

    [Fact]
    public async Task CronCreateTool_InvalidSchedule_ReturnsValidationError()
    {
        var runtime = new InMemoryCronRuntime();
        var tool = new CronCreateTool(runtime);
        var context = CreateContext();

        var validation = await tool.ValidateInputAsync(
            Json(new
            {
                id = "bad",
                schedule = "invalid cron",
                command = "echo hello",
            }),
            context);

        Assert.False(validation.IsValid);
        Assert.Contains("Invalid cron expression", validation.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CronCreateTool_DuplicateId_ReturnsValidationError()
    {
        var runtime = new InMemoryCronRuntime();
        runtime.CreateJob("existing", "0 * * * *", "echo test");
        var tool = new CronCreateTool(runtime);
        var context = CreateContext();

        var validation = await tool.ValidateInputAsync(
            Json(new
            {
                id = "existing",
                schedule = "0 * * * *",
                command = "echo hello",
            }),
            context);

        Assert.False(validation.IsValid);
        Assert.Contains("already exists", validation.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CronCreateTool_MissingFields_ReturnsValidationError()
    {
        var runtime = new InMemoryCronRuntime();
        var tool = new CronCreateTool(runtime);
        var context = CreateContext();

        var noId = await tool.ValidateInputAsync(
            Json(new { schedule = "0 * * * *", command = "echo" }),
            context);
        var noSchedule = await tool.ValidateInputAsync(
            Json(new { id = "test", command = "echo" }),
            context);
        var noCommand = await tool.ValidateInputAsync(
            Json(new { id = "test", schedule = "0 * * * *" }),
            context);

        Assert.False(noId.IsValid);
        Assert.False(noSchedule.IsValid);
        Assert.False(noCommand.IsValid);
    }

    [Fact]
    public async Task CronDeleteTool_DeletesJob()
    {
        var runtime = new InMemoryCronRuntime();
        runtime.CreateJob("cleanup", "0 * * * *", "rm /tmp/*.log");
        var tool = new CronDeleteTool(runtime);
        var context = CreateContext();

        var result = await tool.ExecuteAsync(
            Json(new { id = "cleanup" }),
            context);

        Assert.False(result.IsError);
        Assert.Contains("Deleted", result.Data, StringComparison.Ordinal);
        Assert.Empty(runtime.ListJobs());
    }

    [Fact]
    public async Task CronDeleteTool_NotFound_ReturnsValidationError()
    {
        var runtime = new InMemoryCronRuntime();
        var tool = new CronDeleteTool(runtime);
        var context = CreateContext();

        var validation = await tool.ValidateInputAsync(
            Json(new { id = "nonexistent" }),
            context);

        Assert.False(validation.IsValid);
        Assert.Contains("not found", validation.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CronListTool_ListsJobs()
    {
        var runtime = new InMemoryCronRuntime();
        runtime.CreateJob("backup", "0 2 * * *", "tar czf /tmp/backup.tar.gz /data", "Nightly backup");
        runtime.CreateJob("cleanup", "0 * * * *", "rm /tmp/*.log");
        var tool = new CronListTool(runtime);
        var context = CreateContext();

        var result = await tool.ExecuteAsync(
            Json(new { }),
            context);

        Assert.False(result.IsError);
        Assert.Contains("backup", result.Data, StringComparison.Ordinal);
        Assert.Contains("cleanup", result.Data, StringComparison.Ordinal);
        Assert.Contains("0 2 * * *", result.Data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CronListTool_EmptyList_ShowsNone()
    {
        var runtime = new InMemoryCronRuntime();
        var tool = new CronListTool(runtime);
        var context = CreateContext();

        var result = await tool.ExecuteAsync(
            Json(new { }),
            context);

        Assert.False(result.IsError);
        Assert.Contains("no cron jobs", result.Data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CronListTool_WithHistory_IncludesExecutionRecords()
    {
        var runtime = new InMemoryCronRuntime();
        runtime.CreateJob("test", "*/5 * * * *", "echo hello");
        runtime.RecordExecution(new CronExecutionRecord
        {
            Id = "exec-1",
            JobId = "test",
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Success = true,
            Output = "hello",
        });
        var tool = new CronListTool(runtime);
        var context = CreateContext();

        var result = await tool.ExecuteAsync(
            Json(new { include_history = true }),
            context);

        Assert.False(result.IsError);
        Assert.Contains("Execution history", result.Data, StringComparison.Ordinal);
        Assert.Contains("exec-1", result.Data, StringComparison.Ordinal);
        Assert.Contains("success", result.Data, StringComparison.Ordinal);
    }

    [Fact]
    public void CronListTool_IsReadOnly()
    {
        var runtime = new InMemoryCronRuntime();
        var tool = new CronListTool(runtime);

        Assert.True(tool.IsReadOnly(Json(new { })));
        Assert.True(tool.IsConcurrencySafe(Json(new { })));
    }

    [Fact]
    public void CronCreateTool_ActivityDescription()
    {
        var runtime = new InMemoryCronRuntime();
        var tool = new CronCreateTool(runtime);

        Assert.Equal("Creating cron job", tool.GetActivityDescription(null));
        Assert.Equal("Cron create", tool.GetUserFacingName());
    }

    [Fact]
    public void CronDeleteTool_ActivityDescription()
    {
        var runtime = new InMemoryCronRuntime();
        var tool = new CronDeleteTool(runtime);

        Assert.Equal("Deleting cron job", tool.GetActivityDescription(null));
        Assert.Equal("Cron delete", tool.GetUserFacingName());
    }

    [Fact]
    public void CronListTool_ActivityDescription()
    {
        var runtime = new InMemoryCronRuntime();
        var tool = new CronListTool(runtime);

        Assert.Equal("Listing cron jobs", tool.GetActivityDescription(null));
        Assert.Equal("Cron list", tool.GetUserFacingName());
    }

    [Fact]
    public async Task CronCreateTool_GetPromptAsync_IncludesCronDocumentation()
    {
        var runtime = new InMemoryCronRuntime();
        var tool = new CronCreateTool(runtime);

        var prompt = await tool.GetPromptAsync(new ToolPromptContext
        {
            PermissionContext = new PermissionContext(),
            Tools = [],
        });

        Assert.Contains("minute hour day-of-month month day-of-week", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CronListTool_GetPromptAsync_IncludesCurrentJobs()
    {
        var runtime = new InMemoryCronRuntime();
        runtime.CreateJob("daily", "0 0 * * *", "echo daily");
        var tool = new CronListTool(runtime);

        var prompt = await tool.GetPromptAsync(new ToolPromptContext
        {
            PermissionContext = new PermissionContext(),
            Tools = [],
        });

        Assert.Contains("daily", prompt, StringComparison.Ordinal);
        Assert.Contains("0 0 * * *", prompt, StringComparison.Ordinal);
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

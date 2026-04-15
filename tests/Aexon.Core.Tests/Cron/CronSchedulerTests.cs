using System.Reflection;
using Aexon.Core.Cron;
using Aexon.Core.Tests.Runtime;

namespace Aexon.Core.Tests.Cron;

public sealed class CronSchedulerTests
{
    [Fact]
    public async Task RunCommandAsync_ExecutesFullShellCommand()
    {
        using var temp = new TempDirectory();

        var (exitCode, output) = await InvokeRunCommandAsync(
            "echo hello scheduler",
            temp.Root,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Contains("hello scheduler", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TickAsync_RunsOnlyDueJobs()
    {
        using var temp = new TempDirectory();
        var now = DateTimeOffset.UtcNow;
        var runtime = new InMemoryCronRuntime(
            [
                new CronJob
                {
                    Id = "due",
                    Schedule = "* * * * *",
                    Command = "echo due",
                    CreatedAt = now.AddMinutes(-5),
                    UpdatedAt = now.AddMinutes(-5),
                    NextRunAt = now.AddMinutes(-1),
                },
                new CronJob
                {
                    Id = "future",
                    Schedule = "* * * * *",
                    Command = "echo future",
                    CreatedAt = now.AddMinutes(-5),
                    UpdatedAt = now.AddMinutes(-5),
                    NextRunAt = now.AddMinutes(10),
                },
            ]);

        await using var scheduler = new CronScheduler(runtime, temp.Root);
        await InvokeInstanceTaskAsync(scheduler, "TickAsync", CancellationToken.None);

        var record = Assert.Single(runtime.ListHistory());
        Assert.Equal("due", record.JobId);
        Assert.True(record.Success);
        Assert.Contains("due", record.Output, StringComparison.Ordinal);
        Assert.NotNull(runtime.GetJob("due")!.LastRunAt);
        Assert.Null(runtime.GetJob("future")!.LastRunAt);
    }

    [Fact]
    public async Task ExecuteJobAsync_WhenCommandStartupFails_RecordsFailure()
    {
        var runtime = new InMemoryCronRuntime();
        var job = runtime.CreateJob("broken", "* * * * *", "echo broken");

        await using var scheduler = new CronScheduler(runtime, "/path/that/does/not/exist");
        await InvokeInstanceTaskAsync(scheduler, "ExecuteJobAsync", job, CancellationToken.None);

        var record = Assert.Single(runtime.ListHistory("broken"));
        Assert.False(record.Success);
        Assert.NotNull(record.CompletedAt);
        Assert.False(string.IsNullOrWhiteSpace(record.Output));
    }

    private static async Task<(int ExitCode, string Output)> InvokeRunCommandAsync(
        string command,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var method = typeof(CronScheduler).GetMethod(
            "RunCommandAsync",
            BindingFlags.NonPublic | BindingFlags.Static);
        var task = Assert.IsAssignableFrom<Task>(method!.Invoke(null, [command, workingDirectory, cancellationToken])!);

        await task;

        return ((int ExitCode, string Output))task.GetType()
            .GetProperty("Result")!
            .GetValue(task)!;
    }

    private static async Task InvokeInstanceTaskAsync(
        CronScheduler scheduler,
        string methodName,
        params object[] arguments)
    {
        var method = typeof(CronScheduler).GetMethod(
            methodName,
            BindingFlags.NonPublic | BindingFlags.Instance);
        var task = Assert.IsAssignableFrom<Task>(method!.Invoke(scheduler, arguments)!);
        await task;
    }
}

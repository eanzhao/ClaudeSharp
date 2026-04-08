using ClaudeSharp.Core.Agents;

namespace ClaudeSharp.Core.Tests.Agents;

/// <summary>
/// Contains tests for background-run waiting helpers.
/// </summary>
public sealed class AgentBackgroundRunWaiterTests
{
    [Fact]
    public async Task WaitManyAsync_NormalizesIdsAndCompletesWhenAnyRunFinishes()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var completed = runtime.StartBackgroundRun(
            "completed",
            initialStatus: AgentBackgroundRunStatus.Stopped);
        var pending = runtime.StartBackgroundRun(
            "pending",
            initialStatus: AgentBackgroundRunStatus.Running);

        var result = await AgentBackgroundRunWaiter.WaitManyAsync(
            runtime,
            ["  " + completed.Id + "  ", completed.Id, "", pending.Id],
            AgentBackgroundRunWaitMode.Any,
            TimeSpan.Zero);

        Assert.Equal(AgentBackgroundRunWaitOutcome.Completed, result.Outcome);
        Assert.Equal(AgentBackgroundRunWaitMode.Any, result.Mode);
        Assert.Single(result.CompletedRuns);
        Assert.Equal(completed.Id, result.CompletedRuns[0].BackgroundRunId);
        Assert.Single(result.PendingRuns);
        Assert.Equal(pending.Id, result.PendingRuns[0].BackgroundRunId);
        Assert.Empty(result.MissingRunIds);
    }

    [Fact]
    public async Task WaitManyAsync_CanPollUntilAllRunsComplete()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var run = runtime.StartBackgroundRun(
            "running",
            initialStatus: AgentBackgroundRunStatus.Running);
        var delayCalls = 0;

        var result = await AgentBackgroundRunWaiter.WaitManyAsync(
            runtime,
            [run.Id],
            AgentBackgroundRunWaitMode.All,
            TimeSpan.FromMilliseconds(1),
            timeout: TimeSpan.FromMilliseconds(100),
            delayAsync: (_, _) =>
            {
                delayCalls++;
                runtime.StopBackgroundRun(run.Id, "done");
                return Task.CompletedTask;
            });

        Assert.Equal(AgentBackgroundRunWaitOutcome.Completed, result.Outcome);
        Assert.Single(result.CompletedRuns);
        Assert.Empty(result.PendingRuns);
        Assert.True(delayCalls >= 1);
    }

    [Fact]
    public async Task WaitManyAsync_TimesOutWhenRunsStayPending()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var run = runtime.StartBackgroundRun(
            "running",
            initialStatus: AgentBackgroundRunStatus.Running);

        var result = await AgentBackgroundRunWaiter.WaitManyAsync(
            runtime,
            [run.Id],
            AgentBackgroundRunWaitMode.All,
            TimeSpan.FromMilliseconds(1),
            timeout: TimeSpan.FromMilliseconds(5),
            delayAsync: (_, _) => Task.CompletedTask);

        Assert.Equal(AgentBackgroundRunWaitOutcome.TimedOut, result.Outcome);
        Assert.Empty(result.CompletedRuns);
        Assert.Single(result.PendingRuns);
        Assert.Equal(run.Id, result.PendingRuns[0].BackgroundRunId);
    }

    [Fact]
    public async Task WaitManyAsync_RejectsEmptyIdentifierSets()
    {
        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            AgentBackgroundRunWaiter.WaitManyAsync(
                new InMemoryAgentTaskRuntime(),
                [" ", ""],
                AgentBackgroundRunWaitMode.All,
                TimeSpan.FromMilliseconds(1)));

        Assert.Equal("backgroundRunIds", error.ParamName);
    }

    [Fact]
    public async Task WaitAsync_ReturnsNotFoundWhenRunIsMissing()
    {
        var result = await AgentBackgroundRunWaiter.WaitAsync(
            new InMemoryAgentTaskRuntime(),
            " missing-run ",
            TimeSpan.FromMilliseconds(1),
            timeout: TimeSpan.FromMilliseconds(5),
            delayAsync: (_, _) => Task.CompletedTask);

        Assert.Equal(AgentBackgroundRunWaitOutcome.NotFound, result.Outcome);
        Assert.Equal("missing-run", result.BackgroundRunId);
        Assert.Null(result.Run);
    }

    [Theory]
    [InlineData(AgentBackgroundRunStatus.Queued, false)]
    [InlineData(AgentBackgroundRunStatus.Running, false)]
    [InlineData(AgentBackgroundRunStatus.CancellationRequested, false)]
    [InlineData(AgentBackgroundRunStatus.Stopped, true)]
    [InlineData(AgentBackgroundRunStatus.Failed, true)]
    [InlineData(AgentBackgroundRunStatus.Cancelled, true)]
    public void IsTerminal_RecognizesTerminalStatuses(
        AgentBackgroundRunStatus status,
        bool expected)
    {
        Assert.Equal(expected, AgentBackgroundRunWaiter.IsTerminal(status));
    }

    [Fact]
    public async Task WaitManyAsync_ReturnsNotFoundWhenAnyRequestedRunIsMissing()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var run = runtime.StartBackgroundRun(
            "running",
            initialStatus: AgentBackgroundRunStatus.Running);

        var result = await AgentBackgroundRunWaiter.WaitManyAsync(
            runtime,
            [run.Id, "missing-run"],
            AgentBackgroundRunWaitMode.All,
            TimeSpan.FromMilliseconds(5),
            timeout: TimeSpan.FromMilliseconds(50),
            delayAsync: (_, _) => Task.CompletedTask);

        Assert.Equal(AgentBackgroundRunWaitOutcome.NotFound, result.Outcome);
        Assert.Single(result.PendingRuns);
        Assert.Equal(run.Id, result.PendingRuns[0].BackgroundRunId);
        Assert.Equal(["missing-run"], result.MissingRunIds);
    }
}

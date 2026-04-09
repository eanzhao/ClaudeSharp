using ClaudeSharp.Core.Agents;

namespace ClaudeSharp.Core.Tests.Agents;

/// <summary>
/// Contains tests for mailbox-driven agent activation runtime.
/// </summary>
public sealed class AgentMessageActivationRuntimeTests
{
    [Fact]
    public async Task TryActivateAsync_ReturnsNotRegisteredForUnknownOwner()
    {
        var runtime = new InMemoryAgentMessageActivationRuntime();

        var result = await runtime.TryActivateAsync("Platform/Ada");

        Assert.Equal(AgentMessageActivationStatus.NotRegistered, result.Status);
        Assert.Equal("Platform/Ada", result.Owner);
    }

    [Fact]
    public async Task TryActivateAsync_CoalescesConcurrentActivationRequests()
    {
        var runtime = new InMemoryAgentMessageActivationRuntime();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        runtime.RegisterOwner(
            "Platform/Ada",
            async (_, cancellationToken) =>
            {
                Interlocked.Increment(ref calls);
                await release.Task.WaitAsync(cancellationToken);
                return AgentMessageActivationResult.Reactivated(
                    "Platform/Ada",
                    "background-run-7",
                    "work-item-9");
            });

        var first = runtime.TryActivateAsync("Platform/Ada", "message one");
        var second = runtime.TryActivateAsync("Platform/Ada", "message two");
        release.TrySetResult();

        var results = await Task.WhenAll(first, second);

        Assert.Equal(1, calls);
        Assert.All(results, result =>
        {
            Assert.Equal(AgentMessageActivationStatus.Reactivated, result.Status);
            Assert.Equal("background-run-7", result.BackgroundRunId);
        });
    }
}

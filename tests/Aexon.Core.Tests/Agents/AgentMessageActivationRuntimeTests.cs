using Aexon.Core.Agents;

namespace Aexon.Core.Tests.Agents;

/// <summary>
/// Contains tests for mailbox-driven agent activation runtime.
/// </summary>
public sealed class AgentMessageActivationRuntimeTests
{
    [Fact]
    public async Task TryActivateAsync_ReturnsNotRegisteredForUnknownOwner()
    {
        var runtime = new InMemoryAgentMessageActivationRuntime();
        var message = CreateMessage("Platform/Ada");

        var result = await runtime.TryActivateAsync(message);

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
            async (request, cancellationToken) =>
            {
                Interlocked.Increment(ref calls);
                await release.Task.WaitAsync(cancellationToken);
                return AgentMessageActivationResult.Reactivated(
                    "Platform/Ada",
                    "background-run-7",
                    "work-item-9",
                    request.ResumeReason);
            });

        var first = runtime.TryActivateAsync(CreateMessage("Platform/Ada", "Need follow-up"));
        var second = runtime.TryActivateAsync(CreateMessage("Platform/Ada", "Need follow-up"));
        release.TrySetResult();

        var results = await Task.WhenAll(first, second);

        Assert.Equal(1, calls);
        Assert.All(results, result =>
        {
            Assert.Equal(AgentMessageActivationStatus.Reactivated, result.Status);
            Assert.Equal("background-run-7", result.BackgroundRunId);
            Assert.Equal("Need follow-up", result.Message);
        });
    }

    [Fact]
    public async Task TryActivateAsync_DoesNotReuseCompletedAlreadyActiveResult()
    {
        var runtime = new InMemoryAgentMessageActivationRuntime();
        var calls = 0;

        runtime.RegisterOwner(
            "Platform/Ada",
            (request, _) =>
            {
                calls++;
                return Task.FromResult(
                    calls == 1
                        ? AgentMessageActivationResult.AlreadyActive(
                            "Platform/Ada",
                            $"Trigger {request.Message.Id} is waiting.")
                        : AgentMessageActivationResult.Reactivated(
                            "Platform/Ada",
                            "background-run-8",
                            "work-item-2",
                            "Triggered later."));
            });

        var first = await runtime.TryActivateAsync(CreateMessage("Platform/Ada"));
        var second = await runtime.TryActivateAsync(CreateMessage("Platform/Ada"));

        Assert.Equal(2, calls);
        Assert.Equal(AgentMessageActivationStatus.AlreadyActive, first.Status);
        Assert.Equal(AgentMessageActivationStatus.Reactivated, second.Status);
        Assert.Equal("background-run-8", second.BackgroundRunId);
    }

    private static AgentMessage CreateMessage(string recipient, string? resumeReason = null) =>
        new()
        {
            Id = "agent-message-1",
            ThreadId = "thread-1",
            From = "main",
            To = recipient,
            Kind = AgentMessageKind.Note,
            Body = "Please resume work",
            Protocol = string.IsNullOrWhiteSpace(resumeReason)
                ? null
                : new AgentMessageProtocol
                {
                    ResumeReason = resumeReason,
                },
        };
}

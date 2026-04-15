using System.Runtime.CompilerServices;
using Aexon.Core.Query;
using Microsoft.Extensions.AI;

namespace Aexon.Core.Tests.Runtime;

/// <summary>
/// Contains tests for chat retry middleware.
/// </summary>
public sealed class RetryingChatClientTests
{
    [Fact]
    public async Task GetResponseAsync_RetriesTransientFailures()
    {
        var inner = new ScriptedRetryChatClient(
            batchSteps:
            [
                new BatchStep(Exception: new HttpRequestException("network boom")),
                new BatchStep(ResponseText: "ok"),
            ]);
        using var retrying = new RetryingChatClient(inner, maxAttempts: 3, retryDelay: TimeSpan.Zero);

        var response = await retrying.GetResponseAsync([new ChatMessage(ChatRole.User, "ping")]);

        Assert.Equal("ok", response.Text);
        Assert.Equal(2, inner.BatchCallCount);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_RetriesWhenFailureHappensBeforeFirstUpdate()
    {
        var inner = new ScriptedRetryChatClient(
            streamSteps:
            [
                new StreamStep(Exception: new HttpRequestException("network boom")),
                new StreamStep(Updates:
                [
                    new ChatResponseUpdate
                    {
                        ResponseId = "resp-1",
                        Role = ChatRole.Assistant,
                        Contents = [new TextContent("hello")],
                    },
                ]),
            ]);
        using var retrying = new RetryingChatClient(inner, maxAttempts: 3, retryDelay: TimeSpan.Zero);

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in retrying.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "ping")]))
            updates.Add(update);

        Assert.Single(updates);
        Assert.Equal("hello", updates[0].Text);
        Assert.Equal(2, inner.StreamCallCount);
    }

    private sealed record BatchStep(string? ResponseText = null, Exception? Exception = null);

    private sealed record StreamStep(
        IReadOnlyList<ChatResponseUpdate>? Updates = null,
        Exception? Exception = null);

    private sealed class ScriptedRetryChatClient(
        IReadOnlyList<BatchStep>? batchSteps = null,
        IReadOnlyList<StreamStep>? streamSteps = null) : IChatClient
    {
        private readonly Queue<BatchStep> _batchSteps = new(batchSteps ?? []);
        private readonly Queue<StreamStep> _streamSteps = new(streamSteps ?? []);

        public ChatClientMetadata Metadata => new("retry-test");

        public int BatchCallCount { get; private set; }

        public int StreamCallCount { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            BatchCallCount++;
            var step = _batchSteps.Dequeue();
            if (step.Exception != null)
                return Task.FromException<ChatResponse>(step.Exception);

            return Task.FromResult(new ChatResponse(
                new ChatMessage(ChatRole.Assistant, step.ResponseText ?? string.Empty)));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            StreamCallCount++;
            var step = _streamSteps.Dequeue();
            if (step.Exception != null)
                throw step.Exception;

            foreach (var update in step.Updates ?? [])
            {
                await Task.Yield();
                yield return update;
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}

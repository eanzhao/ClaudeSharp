using Aexon.Core.Query;
using Microsoft.Extensions.AI;

namespace Aexon.Core.Tests.Runtime;

/// <summary>
/// Contains tests for structured output helpers.
/// </summary>
public sealed class StructuredOutputClientTests
{
    [Fact]
    public async Task GetResponseAsync_UsesJsonSchemaResponseFormatAndParsesTypedResult()
    {
        var inner = new StructuredOutputFakeClient(
            """{"verdict":"pass","score":9}""");
        var client = new StructuredOutputClient(inner);

        var result = await client.GetResponseAsync<ReviewSummary>(
            [new ChatMessage(ChatRole.User, "review this")]);

        Assert.Equal("pass", result.Result.Verdict);
        Assert.Equal(9, result.Result.Score);
        Assert.IsType<ChatResponseFormatJson>(inner.LastOptions?.ResponseFormat);
    }

    [Fact]
    public async Task GetResponseAsync_RetriesWhenModelReturnsInvalidJson()
    {
        var inner = new StructuredOutputFakeClient(
            "not json",
            """{"verdict":"retry-ok","score":7}""");
        var client = new StructuredOutputClient(inner);

        var result = await client.GetResponseAsync<ReviewSummary>(
            [new ChatMessage(ChatRole.User, "review this")],
            maxFormatAttempts: 2);

        Assert.Equal("retry-ok", result.Result.Verdict);
        Assert.Equal(2, inner.CallCount);
    }

    private sealed class StructuredOutputFakeClient(params string[] responses) : IChatClient
    {
        private readonly Queue<string> _responses = new(responses);

        public ChatClientMetadata Metadata => new("structured-test");

        public int CallCount { get; private set; }

        public ChatOptions? LastOptions { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastOptions = options?.Clone();
            var payload = _responses.Dequeue();
            return Task.FromResult(new ChatResponse(
                new ChatMessage(ChatRole.Assistant, payload)));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class ReviewSummary
    {
        public string Verdict { get; set; } = string.Empty;

        public int Score { get; set; }
    }
}

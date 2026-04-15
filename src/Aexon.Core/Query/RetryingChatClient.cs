using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Aexon.Core.Query;

/// <summary>
/// Retries transient chat failures before surfacing them to the caller.
/// </summary>
public sealed class RetryingChatClient : DelegatingChatClient
{
    private readonly int _maxAttempts;
    private readonly TimeSpan _retryDelay;
    private readonly ILogger? _logger;

    public RetryingChatClient(
        IChatClient innerClient,
        int maxAttempts = 3,
        TimeSpan? retryDelay = null,
        ILogger? logger = null)
        : base(innerClient)
    {
        _maxAttempts = Math.Max(1, maxAttempts);
        _retryDelay = retryDelay ?? TimeSpan.FromMilliseconds(250);
        _logger = logger;
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var requestMessages = CloneMessages(messages);
        var requestOptions = options?.Clone();

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await base.GetResponseAsync(
                    CloneMessages(requestMessages),
                    requestOptions?.Clone(),
                    cancellationToken);
            }
            catch (Exception ex) when (ShouldRetry(ex, attempt, cancellationToken))
            {
                await DelayBeforeRetryAsync(ex, attempt, cancellationToken);
            }
        }
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var requestMessages = CloneMessages(messages);
        var requestOptions = options?.Clone();

        for (var attempt = 1; ; attempt++)
        {
            var yieldedAnyUpdate = false;
            var shouldRetry = false;
            await using var enumerator = base.GetStreamingResponseAsync(
                CloneMessages(requestMessages),
                requestOptions?.Clone(),
                cancellationToken).GetAsyncEnumerator(cancellationToken);

            while (true)
            {
                ChatResponseUpdate update;

                try
                {
                    if (!await enumerator.MoveNextAsync())
                    {
                        yield break;
                    }

                    update = enumerator.Current;
                }
                catch (Exception ex) when (!yieldedAnyUpdate && ShouldRetry(ex, attempt, cancellationToken))
                {
                    shouldRetry = true;
                    await DelayBeforeRetryAsync(ex, attempt, cancellationToken);
                    break;
                }

                yieldedAnyUpdate = true;
                yield return update;
            }

            if (!shouldRetry)
                yield break;
        }
    }

    private bool ShouldRetry(
        Exception exception,
        int attempt,
        CancellationToken cancellationToken)
    {
        if (attempt >= _maxAttempts || cancellationToken.IsCancellationRequested)
            return false;

        return exception switch
        {
            HttpRequestException => true,
            IOException => true,
            TaskCanceledException when !cancellationToken.IsCancellationRequested => true,
            TimeoutException => true,
            _ => false,
        };
    }

    private async Task DelayBeforeRetryAsync(
        Exception exception,
        int attempt,
        CancellationToken cancellationToken)
    {
        _logger?.LogWarning(
            exception,
            "Retrying chat request after transient failure (attempt {Attempt}/{MaxAttempts}).",
            attempt,
            _maxAttempts);

        await Task.Delay(_retryDelay, cancellationToken);
    }

    private static ChatMessage[] CloneMessages(IEnumerable<ChatMessage> messages) =>
        messages.Select(message => message.Clone()).ToArray();
}

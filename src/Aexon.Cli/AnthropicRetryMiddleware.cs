using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Anthropic.Core;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using Aexon.Core.Query;
using Microsoft.Extensions.AI;

namespace Aexon.Cli;

/// <summary>
/// Adds Anthropic-specific retry, timeout, quota tracking, and stream recovery behavior.
/// </summary>
internal sealed class AnthropicRetryMiddleware(
    IChatClient inner,
    ApiResponseObserver responseObserver,
    Func<TimeSpan, CancellationToken, Task>? delayAsync = null) : DelegatingChatClient(inner)
{
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync =
        delayAsync ?? ((delay, cancellationToken) => Task.Delay(delay, cancellationToken));

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var materializedMessages = messages as IReadOnlyList<ChatMessage> ?? messages.ToList();
        var retryOptions = RetrySettings.FromChatOptions(options);
        var retryCount = 0;

        while (true)
        {
            using var requestScope = responseObserver.BeginRequest();
            using var attemptCts = CreateAttemptCancellationSource(retryOptions.RequestTimeout, cancellationToken);

            ApiQuotaSnapshot? snapshot = null;
            ApiQuotaStatus? quotaStatus = null;

            try
            {
                var response = await base.GetResponseAsync(materializedMessages, options, attemptCts.Token);
                snapshot = TakeSnapshot(requestScope);
                quotaStatus = ApiQuotaStatus.FromSnapshot(snapshot);
                AttachQuotaStatus(response, quotaStatus);
                return response;
            }
            catch (Exception ex)
            {
                snapshot ??= TakeSnapshot(requestScope);
                quotaStatus ??= ApiQuotaStatus.FromSnapshot(snapshot);

                if (!TryPlanRetry(
                        ex,
                        snapshot,
                        retryOptions,
                        retryCount,
                        cancellationToken,
                        attemptCts,
                        out var delay))
                {
                    AttachQuotaStatus(ex, quotaStatus);
                    throw;
                }

                retryCount++;
                await _delayAsync(delay, cancellationToken);
            }
        }
    }

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var materializedMessages = messages as IReadOnlyList<ChatMessage> ?? messages.ToList();
        var retryOptions = RetrySettings.FromChatOptions(options);
        var channel = Channel.CreateUnbounded<ChatResponseUpdate>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
        });

        _ = Task.Run(async () =>
        {
            try
            {
                await StreamWithRetryAsync(
                    materializedMessages,
                    options,
                    retryOptions,
                    channel.Writer,
                    cancellationToken);
                channel.Writer.TryComplete();
            }
            catch (Exception ex)
            {
                channel.Writer.TryComplete(ex);
            }
        }, CancellationToken.None);

        return channel.Reader.ReadAllAsync(cancellationToken);
    }

    private async Task StreamWithRetryAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options,
        RetrySettings retryOptions,
        ChannelWriter<ChatResponseUpdate> writer,
        CancellationToken cancellationToken)
    {
        var retryCount = 0;
        var replayState = new StreamingReplayState();

        while (true)
        {
            using var requestScope = responseObserver.BeginRequest();
            using var attemptCts = CreateAttemptCancellationSource(retryOptions.RequestTimeout, cancellationToken);

            ApiQuotaSnapshot? snapshot = null;
            ApiQuotaStatus? quotaStatus = null;
            var sawMessageStop = false;

            try
            {
                await foreach (var update in base.GetStreamingResponseAsync(messages, options, attemptCts.Token))
                {
                    snapshot ??= TakeSnapshot(requestScope);
                    quotaStatus ??= ApiQuotaStatus.FromSnapshot(snapshot);

                    if (TryGetRawEvent(update, out var rawEvent))
                    {
                        foreach (var transformed in ProcessStreamingEvent(rawEvent, replayState, quotaStatus))
                            await writer.WriteAsync(transformed, cancellationToken);

                        if (rawEvent.TryPickStop(out _))
                            sawMessageStop = true;

                        continue;
                    }

                    var forwarded = AttachQuotaStatus(update, quotaStatus);
                    if (forwarded.FinishReason != null)
                        sawMessageStop = true;

                    await writer.WriteAsync(forwarded, cancellationToken);
                }

                snapshot ??= TakeSnapshot(requestScope);
                quotaStatus ??= ApiQuotaStatus.FromSnapshot(snapshot);

                if (!sawMessageStop)
                    throw new StreamingInterruptedException("流式响应在收到 message_stop 之前结束了。");

                return;
            }
            catch (Exception ex)
            {
                snapshot ??= TakeSnapshot(requestScope);
                quotaStatus ??= ApiQuotaStatus.FromSnapshot(snapshot);

                if (!TryPlanRetry(
                        ex,
                        snapshot,
                        retryOptions,
                        retryCount,
                        cancellationToken,
                        attemptCts,
                        out var delay))
                {
                    AttachQuotaStatus(ex, quotaStatus);
                    throw;
                }

                retryCount++;
                await _delayAsync(delay, cancellationToken);
            }
        }
    }

    private static IReadOnlyList<ChatResponseUpdate> ProcessStreamingEvent(
        RawMessageStreamEvent rawEvent,
        StreamingReplayState replayState,
        ApiQuotaStatus? quotaStatus)
    {
        if (rawEvent.TryPickStart(out var startEvent))
        {
            if (replayState.MessageStartEmitted || string.IsNullOrWhiteSpace(startEvent.Message.ID))
                return [];

            replayState.MessageStartEmitted = true;
            replayState.MessageId = startEvent.Message.ID;

            return
            [
                CreateUpdate(
                    quotaStatus,
                    responseId: startEvent.Message.ID,
                    role: ChatRole.Assistant,
                    rawRepresentation: rawEvent),
            ];
        }

        if (rawEvent.TryPickDelta(out var deltaEvent))
        {
            var contents = new List<AIContent>();
            var usageDetails = ToUsageDetails(deltaEvent.Usage);
            if (usageDetails != null)
                contents.Add(new UsageContent(usageDetails));

            var stopReason = deltaEvent.Delta.StopReason?.Raw();
            return
            [
                CreateUpdate(
                    quotaStatus,
                    responseId: replayState.MessageId,
                    contents: contents,
                    finishReason: string.IsNullOrWhiteSpace(stopReason) ? null : new ChatFinishReason(stopReason),
                    rawRepresentation: rawEvent),
            ];
        }

        if (rawEvent.TryPickContentBlockStart(out var blockStartEvent))
        {
            var replayBlockState = replayState.GetOrCreateBlockState(blockStartEvent.Index, blockStartEvent);
            var builder = CreateStreamingBlockBuilder(blockStartEvent, replayBlockState);
            if (builder == null)
                return [CreateUpdate(quotaStatus, responseId: replayState.MessageId, rawRepresentation: rawEvent)];

            replayState.SetActiveBuilder(blockStartEvent.Index, builder);
            return AttachRawEvent(
                builder.GetInitialUpdates(quotaStatus, replayState.MessageId),
                rawEvent,
                quotaStatus,
                replayState.MessageId);
        }

        if (rawEvent.TryPickContentBlockDelta(out var blockDeltaEvent))
        {
            if (!replayState.TryGetActiveBuilder(blockDeltaEvent.Index, out var builder))
                return [CreateUpdate(quotaStatus, responseId: replayState.MessageId, rawRepresentation: rawEvent)];

            return AttachRawEvent(
                builder.ApplyDelta(blockDeltaEvent, quotaStatus, replayState.MessageId),
                rawEvent,
                quotaStatus,
                replayState.MessageId);
        }

        if (rawEvent.TryPickContentBlockStop(out var blockStopEvent))
        {
            if (!replayState.TryRemoveActiveBuilder(blockStopEvent.Index, out var builder))
                return [CreateUpdate(quotaStatus, responseId: replayState.MessageId, rawRepresentation: rawEvent)];

            return AttachRawEvent(
                FinalizeStreamingBlock(
                    blockStopEvent.Index,
                    builder,
                    replayState,
                    quotaStatus,
                    replayState.MessageId),
                rawEvent,
                quotaStatus,
                replayState.MessageId);
        }

        if (rawEvent.TryPickStop(out _))
            return [CreateUpdate(quotaStatus, responseId: replayState.MessageId, rawRepresentation: rawEvent)];

        return [];
    }

    private static IReadOnlyList<ChatResponseUpdate> AttachRawEvent(
        IReadOnlyList<ChatResponseUpdate> updates,
        RawMessageStreamEvent rawEvent,
        ApiQuotaStatus? quotaStatus,
        string? responseId)
    {
        if (updates.Count == 0)
            return [CreateUpdate(quotaStatus, responseId: responseId, rawRepresentation: rawEvent)];

        var first = updates[0];
        first.RawRepresentation = rawEvent;
        return updates;
    }

    private static StreamingContentBlockBuilder? CreateStreamingBlockBuilder(
        RawContentBlockStartEvent startEvent,
        ReplayBlockState replayState)
    {
        var contentBlock = startEvent.ContentBlock;

        if (contentBlock.TryPickText(out var textBlock))
            return new TextStreamingBlockBuilder(textBlock.Text ?? string.Empty, replayState);

        if (contentBlock.TryPickThinking(out var thinkingBlock))
            return new ThinkingStreamingBlockBuilder(
                thinkingBlock.Thinking ?? string.Empty,
                thinkingBlock.Signature,
                replayState);

        if (contentBlock.TryPickToolUse(out var toolUseBlock))
        {
            var initialInput = JsonSerializer.SerializeToElement(toolUseBlock.Input);
            return new ToolUseStreamingBlockBuilder(
                toolUseBlock.ID ?? string.Empty,
                toolUseBlock.Name ?? string.Empty,
                initialInput,
                replayState);
        }

        return null;
    }

    private static IReadOnlyList<ChatResponseUpdate> FinalizeStreamingBlock(
        long index,
        StreamingContentBlockBuilder builder,
        StreamingReplayState replayState,
        ApiQuotaStatus? quotaStatus,
        string? responseId)
    {
        var finalizedContent = builder.Build();
        if (finalizedContent == null)
            return [];

        var replayBlockState = replayState.GetBlockState(index);
        if (replayBlockState.FinalizedContent != null)
        {
            if (!AreEquivalentContents(replayBlockState.FinalizedContent, finalizedContent))
            {
                throw new StreamReplayMismatchException(
                    "流式重连后的响应和已经输出的内容对不上，已停止自动恢复。");
            }

            return [];
        }

        replayBlockState.FinalizedContent = finalizedContent;
        if (finalizedContent is TextContent textContent)
            replayBlockState.EmittedText = textContent.Text ?? string.Empty;
        else if (finalizedContent is TextReasoningContent reasoningContent)
            replayBlockState.EmittedText = reasoningContent.Text ?? string.Empty;

        return finalizedContent is FunctionCallContent
            ? [CreateUpdate(quotaStatus, responseId: responseId, contents: [finalizedContent])]
            : [];
    }

    private static UsageDetails? ToUsageDetails(MessageDeltaUsage? usage)
    {
        if (usage == null)
            return null;

        var details = new UsageDetails
        {
            InputTokenCount = usage.InputTokens,
            OutputTokenCount = usage.OutputTokens,
            TotalTokenCount = usage.InputTokens.HasValue
                ? usage.InputTokens.Value + usage.OutputTokens
                : null,
            CachedInputTokenCount = usage.CacheReadInputTokens,
        };

        if (usage.CacheCreationInputTokens.HasValue)
        {
            details.AdditionalCounts = new AdditionalPropertiesDictionary<long>
            {
                ["CacheCreationInputTokens"] = usage.CacheCreationInputTokens.Value,
            };
        }

        var hasSignal =
            details.InputTokenCount.HasValue ||
            details.OutputTokenCount.HasValue ||
            details.TotalTokenCount.HasValue ||
            details.CachedInputTokenCount.HasValue ||
            (details.AdditionalCounts?.Count ?? 0) > 0;

        return hasSignal ? details : null;
    }

    private static ChatResponseUpdate CreateUpdate(
        ApiQuotaStatus? quotaStatus,
        string? responseId = null,
        ChatRole? role = null,
        IList<AIContent>? contents = null,
        ChatFinishReason? finishReason = null,
        object? rawRepresentation = null)
    {
        return new ChatResponseUpdate
        {
            ResponseId = responseId,
            Role = role,
            Contents = contents ?? [],
            FinishReason = finishReason,
            RawRepresentation = rawRepresentation,
            AdditionalProperties = CreateAdditionalProperties(quotaStatus),
        };
    }

    private static AdditionalPropertiesDictionary? CreateAdditionalProperties(ApiQuotaStatus? quotaStatus)
    {
        return quotaStatus == null
            ? null
            : new AdditionalPropertiesDictionary
            {
                [ChatClientPropertyKeys.ApiQuotaStatus] = quotaStatus,
            };
    }

    private static void AttachQuotaStatus(ChatResponse response, ApiQuotaStatus? quotaStatus)
    {
        if (quotaStatus == null)
            return;

        response.AdditionalProperties ??= new AdditionalPropertiesDictionary();
        response.AdditionalProperties[ChatClientPropertyKeys.ApiQuotaStatus] = quotaStatus;
    }

    private static ChatResponseUpdate AttachQuotaStatus(ChatResponseUpdate update, ApiQuotaStatus? quotaStatus)
    {
        if (quotaStatus == null)
            return update;

        update.AdditionalProperties ??= new AdditionalPropertiesDictionary();
        update.AdditionalProperties[ChatClientPropertyKeys.ApiQuotaStatus] = quotaStatus;
        update.Contents ??= [];
        return update;
    }

    private static void AttachQuotaStatus(Exception exception, ApiQuotaStatus? quotaStatus)
    {
        if (quotaStatus != null)
            exception.Data[ChatClientPropertyKeys.ApiQuotaStatus] = quotaStatus;
    }

    private ApiQuotaSnapshot? TakeSnapshot(ApiRequestScope requestScope)
    {
        return responseObserver.TryTakeSnapshot(requestScope.RequestId, out var snapshot)
            ? snapshot
            : null;
    }

    private static bool TryGetRawEvent(ChatResponseUpdate update, out RawMessageStreamEvent rawEvent)
    {
        if (update.RawRepresentation is RawMessageStreamEvent typed)
        {
            rawEvent = typed;
            return true;
        }

        rawEvent = null!;
        return false;
    }

    private static CancellationTokenSource CreateAttemptCancellationSource(
        TimeSpan requestTimeout,
        CancellationToken cancellationToken)
    {
        var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (requestTimeout > TimeSpan.Zero && requestTimeout != Timeout.InfiniteTimeSpan)
            attemptCts.CancelAfter(requestTimeout);

        return attemptCts;
    }

    private static bool TryPlanRetry(
        Exception exception,
        ApiQuotaSnapshot? snapshot,
        RetrySettings retrySettings,
        int retryCount,
        CancellationToken cancellationToken,
        CancellationTokenSource attemptCts,
        out TimeSpan delay)
    {
        delay = TimeSpan.Zero;
        if (retryCount >= retrySettings.MaxRetryCount)
            return false;

        if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
            return false;

        if (exception is StreamReplayMismatchException)
            return false;

        if (exception is AnthropicApiException apiException)
        {
            if (!IsRetryableStatusCode(apiException.StatusCode))
                return false;

            delay = ComputeRetryDelay(
                retryCount,
                snapshot,
                retrySettings,
                preferRetryAfter: apiException.StatusCode == HttpStatusCode.TooManyRequests);
            return true;
        }

        if (exception is StreamingInterruptedException or HttpRequestException or IOException)
        {
            delay = ComputeRetryDelay(retryCount, snapshot, retrySettings, preferRetryAfter: true);
            return true;
        }

        if (exception is TimeoutException ||
            exception is TaskCanceledException ||
            (exception is OperationCanceledException && attemptCts.IsCancellationRequested))
        {
            delay = ComputeRetryDelay(retryCount, snapshot, retrySettings, preferRetryAfter: true);
            return true;
        }

        return false;
    }

    private static TimeSpan ComputeRetryDelay(
        int retryCount,
        ApiQuotaSnapshot? snapshot,
        RetrySettings retrySettings,
        bool preferRetryAfter)
    {
        if (preferRetryAfter &&
            ApiQuotaStatus.FromSnapshot(snapshot)?.RetryAfter is { } retryAfter &&
            retryAfter >= TimeSpan.Zero)
        {
            return ClampRetryDelay(retryAfter, retrySettings.MaxDelay);
        }

        var multiplier = Math.Max(1.0, retrySettings.BackoffMultiplier);
        var rawDelayMilliseconds = retrySettings.BaseDelay.TotalMilliseconds *
                                   Math.Pow(multiplier, retryCount);
        return ClampRetryDelay(TimeSpan.FromMilliseconds(rawDelayMilliseconds), retrySettings.MaxDelay);
    }

    private static TimeSpan ClampRetryDelay(TimeSpan delay, TimeSpan maxDelay)
    {
        if (delay < TimeSpan.Zero)
            return TimeSpan.Zero;

        return delay > maxDelay ? maxDelay : delay;
    }

    private static bool IsRetryableStatusCode(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.TooManyRequests or
               HttpStatusCode.InternalServerError or
               HttpStatusCode.BadGateway or
               HttpStatusCode.ServiceUnavailable;
    }

    private static bool AreEquivalentContents(AIContent expected, AIContent actual)
    {
        return (expected, actual) switch
        {
            (TextContent left, TextContent right) =>
                string.Equals(left.Text, right.Text, StringComparison.Ordinal),
            (TextReasoningContent left, TextReasoningContent right) =>
                string.Equals(left.Text, right.Text, StringComparison.Ordinal) &&
                string.Equals(left.ProtectedData, right.ProtectedData, StringComparison.Ordinal),
            (FunctionCallContent left, FunctionCallContent right) =>
                string.Equals(left.CallId, right.CallId, StringComparison.Ordinal) &&
                string.Equals(left.Name, right.Name, StringComparison.Ordinal) &&
                string.Equals(
                    JsonSerializer.Serialize(left.Arguments ?? new Dictionary<string, object?>()),
                    JsonSerializer.Serialize(right.Arguments ?? new Dictionary<string, object?>()),
                    StringComparison.Ordinal),
            _ => false,
        };
    }

    private sealed record RetrySettings(
        TimeSpan RequestTimeout,
        int MaxRetryCount,
        TimeSpan BaseDelay,
        TimeSpan MaxDelay,
        double BackoffMultiplier)
    {
        public static RetrySettings FromChatOptions(ChatOptions? options)
        {
            var properties = options?.AdditionalProperties;
            return new RetrySettings(
                GetTimeSpan(properties, ChatClientPropertyKeys.ApiRequestTimeout, TimeSpan.FromMinutes(10)),
                GetInt(properties, ChatClientPropertyKeys.ApiMaxRetryCount, 3),
                GetTimeSpan(properties, ChatClientPropertyKeys.ApiRetryBaseDelay, TimeSpan.FromSeconds(1)),
                GetTimeSpan(properties, ChatClientPropertyKeys.ApiRetryMaxDelay, TimeSpan.FromSeconds(30)),
                GetDouble(properties, ChatClientPropertyKeys.ApiRetryBackoffMultiplier, 2.0));
        }
    }

    private static int GetInt(AdditionalPropertiesDictionary? properties, string key, int defaultValue)
    {
        if (properties == null || !properties.TryGetValue(key, out var value) || value == null)
            return defaultValue;

        return value switch
        {
            int typed => typed,
            long typed => (int)typed,
            JsonElement json when json.ValueKind == JsonValueKind.Number && json.TryGetInt32(out var number) => number,
            JsonElement json when json.ValueKind == JsonValueKind.String &&
                                  int.TryParse(json.GetString(), out var number) => number,
            _ => Convert.ToInt32(value),
        };
    }

    private static double GetDouble(AdditionalPropertiesDictionary? properties, string key, double defaultValue)
    {
        if (properties == null || !properties.TryGetValue(key, out var value) || value == null)
            return defaultValue;

        return value switch
        {
            double typed => typed,
            float typed => typed,
            JsonElement json when json.ValueKind == JsonValueKind.Number && json.TryGetDouble(out var number) => number,
            JsonElement json when json.ValueKind == JsonValueKind.String &&
                                  double.TryParse(json.GetString(), out var number) => number,
            _ => Convert.ToDouble(value),
        };
    }

    private static TimeSpan GetTimeSpan(AdditionalPropertiesDictionary? properties, string key, TimeSpan defaultValue)
    {
        if (properties == null || !properties.TryGetValue(key, out var value) || value == null)
            return defaultValue;

        return value switch
        {
            TimeSpan typed => typed,
            JsonElement json when json.ValueKind == JsonValueKind.String &&
                                  TimeSpan.TryParse(json.GetString(), out var parsed) => parsed,
            _ when TimeSpan.TryParse(value.ToString(), out var parsed) => parsed,
            _ => defaultValue,
        };
    }

    private abstract class StreamingContentBlockBuilder(ReplayBlockState replayState)
    {
        protected ReplayBlockState ReplayState { get; } = replayState;

        public virtual IReadOnlyList<ChatResponseUpdate> GetInitialUpdates(
            ApiQuotaStatus? quotaStatus,
            string? responseId) => [];

        public abstract IReadOnlyList<ChatResponseUpdate> ApplyDelta(
            RawContentBlockDeltaEvent deltaEvent,
            ApiQuotaStatus? quotaStatus,
            string? responseId);

        public abstract AIContent? Build();
    }

    private sealed class TextStreamingBlockBuilder(
        string initialText,
        ReplayBlockState replayState) : StreamingContentBlockBuilder(replayState)
    {
        private readonly StringBuilder _text = new(initialText ?? string.Empty);
        private int _observedLength;

        public override IReadOnlyList<ChatResponseUpdate> GetInitialUpdates(
            ApiQuotaStatus? quotaStatus,
            string? responseId) =>
            EmitReplayAwareText(_text.ToString(), quotaStatus, responseId);

        public override IReadOnlyList<ChatResponseUpdate> ApplyDelta(
            RawContentBlockDeltaEvent deltaEvent,
            ApiQuotaStatus? quotaStatus,
            string? responseId)
        {
            if (!deltaEvent.Delta.TryPickText(out var textDelta) || string.IsNullOrEmpty(textDelta.Text))
                return [];

            _text.Append(textDelta.Text);
            return EmitReplayAwareText(_text.ToString(), quotaStatus, responseId);
        }

        public override AIContent Build() => new TextContent(_text.ToString());

        private IReadOnlyList<ChatResponseUpdate> EmitReplayAwareText(
            string currentText,
            ApiQuotaStatus? quotaStatus,
            string? responseId)
        {
            var priorObservedLength = _observedLength;
            _observedLength = currentText.Length;
            var emittedPrefix = ReplayState.EmittedText;

            if (string.IsNullOrEmpty(currentText))
                return [];

            if (currentText.Length <= emittedPrefix.Length)
            {
                if (!emittedPrefix.AsSpan(0, currentText.Length).SequenceEqual(currentText))
                {
                    throw new StreamReplayMismatchException(
                        "流式重连后的文本前缀和已输出内容不一致。");
                }

                return [];
            }

            if (!currentText.AsSpan(0, emittedPrefix.Length).SequenceEqual(emittedPrefix))
            {
                throw new StreamReplayMismatchException(
                    "流式重连后的文本前缀和已输出内容不一致。");
            }

            var startIndex = Math.Max(priorObservedLength, emittedPrefix.Length);
            if (startIndex >= currentText.Length)
                return [];

            var freshText = currentText[startIndex..];
            ReplayState.EmittedText = currentText;
            return [CreateUpdate(quotaStatus, responseId: responseId, contents: [new TextContent(freshText)])];
        }
    }

    private sealed class ThinkingStreamingBlockBuilder(
        string initialThinking,
        string? signature,
        ReplayBlockState replayState) : StreamingContentBlockBuilder(replayState)
    {
        private readonly StringBuilder _thinking = new(initialThinking ?? string.Empty);
        private int _observedLength;
        private string? _signature = signature;

        public override IReadOnlyList<ChatResponseUpdate> GetInitialUpdates(
            ApiQuotaStatus? quotaStatus,
            string? responseId) =>
            EmitReplayAwareThinking(_thinking.ToString(), quotaStatus, responseId);

        public override IReadOnlyList<ChatResponseUpdate> ApplyDelta(
            RawContentBlockDeltaEvent deltaEvent,
            ApiQuotaStatus? quotaStatus,
            string? responseId)
        {
            if (deltaEvent.Delta.TryPickThinking(out var thinkingDelta))
            {
                if (!string.IsNullOrEmpty(thinkingDelta.Thinking))
                    _thinking.Append(thinkingDelta.Thinking);

                return EmitReplayAwareThinking(_thinking.ToString(), quotaStatus, responseId);
            }

            if (deltaEvent.Delta.TryPickSignature(out var signatureDelta))
                _signature = signatureDelta.Signature;

            return [];
        }

        public override AIContent Build() =>
            new TextReasoningContent(_thinking.ToString())
            {
                ProtectedData = _signature,
            };

        private IReadOnlyList<ChatResponseUpdate> EmitReplayAwareThinking(
            string currentThinking,
            ApiQuotaStatus? quotaStatus,
            string? responseId)
        {
            var priorObservedLength = _observedLength;
            _observedLength = currentThinking.Length;
            var emittedPrefix = ReplayState.EmittedText;

            if (string.IsNullOrEmpty(currentThinking))
                return [];

            if (currentThinking.Length <= emittedPrefix.Length)
            {
                if (!emittedPrefix.AsSpan(0, currentThinking.Length).SequenceEqual(currentThinking))
                {
                    throw new StreamReplayMismatchException(
                        "流式重连后的 thinking 前缀和已输出内容不一致。");
                }

                return [];
            }

            if (!currentThinking.AsSpan(0, emittedPrefix.Length).SequenceEqual(emittedPrefix))
            {
                throw new StreamReplayMismatchException(
                    "流式重连后的 thinking 前缀和已输出内容不一致。");
            }

            var startIndex = Math.Max(priorObservedLength, emittedPrefix.Length);
            if (startIndex >= currentThinking.Length)
                return [];

            var freshThinking = currentThinking[startIndex..];
            ReplayState.EmittedText = currentThinking;
            return
            [
                CreateUpdate(
                    quotaStatus,
                    responseId: responseId,
                    contents:
                    [
                        new TextReasoningContent(freshThinking)
                        {
                            ProtectedData = _signature,
                        },
                    ]),
            ];
        }
    }

    private sealed class ToolUseStreamingBlockBuilder(
        string toolUseId,
        string name,
        JsonElement initialInput,
        ReplayBlockState replayState) : StreamingContentBlockBuilder(replayState)
    {
        private readonly JsonElement _initialInput = initialInput.Clone();
        private readonly StringBuilder _partialJson = new();

        public override IReadOnlyList<ChatResponseUpdate> ApplyDelta(
            RawContentBlockDeltaEvent deltaEvent,
            ApiQuotaStatus? quotaStatus,
            string? responseId)
        {
            if (deltaEvent.Delta.TryPickInputJson(out var jsonDelta) &&
                !string.IsNullOrEmpty(jsonDelta.PartialJson))
            {
                _partialJson.Append(jsonDelta.PartialJson);
            }

            return [];
        }

        public override AIContent Build()
        {
            var arguments = BuildArguments();
            return new FunctionCallContent(toolUseId, name, arguments);
        }

        private IDictionary<string, object?> BuildArguments()
        {
            var input = BuildInput();
            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, object?>>(input.GetRawText()) ??
                       new Dictionary<string, object?>();
            }
            catch
            {
                return new Dictionary<string, object?>();
            }
        }

        private JsonElement BuildInput()
        {
            if (_partialJson.Length > 0)
            {
                try
                {
                    using var document = JsonDocument.Parse(_partialJson.ToString());
                    return document.RootElement.Clone();
                }
                catch
                {
                }
            }

            return _initialInput.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
                ? JsonSerializer.SerializeToElement(new { })
                : _initialInput.Clone();
        }
    }

    private sealed class StreamingReplayState
    {
        private readonly Dictionary<long, ReplayBlockState> _blockStates = [];
        private readonly Dictionary<long, StreamingContentBlockBuilder> _activeBuilders = [];

        public bool MessageStartEmitted { get; set; }

        public string? MessageId { get; set; }

        public ReplayBlockState GetOrCreateBlockState(long index, RawContentBlockStartEvent startEvent)
        {
            var blockType = startEvent.ContentBlock.Type.GetString() ?? "unknown";

            if (_blockStates.TryGetValue(index, out var existing))
            {
                if (!string.Equals(existing.BlockType, blockType, StringComparison.Ordinal))
                {
                    throw new StreamReplayMismatchException(
                        "流式重连后的内容块类型和首次输出不一致。");
                }

                return existing;
            }

            var created = new ReplayBlockState(blockType);
            _blockStates[index] = created;
            return created;
        }

        public ReplayBlockState GetBlockState(long index) => _blockStates[index];

        public void SetActiveBuilder(long index, StreamingContentBlockBuilder builder) =>
            _activeBuilders[index] = builder;

        public bool TryGetActiveBuilder(long index, out StreamingContentBlockBuilder builder) =>
            _activeBuilders.TryGetValue(index, out builder!);

        public bool TryRemoveActiveBuilder(long index, out StreamingContentBlockBuilder builder)
        {
            if (_activeBuilders.TryGetValue(index, out builder!))
            {
                _activeBuilders.Remove(index);
                return true;
            }

            builder = null!;
            return false;
        }
    }

    private sealed class ReplayBlockState(string blockType)
    {
        public string BlockType { get; } = blockType;

        public string EmittedText { get; set; } = string.Empty;

        public AIContent? FinalizedContent { get; set; }
    }

    private sealed class StreamingInterruptedException(string message, Exception? innerException = null)
        : Exception(message, innerException);

    private sealed class StreamReplayMismatchException(string message) : Exception(message);
}

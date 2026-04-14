using System.Runtime.CompilerServices;
using System.Text.Json;
using Aexon.Core.Compaction;
using Aexon.Core.Hooks;
using Aexon.Core.Memory;
using Aexon.Core.Messages;
using Aexon.Core.Permissions;
using Aexon.Core.Storage;
using Aexon.Core.Tools;
using Microsoft.Extensions.AI;

namespace Aexon.Core.Query;

/// <summary>
/// Runs the main conversation loop, including model calls, tool execution, and compaction.
/// </summary>
public class QueryEngine : IAsyncDisposable
{
    private readonly IChatClient _chatClient;
    private readonly ToolRegistry _tools;
    private readonly IToolRuntime _toolRuntime;
    private readonly IConversationCompactor _compactor;
    private readonly IMicroCompactor _microCompactor;
    private readonly ISessionMemoryCompactor _sessionMemoryCompactor;
    private readonly IContextPressurePipeline _contextPressurePipeline;
    private readonly IHookRuntime _hooks;
    private readonly QueryEngineConfig _config;
    private readonly Context.ContextProvider _contextProvider;
    private readonly IConversationJournal? _journal;
    private readonly SessionMemoryFile? _sessionMemoryFile;
    private readonly AskUserQuestionHandler? _askUserQuestion;
    private readonly ConversationSessionMetadata _sessionMetadata;
    private readonly List<ConversationMessage> _messages;
    private TokenUsage _totalUsage;
    private int _consecutiveAutoCompactFailures;
    private bool _sessionStarted;
    private bool _sessionEnded;

    public QueryEngine(
        IChatClient chatClient,
        ToolRegistry tools,
        IPermissionChecker permissions,
        QueryEngineConfig config,
        Context.ContextProvider contextProvider,
        IToolRuntime? toolRuntime = null,
        IConversationCompactor? compactor = null,
        IMicroCompactor? microCompactor = null,
        ISessionMemoryCompactor? sessionMemoryCompactor = null,
        IContextPressurePipeline? contextPressurePipeline = null,
        IHookRuntime? hooks = null,
        IConversationJournal? journal = null,
        SessionMemoryFile? sessionMemoryFile = null,
        IReadOnlyList<ConversationMessage>? initialMessages = null,
        TokenUsage? initialUsage = null,
        ConversationSessionMetadata? initialMetadata = null,
        AskUserQuestionHandler? askUserQuestion = null)
    {
        _chatClient = chatClient;
        _tools = tools;
        var effectiveHooks = hooks ?? HookRuntime.Empty;
        _toolRuntime = toolRuntime ?? new StreamingToolExecutor(tools, permissions, effectiveHooks);
        _compactor = compactor ?? new HeuristicConversationCompactor();
        _microCompactor = microCompactor ?? new TimeBasedMicroCompactor();
        _sessionMemoryCompactor = sessionMemoryCompactor ?? new SessionMemoryCompactor();
        _contextPressurePipeline = contextPressurePipeline ??
                                  new DefaultContextPressurePipeline(
                                      microCompactor: _microCompactor,
                                      sessionMemoryCompactor: _sessionMemoryCompactor,
                                      conversationCompactor: _compactor);
        _hooks = effectiveHooks;
        _config = config;
        _contextProvider = contextProvider;
        _journal = journal;
        _sessionMemoryFile = sessionMemoryFile;
        _askUserQuestion = askUserQuestion;
        _sessionMetadata = initialMetadata?.Clone() ?? journal?.Metadata ?? new ConversationSessionMetadata();
        _messages = initialMessages?.ToList() ?? [];
        _totalUsage = initialUsage ?? ComputeTotalUsage(_messages);
    }

    /// <summary>
    /// Gets the current conversation messages.
    /// </summary>
    public IReadOnlyList<ConversationMessage> Messages => _messages;

    /// <summary>
    /// Gets the accumulated token usage.
    /// </summary>
    public TokenUsage TotalUsage => _totalUsage;

    /// <summary>
    /// Gets the active model identifier.
    /// </summary>
    public string CurrentModel => _config.Model;

    /// <summary>
    /// Resolves a model alias and updates the active model.
    /// </summary>
    public string SetModel(string modelOrAlias)
    {
        _config.Model = ClaudeModels.Resolve(modelOrAlias);
        return _config.Model;
    }

    public async Task<string> SetModelAsync(
        string modelOrAlias,
        CancellationToken ct = default)
    {
        var resolved = SetModel(modelOrAlias);
        await PersistRuntimeStateAsync(ct);
        return resolved;
    }

    public string? SessionId => _journal?.SessionId;

    public string? TranscriptPath => _journal?.TranscriptPath;

    public ConversationSessionMetadata SessionMetadata => _sessionMetadata.Clone();

    /// <summary>
    /// Sends a user message through the agent loop and streams query events.
    /// </summary>
    public async IAsyncEnumerable<QueryEvent> SubmitMessageAsync(
        string userInput,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var startTime = DateTimeOffset.UtcNow;
        var turnCount = 0;

        await EnsureSessionStartedAsync(ct);

        await AddMessageAsync(UserMessage.FromText(userInput), ct);

        while (!ct.IsCancellationRequested)
        {
            turnCount++;

            var compactionEvents = await PrepareContextForTurnAsync(ct);
            foreach (var compactionEvent in compactionEvents)
                yield return compactionEvent;

            var systemPrompt = await BuildSystemPromptAsync();
            var chatMessages = ChatMessageConverter.ToMeaiMessages(_messages);
            var toolDefs = await _tools.GetToolDefinitionsAsync();

            yield return new StatusEvent("calling_api");

            var assistantTurn = new AssistantTurnAccumulator();
            var chatOptions = BuildChatOptions(systemPrompt, toolDefs);

            string? requestError = null;

            if (_config.UseStreamingApi)
            {
                await using var streamEnumerator =
                    StreamAssistantTurnAsync(chatMessages, chatOptions, assistantTurn, ct).GetAsyncEnumerator(ct);

                while (true)
                {
                    QueryEvent? nextEvent;
                    try
                    {
                        if (!await streamEnumerator.MoveNextAsync())
                            break;

                        nextEvent = streamEnumerator.Current;
                    }
                    catch (Exception ex)
                    {
                        requestError = ex.Message;
                        break;
                    }

                    yield return nextEvent;
                }
            }
            else
            {
                try
                {
                    await CollectAssistantTurnAsync(chatMessages, chatOptions, assistantTurn, ct);
                }
                catch (Exception ex)
                {
                    requestError = ex.Message;
                }
            }

            if (requestError != null)
            {
                await _hooks.OnStopFailureAsync(
                    BuildStopHookContext(
                        success: false,
                        errorMessage: requestError,
                        duration: DateTimeOffset.UtcNow - startTime,
                        turnCount),
                    ct);

                yield return new QueryCompleteEvent
                {
                    Success = false,
                    Duration = DateTimeOffset.UtcNow - startTime,
                    TurnCount = turnCount,
                    TotalUsage = _totalUsage,
                    ErrorMessage = requestError,
                };
                yield break;
            }

            if (assistantTurn.Usage != null)
                _totalUsage += assistantTurn.Usage;

            if (!_config.UseStreamingApi)
            {
                foreach (var evt in EmitBufferedAssistantTurnEvents(assistantTurn))
                    yield return evt;
            }

            if (assistantTurn.MessageId != null || assistantTurn.Usage != null || assistantTurn.ContentBlocks.Count > 0)
                yield return new MessageEndEvent(assistantTurn.StopReason, assistantTurn.Usage);

            await AddMessageAsync(new AssistantMessage
            {
                Content = assistantTurn.ContentBlocks,
                StopReason = assistantTurn.StopReason,
                Usage = assistantTurn.Usage,
            }, ct);

            if (assistantTurn.ToolUseBlocks.Count == 0)
                break;

            if (turnCount >= _config.MaxTurns)
            {
                yield return new TextDeltaEvent(
                    $"\n\n[Reached maximum turn limit of {_config.MaxTurns}]");
                break;
            }

            await foreach (var evt in ExecuteToolCallsAsync(assistantTurn.ToolUseBlocks, ct))
            {
                yield return evt;
            }
        }

        await _hooks.OnStopAsync(
            BuildStopHookContext(
                success: true,
                errorMessage: null,
                duration: DateTimeOffset.UtcNow - startTime,
                turnCount),
            ct);

        yield return new QueryCompleteEvent
        {
            Success = true,
            Duration = DateTimeOffset.UtcNow - startTime,
            TurnCount = turnCount,
            TotalUsage = _totalUsage,
        };
    }

    /// <summary>
    /// Executes the tool calls returned by the model.
    /// </summary>
    private async IAsyncEnumerable<QueryEvent> ExecuteToolCallsAsync(
        List<ToolUseBlock> toolUseBlocks,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var context = CreateToolContext(ct);
        await foreach (var update in _toolRuntime.RunBatchAsync(toolUseBlocks, context, ct))
        {
            switch (update)
            {
                case ToolPermissionRequestUpdate request:
                    {
                        var permissionEvent = new PermissionRequestEvent
                        {
                            ToolName = request.Invocation.Name,
                            Description = request.Description,
                            Input = request.ObservedInput,
                        };

                        yield return permissionEvent;
                        var approved = await permissionEvent.WaitForResponseAsync();
                        request.SetResponse(approved);
                        break;
                    }
                case ToolProgressUpdate progress:
                    {
                        yield return new ToolProgressEvent(
                            progress.ToolUseId,
                            progress.Progress.Message ?? progress.Progress.Type);
                        break;
                    }
                case ToolCompletedUpdate completed:
                    {
                        yield return new ToolResultEvent(
                            completed.Outcome.Invocation.ToolUseId,
                            completed.Outcome.Invocation.Name,
                            completed.Outcome.Result.Data,
                            completed.Outcome.Result.IsError);

                        await AddMessageAsync(UserMessage.FromToolResult(
                            completed.Outcome.Invocation.ToolUseId,
                            completed.Outcome.Result.Data,
                            completed.Outcome.Result.IsError), ct);
                        break;
                    }
            }
        }
    }

    private ToolExecutionContext CreateToolContext(CancellationToken cancellationToken) => new()
    {
        WorkingDirectory = _contextProvider.WorkingDirectory,
        PermissionContext = _contextProvider.GetPermissionContext(),
        Tools = _tools.GetEnabledTools(),
        Messages = _messages,
        CancellationToken = cancellationToken,
        IsNonInteractiveSession = _askUserQuestion == null,
        AskUserQuestionAsync = _askUserQuestion,
        MainLoopModel = _config.Model,
    };

    private async Task<string> BuildSystemPromptAsync()
    {
        return await _contextProvider.BuildSystemPromptAsync(
            _tools.GetEnabledTools(),
            _config);
    }

    private ChatOptions BuildChatOptions(string systemPrompt, IReadOnlyList<JsonElement> toolDefs)
    {
        var options = new ChatOptions
        {
            ModelId = _config.Model,
            MaxOutputTokens = _config.MaxTokens,
            Instructions = systemPrompt,
            Tools = ChatMessageConverter.ToMeaiTools(toolDefs),
            ToolMode = ChatToolMode.Auto,
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["ThinkingMode"] = _config.ThinkingMode.ToString(),
                ["ThinkingBudgetTokens"] = _config.ThinkingBudgetTokens,
            },
        };

        return options;
    }

    /// <summary>
    /// Clears messages.
    /// </summary>
    public async Task ClearMessagesAsync(CancellationToken ct = default)
    {
        await EndSessionAsync(dueToClear: true, ct);
        _messages.Clear();
        _totalUsage = TokenUsage.Empty;
        if (_journal != null)
            await _journal.ResetHeadAsync(ct);
    }

    public void ClearMessages() =>
        ClearMessagesAsync().GetAwaiter().GetResult();

    public async Task SetSessionTitleAsync(
        string? title,
        CancellationToken ct = default)
    {
        _sessionMetadata.Title = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
        if (_journal != null)
        {
            await _journal.UpdateMetadataAsync(
                metadata => metadata.Title = _sessionMetadata.Title,
                ct);
        }
    }

    public async Task AddSessionTagAsync(
        string tag,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return;

        _sessionMetadata.Tags.Add(tag.Trim());
        if (_journal != null)
        {
            var tags = _sessionMetadata.Tags.ToArray();
            await _journal.UpdateMetadataAsync(
                metadata =>
                {
                    metadata.Tags.Clear();
                    foreach (var item in tags)
                        metadata.Tags.Add(item);
                },
                ct);
        }
    }

    public async Task RemoveSessionTagAsync(
        string tag,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return;

        _sessionMetadata.Tags.Remove(tag.Trim());
        if (_journal != null)
        {
            var tags = _sessionMetadata.Tags.ToArray();
            await _journal.UpdateMetadataAsync(
                metadata =>
                {
                    metadata.Tags.Clear();
                    foreach (var item in tags)
                        metadata.Tags.Add(item);
                },
                ct);
        }
    }

    public async Task ClearSessionTagsAsync(CancellationToken ct = default)
    {
        _sessionMetadata.Tags.Clear();
        if (_journal != null)
        {
            await _journal.UpdateMetadataAsync(
                metadata => metadata.Tags.Clear(),
                ct);
        }
    }

    public async Task SetPermissionModeAsync(
        PermissionMode mode,
        CancellationToken ct = default)
    {
        _contextProvider.PermissionContext.Mode = mode;
        _sessionMetadata.Mode = mode;
        if (_journal != null)
        {
            await _journal.UpdateMetadataAsync(
                metadata => metadata.Mode = mode,
                ct);
        }
    }

    public async Task<ConversationCompactionResult?> CompactAsync(
        int preserveTailCount = 8,
        CancellationToken ct = default)
    {
        await _hooks.OnPreCompactAsync(
            new CompactHookContext(
                CompactionLifecycleKind.Conversation,
                automatic: false,
                reason: "manual",
                preserveTailCount: preserveTailCount,
                messageCount: _messages.Count),
            ct);

        var result = _compactor.Compact(_messages, preserveTailCount);
        if (result == null)
            return null;

        await ApplyCompactionResultAsync(result, ct);
        await _hooks.OnPostCompactAsync(
            new CompactHookContext(
                CompactionLifecycleKind.Conversation,
                automatic: false,
                reason: "manual",
                preserveTailCount: preserveTailCount,
                messageCount: _messages.Count,
                conversationResult: result),
            ct);
        return result;
    }

    public async Task<ConversationCompactionResult?> CompactUpToAsync(
        int upToIndex,
        CancellationToken ct = default)
    {
        await _hooks.OnPreCompactAsync(
            new CompactHookContext(
                CompactionLifecycleKind.Conversation,
                automatic: false,
                reason: "manual",
                preserveTailCount: upToIndex,
                messageCount: _messages.Count),
            ct);

        var result = _compactor.CompactUpTo(_messages, upToIndex);
        if (result == null)
            return null;

        await ApplyCompactionResultAsync(result, ct);
        await _hooks.OnPostCompactAsync(
            new CompactHookContext(
                CompactionLifecycleKind.Conversation,
                automatic: false,
                reason: "manual",
                preserveTailCount: upToIndex,
                messageCount: _messages.Count,
                conversationResult: result),
            ct);
        return result;
    }

    public async Task<ConversationCompactionResult?> CompactFromAsync(
        int fromIndex,
        CancellationToken ct = default)
    {
        await _hooks.OnPreCompactAsync(
            new CompactHookContext(
                CompactionLifecycleKind.Conversation,
                automatic: false,
                reason: "manual",
                preserveTailCount: fromIndex,
                messageCount: _messages.Count),
            ct);

        var result = _compactor.CompactFrom(_messages, fromIndex);
        if (result == null)
            return null;

        await ApplyCompactionResultAsync(result, ct);
        await _hooks.OnPostCompactAsync(
            new CompactHookContext(
                CompactionLifecycleKind.Conversation,
                automatic: false,
                reason: "manual",
                preserveTailCount: fromIndex,
                messageCount: _messages.Count,
                conversationResult: result),
            ct);
        return result;
    }

    public async Task<SessionMemoryCompactionResult?> SessionMemoryCompactAsync(
        int preserveTailCount = 8,
        CancellationToken ct = default)
    {
        await _hooks.OnPreCompactAsync(
            new CompactHookContext(
                CompactionLifecycleKind.SessionMemory,
                automatic: false,
                reason: "manual",
                preserveTailCount: preserveTailCount,
                messageCount: _messages.Count),
            ct);

        var result = _sessionMemoryCompactor.Compact(
            _messages,
            new SessionMemoryCompactionOptions
            {
                PreserveTailCount = preserveTailCount,
            });

        if (result == null || !result.HasChanges)
            return null;

        await ApplySessionMemoryCompactionResultAsync(result, ct);
        await _hooks.OnPostCompactAsync(
            new CompactHookContext(
                CompactionLifecycleKind.SessionMemory,
                automatic: false,
                reason: "manual",
                preserveTailCount: preserveTailCount,
                messageCount: _messages.Count,
                sessionMemoryResult: result),
            ct);
        return result;
    }

    public async Task<MicrocompactResult?> MicrocompactAsync(
        int preserveTailCount = 8,
        bool force = true,
        CancellationToken ct = default)
    {
        await _hooks.OnPreCompactAsync(
            new CompactHookContext(
                CompactionLifecycleKind.Microcompact,
                automatic: false,
                reason: "manual",
                preserveTailCount: preserveTailCount,
                messageCount: _messages.Count),
            ct);

        var result = _microCompactor.Run(
            _messages,
            new MicrocompactRunOptions
            {
                PreserveTailCount = preserveTailCount,
                Force = force,
            });

        if (!result.HasChanges)
            return null;

        await ApplyMicrocompactResultAsync(result, ct);
        await _hooks.OnPostCompactAsync(
            new CompactHookContext(
                CompactionLifecycleKind.Microcompact,
                automatic: false,
                reason: "manual",
                preserveTailCount: preserveTailCount,
                messageCount: _messages.Count,
                microcompactResult: result),
            ct);
        return result;
    }

    private async Task<IReadOnlyList<QueryEvent>> PrepareContextForTurnAsync(
        CancellationToken ct)
    {
        var events = new List<QueryEvent>();

        if (!_config.EnableAutoCompact)
            return events;

        if (_consecutiveAutoCompactFailures >= _config.AutoCompactFailureLimit)
        {
            events.Add(new ContextCompactionEvent
            {
                Mode = "skipped",
                Automatic = true,
                Reason = $"auto-compact circuit open after {_consecutiveAutoCompactFailures} consecutive failures",
            });
            return events;
        }

        try
        {
            var preparation = _contextPressurePipeline.Prepare(
                _messages,
                new ContextPressureOptions
                {
                    EnableAutoCompact = _config.EnableAutoCompact,
                    EnableSessionMemoryCompact = _config.EnableSessionMemoryCompact,
                    PreserveTailCount = _config.AutoCompactPreserveTailCount,
                    Policy = new AutoCompactPolicyOptions
                    {
                        ApproxContextWindowTokens = _config.ApproxContextWindowTokens,
                        MaxOutputTokens = _config.MaxTokens,
                        BufferTokens = _config.AutoCompactBufferTokens,
                        ApproxCharsPerToken = _config.ApproxCharsPerToken,
                        MinimumMessageCount = _config.AutoCompactMinimumMessageCount,
                        WarningRatio = _config.AutoCompactWarningRatio,
                        BlockingRatio = _config.AutoCompactBlockingRatio,
                    },
                    SessionMemory = new SessionMemoryCompactionOptions
                    {
                        PreserveTailCount = _config.AutoCompactPreserveTailCount,
                    },
                });

            if (preparation.MicrocompactResult is { HasChanges: true } microcompact)
            {
                await _hooks.OnPreCompactAsync(
                    new CompactHookContext(
                        CompactionLifecycleKind.Microcompact,
                        automatic: true,
                        reason: preparation.InitialDecision.Reason,
                        preserveTailCount: _config.AutoCompactPreserveTailCount,
                        messageCount: _messages.Count),
                    ct);

                await ApplyMicrocompactResultAsync(microcompact, ct);
                await _hooks.OnPostCompactAsync(
                    new CompactHookContext(
                        CompactionLifecycleKind.Microcompact,
                        automatic: true,
                        reason: preparation.InitialDecision.Reason,
                        preserveTailCount: _config.AutoCompactPreserveTailCount,
                        messageCount: _messages.Count,
                        microcompactResult: microcompact),
                    ct);
                events.Add(new ContextCompactionEvent
                {
                    Mode = "microcompact",
                    Automatic = true,
                    Reason = preparation.InitialDecision.Reason,
                    ClearedToolResults = microcompact.ClearedToolResultCount,
                    ClearedThinkingBlocks = microcompact.ClearedThinkingBlockCount,
                });
            }

            if (preparation.SessionMemoryResult is { HasChanges: true } sessionMemory)
            {
                await _hooks.OnPreCompactAsync(
                    new CompactHookContext(
                        CompactionLifecycleKind.SessionMemory,
                        automatic: true,
                        reason: preparation.InitialDecision.Reason,
                        preserveTailCount: _config.AutoCompactPreserveTailCount,
                        messageCount: _messages.Count),
                    ct);

                await ApplySessionMemoryCompactionResultAsync(sessionMemory, ct);
                await _hooks.OnPostCompactAsync(
                    new CompactHookContext(
                        CompactionLifecycleKind.SessionMemory,
                        automatic: true,
                        reason: preparation.InitialDecision.Reason,
                        preserveTailCount: _config.AutoCompactPreserveTailCount,
                        messageCount: _messages.Count,
                        sessionMemoryResult: sessionMemory),
                    ct);
                events.Add(new ContextCompactionEvent
                {
                    Mode = "session_memory",
                    Automatic = true,
                    Reason = preparation.InitialDecision.Reason,
                    RemovedMessages = sessionMemory.FoldedMessageCount,
                    PreservedMessages = sessionMemory.ActiveMessages.Count - 1,
                });
            }

            if (preparation.CompactionResult != null)
            {
                await _hooks.OnPreCompactAsync(
                    new CompactHookContext(
                        CompactionLifecycleKind.Conversation,
                        automatic: true,
                        reason: preparation.InitialDecision.Reason,
                        preserveTailCount: _config.AutoCompactPreserveTailCount,
                        messageCount: _messages.Count),
                    ct);

                await ApplyCompactionResultAsync(preparation.CompactionResult, ct);
                await _hooks.OnPostCompactAsync(
                    new CompactHookContext(
                        CompactionLifecycleKind.Conversation,
                        automatic: true,
                        reason: preparation.InitialDecision.Reason,
                        preserveTailCount: _config.AutoCompactPreserveTailCount,
                        messageCount: _messages.Count,
                        conversationResult: preparation.CompactionResult),
                    ct);
                events.Add(new ContextCompactionEvent
                {
                    Mode = "compact",
                    Automatic = true,
                    Reason = preparation.InitialDecision.Reason,
                    RemovedMessages = preparation.CompactionResult.RemovedMessageCount,
                    PreservedMessages = preparation.CompactionResult.ActiveMessages.Count - 1,
                });
            }

            _consecutiveAutoCompactFailures = 0;
        }
        catch (Exception ex)
        {
            _consecutiveAutoCompactFailures++;
            events.Add(new ContextCompactionEvent
            {
                Mode = "failed",
                Automatic = true,
                Reason = ex.Message,
            });
        }

        return events;
    }

    private async Task ApplyCompactionResultAsync(
        ConversationCompactionResult result,
        CancellationToken ct)
    {
        await ApplyCheckpointAsync(result.SummaryMessage, result.ActiveMessages, ct);
    }

    private async Task ApplySessionMemoryCompactionResultAsync(
        SessionMemoryCompactionResult result,
        CancellationToken ct)
    {
        await ApplyCheckpointAsync(result.MemoryMessage, result.ActiveMessages, ct);
        if (_sessionMemoryFile != null)
            await _sessionMemoryFile.SaveAsync(result.SummaryText, ct);

        _contextProvider.SessionMemoryContent = result.SummaryText;
    }

    private async Task ApplyMicrocompactResultAsync(
        MicrocompactResult result,
        CancellationToken ct)
    {
        _messages.Clear();
        _messages.AddRange(result.UpdatedMessages);

        if (_journal != null)
        {
            await _journal.RecordMicrocompactAsync(
                result.Edits,
                _contextProvider.WorkingDirectory,
                _config.Model,
                ct);
        }
    }

    private async Task ApplyCheckpointAsync(
        ConversationMessage summaryMessage,
        IReadOnlyList<ConversationMessage> activeMessages,
        CancellationToken ct)
    {
        _messages.Clear();
        _messages.AddRange(activeMessages);

        if (_journal != null)
        {
            await _journal.RecordConversationCheckpointAsync(
                summaryMessage,
                activeMessages,
                _contextProvider.WorkingDirectory,
                _config.Model,
                ct);
        }
    }

    private async Task AddMessageAsync(
        ConversationMessage message,
        CancellationToken ct)
    {
        _messages.Add(message);
        if (_journal != null)
        {
            await _journal.AppendMessageAsync(
                message,
                _contextProvider.WorkingDirectory,
                _config.Model,
                ct);
        }
    }

    private async Task PersistRuntimeStateAsync(CancellationToken ct)
    {
        if (_journal == null)
            return;

        await _journal.UpdateSessionInfoAsync(
            _contextProvider.WorkingDirectory,
            _config.Model,
            ct);
    }

    private async Task CollectAssistantTurnAsync(
        List<ChatMessage> messages,
        ChatOptions options,
        AssistantTurnAccumulator turn,
        CancellationToken ct)
    {
        var response = await _chatClient.GetResponseAsync(messages, options, ct);
        ChatMessageConverter.PopulateAssistantTurn(response, turn);
    }

    private static IReadOnlyList<QueryEvent> EmitBufferedAssistantTurnEvents(
        AssistantTurnAccumulator turn)
    {
        var events = new List<QueryEvent>();
        foreach (var block in turn.ContentBlocks)
        {
            switch (block)
            {
                case TextBlock text:
                    events.Add(new TextDeltaEvent(text.Text));
                    break;
                case ThinkingBlock thinking:
                    events.Add(new ThinkingDeltaEvent(thinking.Text));
                    break;
                case ToolUseBlock toolUse:
                    events.Add(new ToolUseStartEvent(
                        toolUse.ToolUseId,
                        toolUse.Name,
                        toolUse.Input));
                    break;
            }
        }

        return events;
    }

    private async IAsyncEnumerable<QueryEvent> StreamAssistantTurnAsync(
        List<ChatMessage> messages,
        ChatOptions options,
        AssistantTurnAccumulator turn,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var updates = new List<ChatResponseUpdate>();

        await foreach (var update in _chatClient.GetStreamingResponseAsync(messages, options, ct))
        {
            updates.Add(update);

            if (updates.Count == 1 && update.ResponseId != null)
                yield return new MessageStartEvent(update.ResponseId);

            foreach (var evt in ChatMessageConverter.ProcessStreamingUpdate(update, turn))
                yield return evt;
        }

        ChatMessageConverter.FinalizeStreamingTurn(updates, turn);
    }

    private async Task EnsureSessionStartedAsync(CancellationToken ct)
    {
        if (_sessionStarted)
            return;

        _sessionStarted = true;
        _sessionEnded = false;

        await _hooks.OnSessionStartAsync(
            new SessionHookContext(
                SessionId,
                _contextProvider.WorkingDirectory,
                _config.Model,
                _sessionMetadata,
                _messages.Count),
            ct);
    }

    private async Task EndSessionAsync(bool dueToClear, CancellationToken ct)
    {
        if (!_sessionStarted || _sessionEnded)
            return;

        _sessionEnded = true;
        await _hooks.OnSessionEndAsync(
            new SessionEndHookContext(
                SessionId,
                _contextProvider.WorkingDirectory,
                _config.Model,
                _sessionMetadata,
                _messages.Count,
                dueToClear),
            ct);

        if (dueToClear)
            _sessionStarted = false;
    }

    private StopHookContext BuildStopHookContext(
        bool success,
        string? errorMessage,
        TimeSpan duration,
        int turnCount) =>
        new(
            SessionId,
            _contextProvider.WorkingDirectory,
            _config.Model,
            success,
            errorMessage,
            duration,
            turnCount,
            _totalUsage);

    public async ValueTask DisposeAsync()
    {
        await EndSessionAsync(dueToClear: false, CancellationToken.None);
    }

    private static TokenUsage ComputeTotalUsage(
        IReadOnlyList<ConversationMessage> messages)
    {
        var total = TokenUsage.Empty;
        foreach (var assistant in messages.OfType<AssistantMessage>())
        {
            if (assistant.Usage != null)
                total += assistant.Usage;
        }

        return total;
    }
}

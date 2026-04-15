using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Anthropic;
using ClaudeSharp.Core.Compaction;
using ClaudeSharp.Core.Hooks;
using ClaudeSharp.Core.Memory;
using ClaudeSharp.Core.Messages;
using ClaudeSharp.Core.Permissions;
using ClaudeSharp.Core.Storage;
using ClaudeSharp.Core.Tools;
using ApiContentBlockParam = Anthropic.Models.Messages.ContentBlockParam;
using ApiInputSchema = Anthropic.Models.Messages.InputSchema;
using ApiMessageCreateParams = Anthropic.Models.Messages.MessageCreateParams;
using ApiMessageParam = Anthropic.Models.Messages.MessageParam;
using ApiMessageParamContent = Anthropic.Models.Messages.MessageParamContent;
using ApiRole = Anthropic.Models.Messages.Role;
using ApiTextBlockParam = Anthropic.Models.Messages.TextBlockParam;
using ApiThinkingBlockParam = Anthropic.Models.Messages.ThinkingBlockParam;
using ApiThinkingConfigAdaptive = Anthropic.Models.Messages.ThinkingConfigAdaptive;
using ApiThinkingConfigDisabled = Anthropic.Models.Messages.ThinkingConfigDisabled;
using ApiThinkingConfigEnabled = Anthropic.Models.Messages.ThinkingConfigEnabled;
using ApiThinkingConfigParam = Anthropic.Models.Messages.ThinkingConfigParam;
using ApiTool = Anthropic.Models.Messages.Tool;
using ApiToolChoiceAuto = Anthropic.Models.Messages.ToolChoiceAuto;
using ApiToolResultBlockParam = Anthropic.Models.Messages.ToolResultBlockParam;
using ApiToolUnion = Anthropic.Models.Messages.ToolUnion;
using ApiToolUseBlockParam = Anthropic.Models.Messages.ToolUseBlockParam;

namespace ClaudeSharp.Core.Query;

/// <summary>
/// Runs the main conversation loop, including model calls, tool execution, and compaction.
/// </summary>
public class QueryEngine : IAsyncDisposable, IPlanModeController
{
    private readonly AnthropicClient _client;
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
    private readonly ConversationSessionMetadata _sessionMetadata;
    private readonly List<ConversationMessage> _messages;
    private TokenUsage _totalUsage;
    private int _consecutiveAutoCompactFailures;
    private bool _sessionStarted;
    private bool _sessionEnded;
    private PermissionMode _planModeResumeMode;

    public QueryEngine(
        AnthropicClient client,
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
        ConversationSessionMetadata? initialMetadata = null)
    {
        _client = client;
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
        _sessionMetadata = initialMetadata?.Clone() ?? journal?.Metadata ?? new ConversationSessionMetadata();
        if (_sessionMetadata.Mode is PermissionMode sessionMode)
            _contextProvider.PermissionContext.Mode = sessionMode;
        _messages = initialMessages?.ToList() ?? [];
        _totalUsage = initialUsage ?? ComputeTotalUsage(_messages);
        _planModeResumeMode = ResolveInitialPlanModeResumeMode();
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

    public bool IsPlanModeActive => CurrentPermissionMode == PermissionMode.Plan;

    public PermissionMode PlanModeResumeMode =>
        _planModeResumeMode == PermissionMode.Plan
            ? PermissionMode.Default
            : _planModeResumeMode;

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
            var apiMessages = ConvertToApiMessages(_messages);
            var toolDefs = await GetAvailableToolDefinitionsAsync();

            yield return new StatusEvent("calling_api");

            var assistantTurn = new AssistantTurnAccumulator();

            string? requestError = null;
            var request = new ApiMessageCreateParams
            {
                Model = _config.Model,
                MaxTokens = _config.MaxTokens,
                System = systemPrompt,
                Messages = apiMessages,
                Tools = ConvertToolDefinitions(toolDefs),
                ToolChoice = new ApiToolChoiceAuto(),
                Thinking = CreateThinkingConfig(),
            };

            if (_config.UseStreamingApi)
            {
                await using var streamEnumerator =
                    StreamAssistantTurnAsync(request, assistantTurn, ct).GetAsyncEnumerator(ct);

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
                    await CollectAssistantTurnAsync(request, assistantTurn, ct);
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
        Tools = GetAvailableTools(),
        Messages = _messages,
        CancellationToken = cancellationToken,
        MainLoopModel = _config.Model,
    };

    private async Task<string> BuildSystemPromptAsync()
    {
        return await _contextProvider.BuildSystemPromptAsync(
            GetAvailableTools(),
            _config);
    }

    /// <summary>
    /// Converts internal messages into Anthropic Messages API payloads.
    /// </summary>
    private static List<ApiMessageParam> ConvertToApiMessages(
        List<ConversationMessage> messages)
    {
        var result = new List<ApiMessageParam>();

        foreach (var msg in messages)
        {
            switch (msg)
            {
                case UserMessage userMsg:
                    var userContents = new List<ApiContentBlockParam>();
                    foreach (var block in userMsg.Content)
                    {
                        if (block is TextBlock tb)
                        {
                            userContents.Add(new ApiContentBlockParam(new ApiTextBlockParam(tb.Text)));
                        }
                        else if (block is ToolResultBlock trb)
                        {
                            userContents.Add(new ApiContentBlockParam(new ApiToolResultBlockParam(trb.ToolUseId)
                            {
                                Content = trb.Content,
                                IsError = trb.IsError,
                            }));
                        }
                    }

                    result.Add(new ApiMessageParam
                    {
                        Role = ApiRole.User,
                        Content = new ApiMessageParamContent(userContents),
                    });
                    break;

                case AssistantMessage assistantMsg:
                    var assistantContents = new List<ApiContentBlockParam>();
                    foreach (var block in assistantMsg.Content)
                    {
                        if (block is TextBlock tb)
                        {
                            assistantContents.Add(new ApiContentBlockParam(new ApiTextBlockParam(tb.Text)));
                        }
                        else if (block is ToolUseBlock tub)
                        {
                            var input = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                                tub.Input.GetRawText()) ?? new Dictionary<string, JsonElement>();

                            assistantContents.Add(new ApiContentBlockParam(new ApiToolUseBlockParam
                            {
                                ID = tub.ToolUseId,
                                Name = tub.Name,
                                Input = input,
                            }));
                        }
                        else if (block is ThinkingBlock thinking && !string.IsNullOrWhiteSpace(thinking.Signature))
                        {
                            assistantContents.Add(new ApiContentBlockParam(new ApiThinkingBlockParam
                            {
                                Signature = thinking.Signature,
                                Thinking = thinking.Text,
                            }));
                        }
                    }

                    if (assistantContents.Count == 0)
                        continue;

                    result.Add(new ApiMessageParam
                    {
                        Role = ApiRole.Assistant,
                        Content = new ApiMessageParamContent(assistantContents),
                    });
                    break;
            }
        }

        return result;
    }

    private ApiThinkingConfigParam? CreateThinkingConfig()
    {
        return _config.ThinkingMode switch
        {
            ThinkingMode.Disabled => new ApiThinkingConfigDisabled(),
            ThinkingMode.Enabled => new ApiThinkingConfigEnabled(_config.ThinkingBudgetTokens),
            ThinkingMode.Adaptive => new ApiThinkingConfigAdaptive(),
            _ => null,
        };
    }

    private static List<ApiToolUnion> ConvertToolDefinitions(
        IReadOnlyList<JsonElement> defs)
    {
        var tools = new List<ApiToolUnion>();

        foreach (var def in defs)
        {
            var schema = JsonSerializer.Deserialize<ApiInputSchema>(
                def.GetProperty("input_schema").GetRawText());

            if (schema == null)
                continue;

            var tool = new ApiTool
            {
                Name = def.GetProperty("name").GetString()!,
                Description = def.GetProperty("description").GetString(),
                InputSchema = schema,
                // ClaudeSharp validates tool inputs locally, so we can keep the
                // API-side schema mode non-strict and avoid Anthropic's strict
                // grammar compilation limits for larger tool sets.
                Strict = false,
            };

            tools.Add(new ApiToolUnion(tool));
        }

        return tools;
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
        var current = CurrentPermissionMode;
        if (mode == PermissionMode.Plan)
        {
            if (current != PermissionMode.Plan)
                _planModeResumeMode = current;
        }
        else
        {
            _planModeResumeMode = mode;
        }

        _contextProvider.PermissionContext.Mode = mode;
        _sessionMetadata.Mode = mode;
        if (_journal != null)
        {
            await _journal.UpdateMetadataAsync(
                metadata => metadata.Mode = mode,
                ct);
        }
    }

    public async Task<bool> EnterPlanModeAsync(CancellationToken ct = default)
    {
        if (IsPlanModeActive)
            return false;

        await SetPermissionModeAsync(PermissionMode.Plan, ct);
        return true;
    }

    public async Task<PermissionMode> ExitPlanModeAsync(CancellationToken ct = default)
    {
        var resumeMode = PlanModeResumeMode;
        if (IsPlanModeActive)
            await SetPermissionModeAsync(resumeMode, ct);

        return resumeMode;
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

    private PermissionMode CurrentPermissionMode =>
        _sessionMetadata.Mode ?? _contextProvider.PermissionContext.Mode;

    private PermissionMode ResolveInitialPlanModeResumeMode()
    {
        var current = CurrentPermissionMode;
        return current == PermissionMode.Plan
            ? PermissionMode.Default
            : current;
    }

    private IReadOnlyList<ITool> GetAvailableTools() =>
        _tools.GetEnabledTools(IsToolAllowedInCurrentMode);

    private Task<IReadOnlyList<JsonElement>> GetAvailableToolDefinitionsAsync() =>
        _tools.GetToolDefinitionsAsync(IsToolAllowedInCurrentMode);

    private bool IsToolAllowedInCurrentMode(ITool tool) =>
        !IsPlanModeActive || PlanModeToolPolicy.IsAllowedInPlanMode(tool.Name);

    private async Task CollectAssistantTurnAsync(
        ApiMessageCreateParams request,
        AssistantTurnAccumulator turn,
        CancellationToken ct)
    {
        var response = await _client.Messages.Create(request, ct);

        turn.MessageId = response.ID;
        turn.StopReason = response.StopReason?.Raw();
        turn.Usage = CreateTokenUsage(
            response.Usage.InputTokens,
            response.Usage.OutputTokens,
            response.Usage.CacheReadInputTokens,
            response.Usage.CacheCreationInputTokens);

        foreach (var content in response.Content)
        {
            if (content.TryPickText(out var textContent))
            {
                turn.ContentBlocks.Add(new TextBlock(textContent.Text));
            }
            else if (content.TryPickThinking(out var thinkingContent))
            {
                turn.ContentBlocks.Add(new ThinkingBlock(
                    thinkingContent.Thinking,
                    thinkingContent.Signature));
            }
            else if (content.TryPickToolUse(out var toolUseContent))
            {
                var inputJson = JsonSerializer.SerializeToElement(toolUseContent.Input);
                var block = new ToolUseBlock
                {
                    ToolUseId = toolUseContent.ID,
                    Name = toolUseContent.Name,
                    Input = inputJson,
                };

                turn.ContentBlocks.Add(block);
                turn.ToolUseBlocks.Add(block);
            }
        }
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
        ApiMessageCreateParams request,
        AssistantTurnAccumulator turn,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var blockBuilders = new Dictionary<long, StreamingContentBlockBuilder>();
        var rawResponse = await _client.Messages.WithRawResponse.CreateStreaming(request, ct);
        using var httpResponse = rawResponse.RawMessage;
        await using var stream = await httpResponse.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        string? eventName = null;
        var dataBuilder = new StringBuilder();

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line == null)
                break;

            if (line.StartsWith("event:", StringComparison.OrdinalIgnoreCase))
            {
                eventName = line[6..].Trim();
                continue;
            }

            if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                if (dataBuilder.Length > 0)
                    dataBuilder.Append('\n');

                dataBuilder.Append(line[5..].Trim());
                continue;
            }

            if (!string.IsNullOrWhiteSpace(line))
                continue;

            if (dataBuilder.Length == 0)
            {
                eventName = null;
                continue;
            }

            using var payloadDocument = JsonDocument.Parse(dataBuilder.ToString());
            var payload = payloadDocument.RootElement.Clone();
            var resolvedEventName = string.IsNullOrWhiteSpace(eventName) &&
                                    payload.TryGetProperty("type", out var typeProperty)
                ? typeProperty.GetString()
                : eventName;

            dataBuilder.Clear();
            eventName = null;

            if (string.Equals(resolvedEventName, "ping", StringComparison.OrdinalIgnoreCase))
                continue;

            if (string.Equals(resolvedEventName, "message_start", StringComparison.OrdinalIgnoreCase))
            {
                var message = payload.GetProperty("message");
                turn.MessageId = message.GetProperty("id").GetString();
                if (!string.IsNullOrWhiteSpace(turn.MessageId))
                    yield return new MessageStartEvent(turn.MessageId);
                continue;
            }

            if (string.Equals(resolvedEventName, "message_delta", StringComparison.OrdinalIgnoreCase))
            {
                if (payload.TryGetProperty("delta", out var delta))
                {
                    turn.StopReason = TryGetString(delta, "stop_reason");
                }

                if (payload.TryGetProperty("usage", out var usage))
                {
                    turn.Usage = CreateTokenUsage(
                        TryGetInt64(usage, "input_tokens"),
                        TryGetInt64(usage, "output_tokens"),
                        TryGetInt64(usage, "cache_read_input_tokens"),
                        TryGetInt64(usage, "cache_creation_input_tokens"));
                }

                continue;
            }

            if (string.Equals(resolvedEventName, "content_block_start", StringComparison.OrdinalIgnoreCase))
            {
                var index = payload.GetProperty("index").GetInt64();
                var builder = CreateStreamingBlockBuilder(payload);
                if (builder == null)
                    continue;

                blockBuilders[index] = builder;
                foreach (var initialEvent in builder.GetInitialEvents())
                    yield return initialEvent;
                continue;
            }

            if (string.Equals(resolvedEventName, "content_block_delta", StringComparison.OrdinalIgnoreCase))
            {
                var index = payload.GetProperty("index").GetInt64();
                if (!blockBuilders.TryGetValue(index, out var builder))
                    continue;

                foreach (var deltaEvent in builder.ApplyDelta(payload))
                    yield return deltaEvent;
                continue;
            }

            if (string.Equals(resolvedEventName, "content_block_stop", StringComparison.OrdinalIgnoreCase))
            {
                var index = payload.GetProperty("index").GetInt64();
                if (!blockBuilders.Remove(index, out var builder))
                    continue;

                foreach (var finalizedEvent in FinalizeStreamingBlock(turn, builder))
                    yield return finalizedEvent;

                continue;
            }
        }

        foreach (var (_, builder) in blockBuilders.OrderBy(pair => pair.Key))
        {
            foreach (var finalizedEvent in FinalizeStreamingBlock(turn, builder))
                yield return finalizedEvent;
        }
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

    private static TokenUsage CreateTokenUsage(
        long? inputTokens,
        long? outputTokens,
        long? cacheReadInputTokens,
        long? cacheCreationInputTokens) =>
        new()
        {
            InputTokens = (int)(inputTokens ?? 0),
            OutputTokens = (int)(outputTokens ?? 0),
            CacheReadInputTokens = (int)(cacheReadInputTokens ?? 0),
            CacheCreationInputTokens = (int)(cacheCreationInputTokens ?? 0),
        };

    private static StreamingContentBlockBuilder? CreateStreamingBlockBuilder(
        JsonElement payload)
    {
        var contentBlock = payload.GetProperty("content_block");
        var blockType = contentBlock.GetProperty("type").GetString();

        if (string.Equals(blockType, "text", StringComparison.Ordinal))
            return new TextStreamingBlockBuilder(TryGetString(contentBlock, "text") ?? string.Empty);

        if (string.Equals(blockType, "thinking", StringComparison.Ordinal))
        {
            return new ThinkingStreamingBlockBuilder(
                TryGetString(contentBlock, "thinking") ?? string.Empty,
                TryGetString(contentBlock, "signature"));
        }

        if (string.Equals(blockType, "tool_use", StringComparison.Ordinal))
        {
            var initialInput = contentBlock.TryGetProperty("input", out var input)
                ? input.Clone()
                : JsonSerializer.SerializeToElement(new { });

            return new ToolUseStreamingBlockBuilder(
                contentBlock.GetProperty("id").GetString() ?? string.Empty,
                contentBlock.GetProperty("name").GetString() ?? string.Empty,
                initialInput);
        }

        return null;
    }

    private static IReadOnlyList<QueryEvent> FinalizeStreamingBlock(
        AssistantTurnAccumulator turn,
        StreamingContentBlockBuilder builder)
    {
        var finalizedBlock = builder.Build();
        if (finalizedBlock == null)
            return [];

        turn.ContentBlocks.Add(finalizedBlock);
        if (finalizedBlock is not ToolUseBlock toolUseBlock)
            return [];

        turn.ToolUseBlocks.Add(toolUseBlock);
        return
        [
            new ToolUseStartEvent(
                toolUseBlock.ToolUseId,
                toolUseBlock.Name,
                toolUseBlock.Input),
        ];
    }

    private sealed class AssistantTurnAccumulator
    {
        public string? MessageId { get; set; }
        public string? StopReason { get; set; }
        public TokenUsage? Usage { get; set; }
        public List<ContentBlock> ContentBlocks { get; } = [];
        public List<ToolUseBlock> ToolUseBlocks { get; } = [];
    }

    private abstract class StreamingContentBlockBuilder
    {
        public virtual IReadOnlyList<QueryEvent> GetInitialEvents() => [];

        public abstract IReadOnlyList<QueryEvent> ApplyDelta(JsonElement payload);

        public abstract ContentBlock? Build();
    }

    private sealed class TextStreamingBlockBuilder : StreamingContentBlockBuilder
    {
        private readonly StringBuilder _text;

        public TextStreamingBlockBuilder(string initialText)
        {
            _text = new StringBuilder(initialText ?? string.Empty);
        }

        public override IReadOnlyList<QueryEvent> GetInitialEvents() =>
            string.IsNullOrEmpty(_text.ToString())
                ? []
                : [new TextDeltaEvent(_text.ToString())];

        public override IReadOnlyList<QueryEvent> ApplyDelta(JsonElement payload)
        {
            if (!payload.TryGetProperty("delta", out var delta) ||
                !string.Equals(TryGetString(delta, "type"), "text_delta", StringComparison.Ordinal))
            {
                return [];
            }

            var text = TryGetString(delta, "text");
            if (string.IsNullOrEmpty(text))
                return [];

            _text.Append(text);
            return [new TextDeltaEvent(text)];
        }

        public override ContentBlock Build() => new TextBlock(_text.ToString());
    }

    private sealed class ThinkingStreamingBlockBuilder : StreamingContentBlockBuilder
    {
        private readonly StringBuilder _thinking;
        private string? _signature;

        public ThinkingStreamingBlockBuilder(string initialThinking, string? signature)
        {
            _thinking = new StringBuilder(initialThinking ?? string.Empty);
            _signature = signature;
        }

        public override IReadOnlyList<QueryEvent> GetInitialEvents() =>
            string.IsNullOrEmpty(_thinking.ToString())
                ? []
                : [new ThinkingDeltaEvent(_thinking.ToString())];

        public override IReadOnlyList<QueryEvent> ApplyDelta(JsonElement payload)
        {
            if (!payload.TryGetProperty("delta", out var delta))
                return [];

            var deltaType = TryGetString(delta, "type");
            if (string.Equals(deltaType, "thinking_delta", StringComparison.Ordinal))
            {
                var thinking = TryGetString(delta, "thinking");
                if (string.IsNullOrEmpty(thinking))
                    return [];

                _thinking.Append(thinking);
                return [new ThinkingDeltaEvent(thinking)];
            }

            if (string.Equals(deltaType, "signature_delta", StringComparison.Ordinal))
                _signature = TryGetString(delta, "signature");

            return [];
        }

        public override ContentBlock Build() =>
            new ThinkingBlock(_thinking.ToString(), _signature);
    }

    private sealed class ToolUseStreamingBlockBuilder : StreamingContentBlockBuilder
    {
        private readonly string _toolUseId;
        private readonly string _name;
        private readonly JsonElement _initialInput;
        private readonly StringBuilder _partialJson = new();

        public ToolUseStreamingBlockBuilder(
            string toolUseId,
            string name,
            JsonElement initialInput)
        {
            _toolUseId = toolUseId;
            _name = name;
            _initialInput = initialInput.Clone();
        }

        public override IReadOnlyList<QueryEvent> ApplyDelta(JsonElement payload)
        {
            if (!payload.TryGetProperty("delta", out var delta) ||
                !string.Equals(TryGetString(delta, "type"), "input_json_delta", StringComparison.Ordinal))
            {
                return [];
            }

            var partialJson = TryGetString(delta, "partial_json");
            if (!string.IsNullOrEmpty(partialJson))
            {
                _partialJson.Append(partialJson);
            }

            return [];
        }

        public override ContentBlock Build() =>
            new ToolUseBlock
            {
                ToolUseId = _toolUseId,
                Name = _name,
                Input = BuildInput(),
            };

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
                    // Fall through to the initial input if the stream is malformed.
                }
            }

            if (_initialInput.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null)
                return _initialInput.Clone();

            return JsonSerializer.SerializeToElement(new { });
        }
    }

    private static string? TryGetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) &&
        property.ValueKind != JsonValueKind.Null
            ? property.GetString()
            : null;

    private static long? TryGetInt64(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return property.TryGetInt64(out var value) ? value : null;
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

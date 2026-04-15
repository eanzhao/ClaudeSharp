using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Aexon.Core.Compaction;
using Aexon.Core.Hooks;
using Aexon.Core.Memory;
using Aexon.Core.Messages;
using Aexon.Core.Permissions;
using Aexon.Core.Storage;
using Aexon.Core.Tools;
using Microsoft.Extensions.AI;
using ApiCacheControlEphemeral = Anthropic.Models.Messages.CacheControlEphemeral;
using ApiContentBlockParam = Anthropic.Models.Messages.ContentBlockParam;
using ApiInputJsonDelta = Anthropic.Models.Messages.InputJsonDelta;
using ApiInputSchema = Anthropic.Models.Messages.InputSchema;
using ApiMessageCreateParams = Anthropic.Models.Messages.MessageCreateParams;
using ApiMessageCreateParamsSystem = Anthropic.Models.Messages.MessageCreateParamsSystem;
using ApiMessageParam = Anthropic.Models.Messages.MessageParam;
using ApiMessageParamContent = Anthropic.Models.Messages.MessageParamContent;
using ApiRawContentBlockDeltaEvent = Anthropic.Models.Messages.RawContentBlockDeltaEvent;
using ApiRawContentBlockStartEvent = Anthropic.Models.Messages.RawContentBlockStartEvent;
using ApiRawContentBlockStopEvent = Anthropic.Models.Messages.RawContentBlockStopEvent;
using ApiRawMessageStreamEvent = Anthropic.Models.Messages.RawMessageStreamEvent;
using ApiRole = Anthropic.Models.Messages.Role;
using ApiTextBlockParam = Anthropic.Models.Messages.TextBlockParam;
using ApiThinkingBlockParam = Anthropic.Models.Messages.ThinkingBlockParam;
using ApiTool = Anthropic.Models.Messages.Tool;
using ApiToolChoiceAuto = Anthropic.Models.Messages.ToolChoiceAuto;
using ApiToolResultBlockParam = Anthropic.Models.Messages.ToolResultBlockParam;
using ApiToolUnion = Anthropic.Models.Messages.ToolUnion;
using ApiToolUseBlock = Anthropic.Models.Messages.ToolUseBlock;
using ApiToolUseBlockParam = Anthropic.Models.Messages.ToolUseBlockParam;

namespace Aexon.Core.Query;

/// <summary>
/// Runs the main conversation loop, including model calls, tool execution, and compaction.
/// </summary>
public class QueryEngine : IAsyncDisposable, IPlanModeController, IAwayModeController
{
    private const int MaxPromptCacheBreakpoints = 4;
    private const int PromptCacheLookbackWindowBlocks = 20;
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
    private readonly AttachmentRegistry _attachmentRegistry = new();
    private readonly List<ConversationMessage> _messages;
    private TokenUsage _totalUsage;
    private int _consecutiveAutoCompactFailures;
    private bool _sessionStarted;
    private bool _sessionEnded;
    private bool _promptCacheWarm;
    private PermissionMode _planModeResumeMode;
    private bool _awayModeActive;
    private DateTimeOffset? _awayEnteredAt;
    private string? _awayTriggerReason;

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
        if (_sessionMetadata.Mode is PermissionMode sessionMode)
            _contextProvider.PermissionContext.Mode = sessionMode;
        _messages = initialMessages?.ToList() ?? [];
        _totalUsage = initialUsage ?? ComputeTotalUsage(_messages);
        foreach (var attachment in _sessionMetadata.Attachments.Values)
            _attachmentRegistry.Register(attachment);
        _planModeResumeMode = ResolveInitialPlanModeResumeMode();
        _awayModeActive = _sessionMetadata.AwayEnteredAt.HasValue;
        _awayEnteredAt = _sessionMetadata.AwayEnteredAt;
        _awayTriggerReason = _sessionMetadata.AwayTriggerReason;
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

    public ApiQuotaStatus? LatestQuotaStatus { get; private set; }

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
            var promptCachingEnabled = SupportsPromptCaching(_config.Model);
            var hasSystemPromptCacheBreakpoint =
                promptCachingEnabled && !string.IsNullOrWhiteSpace(systemPrompt);
            var messagePromptCacheBreakpointBudget = promptCachingEnabled
                ? MaxPromptCacheBreakpoints - (hasSystemPromptCacheBreakpoint ? 1 : 0)
                : 0;
            var chatMessages = ChatMessageConverter.ToMeaiMessages(_messages);
            var toolDefs = await GetAvailableToolDefinitionsAsync();

            yield return new StatusEvent("calling_api");

            var assistantTurn = new AssistantTurnAccumulator();
            var chatOptions = BuildChatOptions(
                systemPrompt,
                toolDefs,
                promptCachingEnabled,
                hasSystemPromptCacheBreakpoint,
                messagePromptCacheBreakpointBudget);

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
                        UpdateQuotaStatus(TryGetQuotaStatus(ex));
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
                    UpdateQuotaStatus(TryGetQuotaStatus(ex));
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

            PromptCacheStatusEvent? promptCacheStatusEvent = null;
            if (assistantTurn.Usage != null)
            {
                _totalUsage += assistantTurn.Usage;
                promptCacheStatusEvent = CreatePromptCacheStatusEvent(
                    promptCachingEnabled,
                    assistantTurn.Usage);
            }

            if (!_config.UseStreamingApi)
            {
                foreach (var evt in EmitBufferedAssistantTurnEvents(assistantTurn))
                    yield return evt;
            }

            if (assistantTurn.MessageId != null || assistantTurn.Usage != null || assistantTurn.ContentBlocks.Count > 0)
                yield return new MessageEndEvent(assistantTurn.StopReason, assistantTurn.Usage);

            if (promptCacheStatusEvent != null)
                yield return promptCacheStatusEvent;

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
        IsNonInteractiveSession = _askUserQuestion == null,
        AskUserQuestionAsync = _askUserQuestion,
        MainLoopModel = _config.Model,
        MainLoopProvider = _config.Provider,
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
        List<ConversationMessage> messages,
        int promptCacheMessageBreakpointBudget)
    {
        var pendingMessages = new List<PendingApiMessage>();
        var promptCacheCandidates = new List<PromptCacheBlockCandidate>();
        var stableMessageCount = promptCacheMessageBreakpointBudget > 0
            ? GetStablePromptCacheMessageCount(messages)
            : 0;
        var contentPosition = 0;

        for (var messageIndex = 0; messageIndex < messages.Count; messageIndex++)
        {
            var msg = messages[messageIndex];
            var includeInStablePrefix = messageIndex < stableMessageCount;

            switch (msg)
            {
                case UserMessage userMsg:
                    var userContents = new List<ApiContentBlockParam>();
                    foreach (var block in userMsg.Content)
                    {
                        if (block is TextBlock tb)
                        {
                            var contentIndex = userContents.Count;
                            userContents.Add(new ApiContentBlockParam(new ApiTextBlockParam(tb.Text)));
                            Action? applyCacheControl = null;
                            if (!string.IsNullOrWhiteSpace(tb.Text))
                            {
                                applyCacheControl = () => userContents[contentIndex] =
                                    new ApiContentBlockParam(new ApiTextBlockParam(tb.Text)
                                    {
                                        CacheControl = CreatePromptCacheControl(),
                                    });
                            }

                            RegisterPromptCacheCandidate(
                                promptCacheCandidates,
                                includeInStablePrefix,
                                ref contentPosition,
                                applyCacheControl);
                        }
                        else if (block is ToolResultBlock trb)
                        {
                            var contentIndex = userContents.Count;
                            userContents.Add(new ApiContentBlockParam(new ApiToolResultBlockParam(trb.ToolUseId)
                            {
                                Content = trb.Content,
                                IsError = trb.IsError,
                            }));
                            RegisterPromptCacheCandidate(
                                promptCacheCandidates,
                                includeInStablePrefix,
                                ref contentPosition,
                                () => userContents[contentIndex] =
                                    new ApiContentBlockParam(new ApiToolResultBlockParam(trb.ToolUseId)
                                    {
                                        Content = trb.Content,
                                        IsError = trb.IsError,
                                        CacheControl = CreatePromptCacheControl(),
                                    }));
                        }
                    }

                    pendingMessages.Add(new PendingApiMessage(ApiRole.User, userContents));
                    break;

                case AssistantMessage assistantMsg:
                    var assistantContents = new List<ApiContentBlockParam>();
                    foreach (var block in assistantMsg.Content)
                    {
                        if (block is TextBlock tb)
                        {
                            var contentIndex = assistantContents.Count;
                            assistantContents.Add(new ApiContentBlockParam(new ApiTextBlockParam(tb.Text)));
                            Action? applyCacheControl = null;
                            if (!string.IsNullOrWhiteSpace(tb.Text))
                            {
                                applyCacheControl = () => assistantContents[contentIndex] =
                                    new ApiContentBlockParam(new ApiTextBlockParam(tb.Text)
                                    {
                                        CacheControl = CreatePromptCacheControl(),
                                    });
                            }

                            RegisterPromptCacheCandidate(
                                promptCacheCandidates,
                                includeInStablePrefix,
                                ref contentPosition,
                                applyCacheControl);
                        }
                        else if (block is ToolUseBlock tub)
                        {
                            var input = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                                tub.Input.GetRawText()) ?? new Dictionary<string, JsonElement>();
                            var contentIndex = assistantContents.Count;
                            assistantContents.Add(new ApiContentBlockParam(new ApiToolUseBlockParam
                            {
                                ID = tub.ToolUseId,
                                Name = tub.Name,
                                Input = input,
                            }));
                            RegisterPromptCacheCandidate(
                                promptCacheCandidates,
                                includeInStablePrefix,
                                ref contentPosition,
                                () => assistantContents[contentIndex] =
                                    new ApiContentBlockParam(new ApiToolUseBlockParam
                                    {
                                        ID = tub.ToolUseId,
                                        Name = tub.Name,
                                        Input = input,
                                        CacheControl = CreatePromptCacheControl(),
                                    }));
                        }
                        else if (block is ThinkingBlock thinking && !string.IsNullOrWhiteSpace(thinking.Signature))
                        {
                            assistantContents.Add(new ApiContentBlockParam(new ApiThinkingBlockParam
                            {
                                Signature = thinking.Signature,
                                Thinking = thinking.Text,
                            }));
                            RegisterPromptCacheCandidate(
                                promptCacheCandidates,
                                includeInStablePrefix,
                                ref contentPosition,
                                null);
                        }
                    }

                    if (assistantContents.Count == 0)
                        continue;

                    pendingMessages.Add(new PendingApiMessage(ApiRole.Assistant, assistantContents));
                    break;
            }
        }

        ApplyPromptCacheBreakpoints(promptCacheCandidates, promptCacheMessageBreakpointBudget);
        return pendingMessages
            .Select(message => new ApiMessageParam
            {
                Role = message.Role,
                Content = new ApiMessageParamContent(message.Content),
            })
            .ToList();
    }

    private static ApiMessageCreateParamsSystem CreateApiSystemPrompt(
        string systemPrompt,
        bool includeCacheBreakpoint)
    {
        if (!includeCacheBreakpoint)
            return new ApiMessageCreateParamsSystem(systemPrompt, null);

        var textBlock = new ApiTextBlockParam(systemPrompt)
        {
            CacheControl = CreatePromptCacheControl(),
        };

        return new ApiMessageCreateParamsSystem(new[] { textBlock }, null);
    }

    private static string? ToStopReason(ChatFinishReason? finishReason) =>
        NormalizeStopReason(finishReason?.Value);

    private static string? NormalizeStopReason(string? stopReason) =>
        stopReason switch
        {
            null => null,
            "tool_calls" => "tool_use",
            "stop" => "end_turn",
            "length" => "max_tokens",
            "content_filter" => "refusal",
            var value => value,
        };

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
                Strict = false,
            };

            tools.Add(new ApiToolUnion(tool));
        }

        return tools;
    }

    private ChatOptions BuildChatOptions(
        string systemPrompt,
        IReadOnlyList<JsonElement> toolDefs,
        bool promptCachingEnabled,
        bool hasSystemPromptCacheBreakpoint,
        int messagePromptCacheBreakpointBudget)
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
                [ChatClientPropertyKeys.ThinkingMode] = _config.ThinkingMode.ToString(),
                [ChatClientPropertyKeys.ThinkingBudgetTokens] = _config.ThinkingBudgetTokens,
                [ChatClientPropertyKeys.ApiRequestTimeout] = _config.ApiRequestTimeout,
                [ChatClientPropertyKeys.ApiMaxRetryCount] = _config.ApiMaxRetryCount,
                [ChatClientPropertyKeys.ApiRetryBaseDelay] = _config.ApiRetryBaseDelay,
                [ChatClientPropertyKeys.ApiRetryMaxDelay] = _config.ApiRetryMaxDelay,
                [ChatClientPropertyKeys.ApiRetryBackoffMultiplier] = _config.ApiRetryBackoffMultiplier,
            },
        };

        if (promptCachingEnabled)
        {
            options.RawRepresentationFactory = _ => new ApiMessageCreateParams
            {
                Model = _config.Model,
                MaxTokens = _config.MaxTokens,
                System = CreateApiSystemPrompt(systemPrompt, hasSystemPromptCacheBreakpoint),
                Messages = ConvertToApiMessages(_messages, messagePromptCacheBreakpointBudget),
            };
        }

        return options;
    }

    private void UpdateQuotaStatus(ApiQuotaStatus? status)
    {
        if (status != null)
            LatestQuotaStatus = status;
    }

    private static ApiQuotaStatus? ExtractQuotaStatus(AdditionalPropertiesDictionary? additionalProperties)
    {
        if (additionalProperties == null ||
            !additionalProperties.TryGetValue(ChatClientPropertyKeys.ApiQuotaStatus, out var value) ||
            value is not ApiQuotaStatus status)
        {
            return null;
        }

        return status;
    }

    private static ApiQuotaStatus? TryGetQuotaStatus(Exception exception)
    {
        return exception.Data.Contains(ChatClientPropertyKeys.ApiQuotaStatus)
            ? exception.Data[ChatClientPropertyKeys.ApiQuotaStatus] as ApiQuotaStatus
            : null;
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

    public IAttachmentRegistry Attachments => _attachmentRegistry;

    public async Task<Attachment> RegisterAttachmentAsync(
        string fileName,
        string mimeType,
        long sizeBytes,
        AttachmentSource source,
        string? sourcePath = null,
        CancellationToken ct = default)
    {
        var attachment = _attachmentRegistry.Register(fileName, mimeType, sizeBytes, source, sourcePath);
        _sessionMetadata.Attachments[attachment.Id] = attachment;
        if (_journal != null)
        {
            var snapshot = _sessionMetadata.Attachments.ToDictionary(kv => kv.Key, kv => kv.Value);
            await _journal.UpdateMetadataAsync(
                metadata =>
                {
                    metadata.Attachments.Clear();
                    foreach (var (id, a) in snapshot)
                        metadata.Attachments[id] = a;
                },
                ct);
        }

        return attachment;
    }

    public async Task<bool> RemoveAttachmentAsync(
        string attachmentId,
        CancellationToken ct = default)
    {
        if (!_attachmentRegistry.Remove(attachmentId))
            return false;

        _sessionMetadata.Attachments.Remove(attachmentId);
        if (_journal != null)
        {
            var snapshot = _sessionMetadata.Attachments.ToDictionary(kv => kv.Key, kv => kv.Value);
            await _journal.UpdateMetadataAsync(
                metadata =>
                {
                    metadata.Attachments.Clear();
                    foreach (var (id, a) in snapshot)
                        metadata.Attachments[id] = a;
                },
                ct);
        }

        return true;
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

    public bool IsAwayModeActive => _awayModeActive;

    public DateTimeOffset? AwayEnteredAt => _awayEnteredAt;

    public string? AwayTriggerReason => _awayTriggerReason;

    public async Task<bool> EnterAwayModeAsync(string triggerReason, CancellationToken ct = default)
    {
        if (_awayModeActive)
            return false;

        var now = DateTimeOffset.UtcNow;
        _awayModeActive = true;
        _awayEnteredAt = now;
        _awayTriggerReason = triggerReason;

        _sessionMetadata.AwayEnteredAt = now;
        _sessionMetadata.AwayTriggerReason = triggerReason;

        if (_journal != null)
        {
            await _journal.AppendMetadataEntryAsync(
                new TranscriptMetadataEntry(
                    "away-enter",
                    System.Text.Json.JsonSerializer.SerializeToElement(new
                    {
                        trigger = triggerReason,
                        entered_at = now,
                    }),
                    now),
                ct);
        }

        return true;
    }

    public async Task<SystemAwaySummaryMessage?> ExitAwayModeAsync(CancellationToken ct = default)
    {
        if (!_awayModeActive || !_awayEnteredAt.HasValue)
            return null;

        var now = DateTimeOffset.UtcNow;
        var enteredAt = _awayEnteredAt.Value;
        var duration = now - enteredAt;
        var reason = _awayTriggerReason ?? "unknown";

        var summary = new SystemAwaySummaryMessage
        {
            AwayEnteredAt = enteredAt,
            AwayExitedAt = now,
            TriggerReason = reason,
            SummaryText = $"User was away for {FormatDuration(duration)}. Reason: {reason}.",
        };

        await AddMessageAsync(summary, ct);

        if (_journal != null)
        {
            await _journal.AppendMetadataEntryAsync(
                new TranscriptMetadataEntry(
                    "away-exit",
                    System.Text.Json.JsonSerializer.SerializeToElement(new
                    {
                        exited_at = now,
                        duration_ms = (long)duration.TotalMilliseconds,
                    }),
                    now),
                ct);
        }

        _awayModeActive = false;
        _awayEnteredAt = null;
        _awayTriggerReason = null;
        _sessionMetadata.AwayEnteredAt = null;
        _sessionMetadata.AwayTriggerReason = null;

        return summary;
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalDays >= 1)
            return $"{(int)duration.TotalDays}d {duration.Hours}h {duration.Minutes}m";
        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours}h {duration.Minutes}m";
        return $"{(int)duration.TotalMinutes}m {duration.Seconds}s";
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
        IReadOnlyList<ChatMessage> messages,
        ChatOptions options,
        AssistantTurnAccumulator turn,
        CancellationToken ct)
    {
        var response = await _chatClient.GetResponseAsync(messages, options, ct);
        UpdateQuotaStatus(ExtractQuotaStatus(response.AdditionalProperties));
        ChatMessageConverter.PopulateAssistantTurn(response, turn);
        turn.StopReason = NormalizeStopReason(turn.StopReason);
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
        IReadOnlyList<ChatMessage> messages,
        ChatOptions options,
        AssistantTurnAccumulator turn,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var accumulator = new StreamingChatContentAccumulator(turn);
        var updates = new List<ChatResponseUpdate>();
        var emittedMessageStart = false;

        await foreach (var update in _chatClient.GetStreamingResponseAsync(messages, options, ct))
        {
            updates.Add(update);
            UpdateQuotaStatus(ExtractQuotaStatus(update.AdditionalProperties));

            IList<AIContent> contents = update.Contents ?? [];
            var fallbackEvents = accumulator.TrackRawUpdate(update.RawRepresentation, contents);
            var messageId = !string.IsNullOrWhiteSpace(update.MessageId)
                ? update.MessageId
                : update.ResponseId;

            if (!emittedMessageStart && !string.IsNullOrWhiteSpace(messageId))
            {
                turn.MessageId = messageId;
                emittedMessageStart = true;
                yield return new MessageStartEvent(messageId);
            }
            else if (!string.IsNullOrWhiteSpace(messageId))
            {
                turn.MessageId ??= messageId;
            }

            if (update.FinishReason is { } finishReason)
                turn.StopReason = ToStopReason(finishReason);

            foreach (var content in contents)
            {
                if (content is UsageContent usageContent)
                {
                    turn.Usage = ChatMessageConverter.ToTokenUsage(usageContent.Details);
                    continue;
                }

                foreach (var queryEvent in accumulator.Apply(content))
                    yield return queryEvent;
            }

            foreach (var fallbackEvent in fallbackEvents)
                yield return fallbackEvent;
        }

        foreach (var fallbackEvent in accumulator.FlushPendingToolUses())
            yield return fallbackEvent;
        accumulator.Flush();

        if (updates.Count == 0)
            yield break;

        var response = updates.ToChatResponse();
        turn.MessageId ??= response.Messages
            .Select(message => message.MessageId)
            .FirstOrDefault(messageId => !string.IsNullOrWhiteSpace(messageId))
            ?? response.ResponseId;
        turn.StopReason ??= NormalizeStopReason(response.FinishReason?.Value);
        turn.Usage ??= ChatMessageConverter.ToTokenUsage(response.Usage);
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

    private PromptCacheStatusEvent? CreatePromptCacheStatusEvent(
        bool promptCachingEnabled,
        TokenUsage usage)
    {
        if (!promptCachingEnabled)
        {
            _promptCacheWarm = false;
            return null;
        }

        var hasCacheActivity =
            usage.CacheReadInputTokens > 0 ||
            usage.CacheCreationInputTokens > 0;
        var breakDetected = _promptCacheWarm && usage.CacheReadInputTokens == 0;
        _promptCacheWarm = hasCacheActivity;

        return breakDetected
            ? new PromptCacheStatusEvent(usage, BreakDetected: true)
            : null;
    }

    private static ApiCacheControlEphemeral CreatePromptCacheControl() => new();

    private static int GetStablePromptCacheMessageCount(
        IReadOnlyList<ConversationMessage> messages)
    {
        if (messages.Count == 0)
            return 0;

        return messages[^1] is UserMessage user &&
               user.Content.Any(block => block is not ToolResultBlock)
            ? messages.Count - 1
            : messages.Count;
    }

    private static void RegisterPromptCacheCandidate(
        List<PromptCacheBlockCandidate> candidates,
        bool includeInStablePrefix,
        ref int contentPosition,
        Action? applyCacheControl)
    {
        if (!includeInStablePrefix)
            return;

        if (applyCacheControl != null)
            candidates.Add(new PromptCacheBlockCandidate(contentPosition, applyCacheControl));

        contentPosition++;
    }

    private static void ApplyPromptCacheBreakpoints(
        IReadOnlyList<PromptCacheBlockCandidate> candidates,
        int breakpointBudget)
    {
        if (breakpointBudget <= 0 || candidates.Count == 0)
            return;

        var current = candidates[^1];
        current.ApplyCacheControl();
        var applied = 1;

        while (applied < breakpointBudget && current.Position >= PromptCacheLookbackWindowBlocks)
        {
            var lowerBound = current.Position - (PromptCacheLookbackWindowBlocks - 1);
            var next = candidates.FirstOrDefault(candidate =>
                candidate.Position >= lowerBound &&
                candidate.Position < current.Position);
            if (next == null)
                break;

            next.ApplyCacheControl();
            current = next;
            applied++;
        }
    }

    private static bool SupportsPromptCaching(string? modelOrAlias)
    {
        var descriptor = ClaudeModelCatalog.TryResolve(modelOrAlias);
        return descriptor?.StableId is
            "claude-3-5-haiku" or
            "claude-3-7-sonnet" or
            "claude-haiku-4-5" or
            "claude-sonnet-4" or
            "claude-sonnet-4-5" or
            "claude-sonnet-4-6" or
            "claude-opus-4" or
            "claude-opus-4-1" or
            "claude-opus-4-5" or
            "claude-opus-4-6";
    }

    private sealed record PendingApiMessage(ApiRole Role, List<ApiContentBlockParam> Content);

    private sealed record PromptCacheBlockCandidate(int Position, Action ApplyCacheControl);

    private sealed class StreamingChatContentAccumulator
    {
        private readonly AssistantTurnAccumulator _turn;
        private readonly Dictionary<long, PendingToolUse> _pendingToolUses = [];
        private BufferedContentKind _bufferKind;
        private StringBuilder? _textBuffer;
        private StringBuilder? _thinkingBuffer;
        private string? _thinkingSignature;

        public StreamingChatContentAccumulator(AssistantTurnAccumulator turn)
        {
            _turn = turn;
        }

        public IReadOnlyList<QueryEvent> Apply(AIContent content)
        {
            switch (content)
            {
                case TextContent text:
                    EnsureBuffer(BufferedContentKind.Text);
                    if (!string.IsNullOrEmpty(text.Text))
                        _textBuffer!.Append(text.Text);
                    return string.IsNullOrEmpty(text.Text) ? [] : [new TextDeltaEvent(text.Text)];

                case TextReasoningContent reasoning:
                    EnsureBuffer(BufferedContentKind.Thinking);
                    if (!string.IsNullOrEmpty(reasoning.Text))
                        _thinkingBuffer!.Append(reasoning.Text);
                    if (!string.IsNullOrWhiteSpace(reasoning.ProtectedData))
                        _thinkingSignature = reasoning.ProtectedData;
                    return string.IsNullOrEmpty(reasoning.Text) ? [] : [new ThinkingDeltaEvent(reasoning.Text)];

                case FunctionCallContent functionCall:
                    Flush();
                    var toolUse = new ToolUseBlock
                    {
                        ToolUseId = functionCall.CallId ?? string.Empty,
                        Name = functionCall.Name ?? string.Empty,
                        Input = functionCall.Arguments != null
                            ? JsonSerializer.SerializeToElement(functionCall.Arguments)
                            : JsonSerializer.SerializeToElement(new { }),
                    };

                    MarkToolUseEmitted(toolUse.ToolUseId);
                    return AddToolUse(toolUse);

                default:
                    return [];
            }
        }

        public IReadOnlyList<QueryEvent> TrackRawUpdate(
            object? rawRepresentation,
            IList<AIContent> contents)
        {
            if (rawRepresentation is ApiRawMessageStreamEvent streamEvent)
                rawRepresentation = streamEvent.Value;

            switch (rawRepresentation)
            {
                case ApiRawContentBlockStartEvent start
                    when start.ContentBlock.Value is ApiToolUseBlock toolUse:
                    _pendingToolUses[start.Index] = new PendingToolUse(
                        start.Index,
                        toolUse.ID,
                        toolUse.Name,
                        toolUse.Input is { Count: > 0 }
                            ? JsonSerializer.SerializeToElement(toolUse.Input)
                            : JsonSerializer.SerializeToElement(new { }));
                    break;

                case ApiRawContentBlockDeltaEvent delta
                    when delta.Delta.Value is ApiInputJsonDelta inputJsonDelta
                         && _pendingToolUses.TryGetValue(delta.Index, out var pending):
                    pending.PartialJson.Append(inputJsonDelta.PartialJson);
                    break;

                case ApiRawContentBlockStopEvent stop:
                    var hasFunctionCall = contents.OfType<FunctionCallContent>().Any();
                    if (!_pendingToolUses.Remove(stop.Index, out var stoppedPending) || hasFunctionCall)
                        break;

                    return FinalizePendingToolUse(stoppedPending);
            }

            return [];
        }

        public IReadOnlyList<QueryEvent> FlushPendingToolUses()
        {
            if (_pendingToolUses.Count == 0)
                return [];

            var events = new List<QueryEvent>();
            foreach (var pending in _pendingToolUses.OrderBy(pair => pair.Key).Select(pair => pair.Value))
            {
                foreach (var queryEvent in FinalizePendingToolUse(pending))
                    events.Add(queryEvent);
            }

            _pendingToolUses.Clear();
            return events;
        }

        public void Flush()
        {
            switch (_bufferKind)
            {
                case BufferedContentKind.Text when _textBuffer is { Length: > 0 }:
                    _turn.ContentBlocks.Add(new TextBlock(_textBuffer.ToString()));
                    break;

                case BufferedContentKind.Thinking
                    when _thinkingBuffer is { Length: > 0 } || !string.IsNullOrWhiteSpace(_thinkingSignature):
                    _turn.ContentBlocks.Add(new ThinkingBlock(
                        _thinkingBuffer?.ToString() ?? string.Empty,
                        _thinkingSignature));
                    break;
            }

            _bufferKind = BufferedContentKind.None;
            _textBuffer = null;
            _thinkingBuffer = null;
            _thinkingSignature = null;
        }

        private void EnsureBuffer(BufferedContentKind kind)
        {
            if (_bufferKind == kind)
                return;

            Flush();
            _bufferKind = kind;

            if (kind == BufferedContentKind.Text)
                _textBuffer = new StringBuilder();
            else if (kind == BufferedContentKind.Thinking)
                _thinkingBuffer = new StringBuilder();
        }

        private IReadOnlyList<QueryEvent> FinalizePendingToolUse(PendingToolUse pending)
        {
            if (pending.Emitted)
                return [];

            return AddToolUse(new ToolUseBlock
            {
                ToolUseId = pending.ToolUseId,
                Name = pending.Name,
                Input = pending.BuildInput(),
            });
        }

        private IReadOnlyList<QueryEvent> AddToolUse(ToolUseBlock toolUse)
        {
            _turn.ContentBlocks.Add(toolUse);
            _turn.ToolUseBlocks.Add(toolUse);
            return
            [
                new ToolUseStartEvent(
                    toolUse.ToolUseId,
                    toolUse.Name,
                    toolUse.Input),
            ];
        }

        private void MarkToolUseEmitted(string toolUseId)
        {
            foreach (var pending in _pendingToolUses.Values)
            {
                if (string.Equals(pending.ToolUseId, toolUseId, StringComparison.Ordinal))
                {
                    pending.Emitted = true;
                    break;
                }
            }
        }

        private enum BufferedContentKind
        {
            None,
            Text,
            Thinking,
        }

        private sealed class PendingToolUse(
            long index,
            string toolUseId,
            string name,
            JsonElement initialInput)
        {
            public long Index { get; } = index;
            public string ToolUseId { get; } = toolUseId;
            public string Name { get; } = name;
            public JsonElement InitialInput { get; } = initialInput.Clone();
            public StringBuilder PartialJson { get; } = new();
            public bool Emitted { get; set; }

            public JsonElement BuildInput()
            {
                if (PartialJson.Length > 0)
                {
                    try
                    {
                        using var document = JsonDocument.Parse(PartialJson.ToString());
                        return document.RootElement.Clone();
                    }
                    catch
                    {
                        // Fall back to the initial payload if the partial JSON is malformed.
                    }
                }

                return InitialInput.Clone();
            }
        }
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

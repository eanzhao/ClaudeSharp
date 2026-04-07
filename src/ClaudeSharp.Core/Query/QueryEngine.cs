using System.Runtime.CompilerServices;
using System.Text.Json;
using Anthropic;
using ClaudeSharp.Core.Compaction;
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
/// 查询引擎 — 对应 Claude Code 的 QueryEngine 类 (QueryEngine.ts)
///
/// 这是 Claude Code 最核心的组件，负责：
/// 1. 管理对话消息链
/// 2. 构建系统提示 (包含工具定义和上下文)
/// 3. 调用 Anthropic API
/// 4. 实现 agentic loop: API调用 → 工具执行 → 结果回传 → 再次调用
/// 5. 权限检查和用户审批流程
/// 6. Token 使用统计
/// </summary>
public class QueryEngine
{
    private readonly AnthropicClient _client;
    private readonly ToolRegistry _tools;
    private readonly IToolRuntime _toolRuntime;
    private readonly IConversationCompactor _compactor;
    private readonly QueryEngineConfig _config;
    private readonly Context.ContextProvider _contextProvider;
    private readonly IConversationJournal? _journal;
    private readonly ConversationSessionMetadata _sessionMetadata;
    private readonly List<ConversationMessage> _messages;
    private TokenUsage _totalUsage;

    public QueryEngine(
        AnthropicClient client,
        ToolRegistry tools,
        IPermissionChecker permissions,
        QueryEngineConfig config,
        Context.ContextProvider contextProvider,
        IToolRuntime? toolRuntime = null,
        IConversationCompactor? compactor = null,
        IConversationJournal? journal = null,
        IReadOnlyList<ConversationMessage>? initialMessages = null,
        TokenUsage? initialUsage = null,
        ConversationSessionMetadata? initialMetadata = null)
    {
        _client = client;
        _tools = tools;
        _toolRuntime = toolRuntime ?? new StreamingToolExecutor(tools, permissions);
        _compactor = compactor ?? new HeuristicConversationCompactor();
        _config = config;
        _contextProvider = contextProvider;
        _journal = journal;
        _sessionMetadata = initialMetadata?.Clone() ?? journal?.Metadata ?? new ConversationSessionMetadata();
        _messages = initialMessages?.ToList() ?? [];
        _totalUsage = initialUsage ?? ComputeTotalUsage(_messages);
    }

    /// <summary>获取当前对话消息</summary>
    public IReadOnlyList<ConversationMessage> Messages => _messages;

    /// <summary>获取累计 token 用量</summary>
    public TokenUsage TotalUsage => _totalUsage;

    /// <summary>当前模型</summary>
    public string CurrentModel => _config.Model;

    /// <summary>切换模型（支持常见别名）</summary>
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
    /// 提交用户消息并获取事件流。
    /// 当前依然是“按块返回”，不是 token 级流式，但已经接到新版 Anthropic SDK。
    /// </summary>
    public async IAsyncEnumerable<QueryEvent> SubmitMessageAsync(
        string userInput,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var startTime = DateTimeOffset.UtcNow;
        var turnCount = 0;

        await AddMessageAsync(UserMessage.FromText(userInput), ct);

        while (!ct.IsCancellationRequested)
        {
            turnCount++;

            var systemPrompt = await BuildSystemPromptAsync();
            var apiMessages = ConvertToApiMessages(_messages);
            var toolDefs = await _tools.GetToolDefinitionsAsync();

            yield return new StatusEvent("calling_api");

            var contentBlocks = new List<ContentBlock>();
            var toolUseBlocks = new List<ToolUseBlock>();
            TokenUsage? responseUsage = null;
            string? stopReason = null;

            Anthropic.Models.Messages.Message? response = null;
            string? requestError = null;
            try
            {
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

                response = await _client.Messages.Create(request, ct);
            }
            catch (Exception ex)
            {
                requestError = ex.Message;
            }

            if (requestError != null)
            {
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

            if (response != null)
            {
                stopReason = response.StopReason?.Raw();
                responseUsage = new TokenUsage
                {
                    InputTokens = (int)response.Usage.InputTokens,
                    OutputTokens = (int)response.Usage.OutputTokens,
                    CacheReadInputTokens = (int)(response.Usage.CacheReadInputTokens ?? 0),
                    CacheCreationInputTokens = (int)(response.Usage.CacheCreationInputTokens ?? 0),
                };
                _totalUsage += responseUsage;

                foreach (var content in response.Content)
                {
                    if (content.TryPickText(out var textContent))
                    {
                        var block = new TextBlock(textContent.Text);
                        contentBlocks.Add(block);
                        yield return new TextDeltaEvent(textContent.Text);
                    }
                    else if (content.TryPickThinking(out var thinkingContent))
                    {
                        var block = new ThinkingBlock(
                            thinkingContent.Thinking,
                            thinkingContent.Signature);
                        contentBlocks.Add(block);
                        yield return new ThinkingDeltaEvent(thinkingContent.Thinking);
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

                        contentBlocks.Add(block);
                        toolUseBlocks.Add(block);

                        yield return new ToolUseStartEvent(
                            toolUseContent.ID,
                            toolUseContent.Name,
                            inputJson);
                    }
                }

                yield return new MessageEndEvent(stopReason, responseUsage);
            }

            await AddMessageAsync(new AssistantMessage
            {
                Content = contentBlocks,
                StopReason = stopReason,
                Usage = responseUsage,
            }, ct);

            if (toolUseBlocks.Count == 0)
                break;

            if (turnCount >= _config.MaxTurns)
            {
                yield return new TextDeltaEvent(
                    $"\n\n[Reached maximum turn limit of {_config.MaxTurns}]");
                break;
            }

            await foreach (var evt in ExecuteToolCallsAsync(toolUseBlocks, ct))
            {
                yield return evt;
            }
        }

        yield return new QueryCompleteEvent
        {
            Success = true,
            Duration = DateTimeOffset.UtcNow - startTime,
            TurnCount = turnCount,
            TotalUsage = _totalUsage,
        };
    }

    /// <summary>
    /// 执行工具调用 — 对应 query.ts 中的 runTools + toolOrchestration.ts
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
        MainLoopModel = _config.Model,
    };

    private async Task<string> BuildSystemPromptAsync()
    {
        return await _contextProvider.BuildSystemPromptAsync(
            _tools.GetEnabledTools(),
            _config);
    }

    /// <summary>
    /// 将内部消息模型转换为官方 Anthropic C# SDK 的 Messages API 结构。
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
                Strict = true,
            };

            tools.Add(new ApiToolUnion(tool));
        }

        return tools;
    }

    /// <summary>清除对话历史</summary>
    public async Task ClearMessagesAsync(CancellationToken ct = default)
    {
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
        var result = _compactor.Compact(_messages, preserveTailCount);
        if (result == null)
            return null;

        _messages.Clear();
        _messages.AddRange(result.ActiveMessages);

        if (_journal != null)
        {
            await _journal.RecordConversationCheckpointAsync(
                result.SummaryMessage,
                result.ActiveMessages,
                _contextProvider.WorkingDirectory,
                _config.Model,
                ct);
        }

        return result;
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

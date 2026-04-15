using System.Text.Json;
using Aexon.Core.Messages;
using Microsoft.Extensions.AI;

namespace Aexon.Core.Query;

/// <summary>
/// Bidirectional conversion between internal <see cref="ConversationMessage"/> and MEAI <see cref="ChatMessage"/> types.
/// </summary>
internal static class ChatMessageConverter
{
    public static List<ChatMessage> ToMeaiMessages(IReadOnlyList<ConversationMessage> messages)
    {
        var result = new List<ChatMessage>();

        foreach (var msg in messages)
        {
            switch (msg)
            {
                case TombstoneMessage:
                    continue;

                case UserMessage userMsg:
                    result.Add(ToMeaiUserMessage(userMsg));
                    break;

                case AssistantMessage assistantMsg:
                    var chatMsg = ToMeaiAssistantMessage(assistantMsg);
                    if (chatMsg != null)
                        result.Add(chatMsg);
                    break;
            }
        }

        return result;
    }

    private static ChatMessage ToMeaiUserMessage(UserMessage msg)
    {
        var contents = new List<AIContent>();

        foreach (var block in msg.Content)
        {
            switch (block)
            {
                case TextBlock tb:
                    contents.Add(new TextContent(tb.Text));
                    break;

                case ToolResultBlock trb:
                    contents.Add(new FunctionResultContent(trb.ToolUseId, trb.Content)
                    {
                        Exception = trb.IsError ? new InvalidOperationException(trb.Content) : null,
                    });
                    break;
            }
        }

        return new ChatMessage(ChatRole.User, contents);
    }

    private static ChatMessage? ToMeaiAssistantMessage(AssistantMessage msg)
    {
        var contents = new List<AIContent>();

        foreach (var block in msg.Content)
        {
            switch (block)
            {
                case TextBlock tb:
                    contents.Add(new TextContent(tb.Text));
                    break;

                case ToolUseBlock tub:
                    {
                        var args = JsonSerializer.Deserialize<Dictionary<string, object?>>(
                            tub.Input.GetRawText());
                        contents.Add(new FunctionCallContent(tub.ToolUseId, tub.Name, args));
                        break;
                    }

                case ThinkingBlock thinking when !string.IsNullOrWhiteSpace(thinking.Signature):
                    contents.Add(new TextReasoningContent(thinking.Text)
                    {
                        ProtectedData = thinking.Signature,
                    });
                    break;

                case ThinkingBlock thinking:
                    contents.Add(new TextReasoningContent(thinking.Text));
                    break;
            }
        }

        return contents.Count == 0 ? null : new ChatMessage(ChatRole.Assistant, contents);
    }

    public static void PopulateAssistantTurn(
        ChatResponse response,
        AssistantTurnAccumulator turn)
    {
        turn.MessageId = response.ResponseId;
        turn.StopReason = response.FinishReason?.Value;
        turn.Usage = ToTokenUsage(response.Usage);

        foreach (var message in response.Messages)
        {
            if (message.Role != ChatRole.Assistant)
                continue;

            PopulateFromContents(message.Contents, turn);
        }
    }

    public static IReadOnlyList<QueryEvent> ProcessStreamingUpdate(
        ChatResponseUpdate update,
        AssistantTurnAccumulator turn)
    {
        var events = new List<QueryEvent>();

        if (update.ResponseId != null)
            turn.MessageId = update.ResponseId;

        if (update.FinishReason != null)
            turn.StopReason = update.FinishReason.Value.Value;

        foreach (var content in update.Contents)
        {
            switch (content)
            {
                case TextContent tc when !string.IsNullOrEmpty(tc.Text):
                    events.Add(new TextDeltaEvent(tc.Text));
                    break;

                case TextReasoningContent trc when !string.IsNullOrEmpty(trc.Text):
                    events.Add(new ThinkingDeltaEvent(trc.Text));
                    break;

                case TextReasoningContent:
                    break;

                case FunctionCallContent fcc:
                    {
                        var inputJson = fcc.Arguments != null
                            ? JsonSerializer.SerializeToElement(fcc.Arguments)
                            : JsonSerializer.SerializeToElement(new { });

                        var block = new ToolUseBlock
                        {
                            ToolUseId = fcc.CallId ?? string.Empty,
                            Name = fcc.Name ?? string.Empty,
                            Input = inputJson,
                        };
                        turn.ContentBlocks.Add(block);
                        turn.ToolUseBlocks.Add(block);
                        events.Add(new ToolUseStartEvent(block.ToolUseId, block.Name, block.Input));
                        break;
                    }
            }
        }

        return events;
    }

    public static void FinalizeStreamingTurn(
        IReadOnlyList<ChatResponseUpdate> updates,
        AssistantTurnAccumulator turn)
    {
        var chatResponse = updates.ToChatResponse();
        turn.Usage = ToTokenUsage(chatResponse.Usage);

        if (chatResponse.FinishReason != null)
            turn.StopReason ??= chatResponse.FinishReason.Value.Value;

        foreach (var message in chatResponse.Messages)
        {
            if (message.Role != ChatRole.Assistant)
                continue;

            foreach (var content in message.Contents)
            {
                switch (content)
                {
                    case TextContent tc when !string.IsNullOrEmpty(tc.Text):
                        if (!turn.ContentBlocks.OfType<TextBlock>().Any())
                            turn.ContentBlocks.Insert(0, new TextBlock(tc.Text));
                        break;

                    case TextReasoningContent trc when !string.IsNullOrEmpty(trc.Text):
                        if (!turn.ContentBlocks.OfType<ThinkingBlock>().Any())
                            turn.ContentBlocks.Insert(0, new ThinkingBlock(trc.Text, trc.ProtectedData));
                        break;

                    case FunctionCallContent fcc:
                        if (!turn.ToolUseBlocks.Any(b => b.ToolUseId == fcc.CallId))
                        {
                            var inputJson = fcc.Arguments != null
                                ? JsonSerializer.SerializeToElement(fcc.Arguments)
                                : JsonSerializer.SerializeToElement(new { });

                            var block = new ToolUseBlock
                            {
                                ToolUseId = fcc.CallId ?? string.Empty,
                                Name = fcc.Name ?? string.Empty,
                                Input = inputJson,
                            };
                            turn.ContentBlocks.Add(block);
                            turn.ToolUseBlocks.Add(block);
                        }

                        break;
                }
            }
        }
    }

    public static List<AITool> ToMeaiTools(IReadOnlyList<JsonElement> toolDefinitions)
    {
        var tools = new List<AITool>();

        foreach (var def in toolDefinitions)
        {
            var name = def.GetProperty("name").GetString()!;
            var description = def.GetProperty("description").GetString();
            var inputSchema = def.GetProperty("input_schema");

            tools.Add(AIFunctionFactory.CreateDeclaration(name, description, inputSchema));
        }

        return tools;
    }

    public static TokenUsage ToTokenUsage(UsageDetails? usage)
    {
        if (usage == null)
            return TokenUsage.Empty;

        long cacheRead = usage.CachedInputTokenCount ?? 0;
        long cacheCreation = 0;

        if (usage.AdditionalCounts != null &&
            usage.AdditionalCounts.TryGetValue("CacheCreationInputTokens", out var ccVal))
        {
            cacheCreation = ccVal;
        }

        var directInput = Math.Max(0, (usage.InputTokenCount ?? 0) - cacheRead - cacheCreation);

        return new TokenUsage
        {
            InputTokens = ToInt32(directInput),
            OutputTokens = ToInt32(usage.OutputTokenCount ?? 0),
            CacheReadInputTokens = ToInt32(cacheRead),
            CacheCreationInputTokens = ToInt32(cacheCreation),
        };
    }

    private static void PopulateFromContents(IList<AIContent> contents, AssistantTurnAccumulator turn)
    {
        foreach (var content in contents)
        {
            switch (content)
            {
                case TextContent tc:
                    turn.ContentBlocks.Add(new TextBlock(tc.Text ?? string.Empty));
                    break;

                case TextReasoningContent trc:
                    turn.ContentBlocks.Add(new ThinkingBlock(
                        trc.Text ?? string.Empty,
                        trc.ProtectedData));
                    break;

                case FunctionCallContent fcc:
                    {
                        var inputJson = fcc.Arguments != null
                            ? JsonSerializer.SerializeToElement(fcc.Arguments)
                            : JsonSerializer.SerializeToElement(new { });

                        var block = new ToolUseBlock
                        {
                            ToolUseId = fcc.CallId ?? string.Empty,
                            Name = fcc.Name ?? string.Empty,
                            Input = inputJson,
                        };
                        turn.ContentBlocks.Add(block);
                        turn.ToolUseBlocks.Add(block);
                        break;
                    }
            }
        }
    }

    private static int ToInt32(long value) =>
        value switch
        {
            > int.MaxValue => int.MaxValue,
            < int.MinValue => int.MinValue,
            _ => (int)value,
        };

}

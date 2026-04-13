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

        var textParts = new List<string>();
        var thinkingParts = new List<string>();
        string? thinkingSignature = null;

        foreach (var update in updates)
        {
            foreach (var content in update.Contents)
            {
                switch (content)
                {
                    case TextContent tc when !string.IsNullOrEmpty(tc.Text):
                        textParts.Add(tc.Text);
                        break;

                    case TextReasoningContent trc when !string.IsNullOrEmpty(trc.Text):
                        thinkingParts.Add(trc.Text);
                        break;

                    case TextReasoningContent trc when !string.IsNullOrEmpty(trc.ProtectedData):
                        thinkingSignature = trc.ProtectedData;
                        break;
                }
            }
        }

        if (textParts.Count > 0 && !turn.ContentBlocks.OfType<TextBlock>().Any())
            turn.ContentBlocks.Insert(0, new TextBlock(string.Concat(textParts)));

        if (thinkingParts.Count > 0 && !turn.ContentBlocks.OfType<ThinkingBlock>().Any())
            turn.ContentBlocks.Insert(0, new ThinkingBlock(string.Concat(thinkingParts), thinkingSignature));
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

        int cacheRead = (int)(usage.CachedInputTokenCount ?? 0);
        int cacheCreation = 0;

        if (usage.AdditionalCounts != null &&
            usage.AdditionalCounts.TryGetValue("CacheCreationInputTokens", out var ccVal))
        {
            cacheCreation = (int)ccVal;
        }

        return new TokenUsage
        {
            InputTokens = (int)(usage.InputTokenCount ?? 0),
            OutputTokens = (int)(usage.OutputTokenCount ?? 0),
            CacheReadInputTokens = cacheRead,
            CacheCreationInputTokens = cacheCreation,
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

}

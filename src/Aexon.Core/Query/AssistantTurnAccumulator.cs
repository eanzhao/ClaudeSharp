using Aexon.Core.Messages;

namespace Aexon.Core.Query;

internal sealed class AssistantTurnAccumulator
{
    public string? MessageId { get; set; }
    public string? StopReason { get; set; }
    public TokenUsage? Usage { get; set; }
    public List<ContentBlock> ContentBlocks { get; } = [];
    public List<ToolUseBlock> ToolUseBlocks { get; } = [];
}

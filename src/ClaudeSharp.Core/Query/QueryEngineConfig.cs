namespace ClaudeSharp.Core.Query;

/// <summary>
/// 查询引擎配置 — 对应 Claude Code 的 QueryEngineConfig (QueryEngine.ts:130-173)
/// </summary>
public class QueryEngineConfig
{
    /// <summary>使用的模型名称</summary>
    public string Model { get; set; } = ClaudeModels.DefaultMainModel;

    /// <summary>最大输出 tokens</summary>
    public int MaxTokens { get; set; } = 16384;

    /// <summary>最大工具使用轮次 (防止死循环)</summary>
    public int MaxTurns { get; set; } = 50;

    /// <summary>最大预算 (USD)</summary>
    public double? MaxBudgetUsd { get; set; }

    /// <summary>系统提示 (如果自定义)</summary>
    public string? CustomSystemPrompt { get; set; }

    /// <summary>追加的系统提示</summary>
    public string? AppendSystemPrompt { get; set; }

    /// <summary>是否启用思考模式</summary>
    public ThinkingMode ThinkingMode { get; set; } = ThinkingMode.Adaptive;

    /// <summary>思考模式 token 预算</summary>
    public int ThinkingBudgetTokens { get; set; } = 10240;
}

/// <summary>
/// 思考模式配置 — 对应 Claude Code 的 ThinkingConfig (utils/thinking.ts)
/// </summary>
public enum ThinkingMode
{
    /// <summary>禁用思考</summary>
    Disabled,

    /// <summary>始终启用思考</summary>
    Enabled,

    /// <summary>自适应 (模型自行决定)</summary>
    Adaptive,
}

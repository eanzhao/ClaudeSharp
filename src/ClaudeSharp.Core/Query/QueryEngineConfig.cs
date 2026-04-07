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

    /// <summary>是否启用自动上下文压缩</summary>
    public bool EnableAutoCompact { get; set; } = true;

    /// <summary>估算的模型上下文窗口大小</summary>
    public int ApproxContextWindowTokens { get; set; } = 200_000;

    /// <summary>给自动压缩预留的安全 buffer</summary>
    public int AutoCompactBufferTokens { get; set; } = 12_000;

    /// <summary>自动压缩时至少保留多少条最近消息原文</summary>
    public int AutoCompactPreserveTailCount { get; set; } = 8;

    /// <summary>是否优先使用 session memory compact 作为自动压缩的温和路径</summary>
    public bool EnableSessionMemoryCompact { get; set; } = true;

    /// <summary>自动压缩触发前，至少要有多少条消息</summary>
    public int AutoCompactMinimumMessageCount { get; set; } = 12;

    /// <summary>字符转 token 的粗略估算比率</summary>
    public int ApproxCharsPerToken { get; set; } = 4;

    /// <summary>进入 warning 级上下文压力的阈值比例</summary>
    public double AutoCompactWarningRatio { get; set; } = 0.72;

    /// <summary>进入 blocking 级上下文压力的阈值比例</summary>
    public double AutoCompactBlockingRatio { get; set; } = 0.82;

    /// <summary>自动压缩连续失败的熔断上限</summary>
    public int AutoCompactFailureLimit { get; set; } = 3;
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

namespace Aexon.Core.Query;

/// <summary>
/// Represents options for query engine.
/// </summary>
public class QueryEngineConfig
{
    /// <summary>
    /// Gets the active provider for the main loop.
    /// </summary>
    public AiProvider Provider { get; set; } = AiProvider.Anthropic;

    /// <summary>
    /// Gets model.
    /// </summary>
    public string Model { get; set; } = ClaudeModels.DefaultMainModel;

    /// <summary>
    /// Gets max tokens.
    /// </summary>
    public int MaxTokens { get; set; } = 16384;

    /// <summary>
    /// Gets max turns.
    /// </summary>
    public int MaxTurns { get; set; } = 50;

    /// <summary>
    /// Gets max budget usd.
    /// </summary>
    public double? MaxBudgetUsd { get; set; }

    /// <summary>
    /// Gets custom system prompt.
    /// </summary>
    public string? CustomSystemPrompt { get; set; }

    /// <summary>
    /// Gets or sets text appended after the built-in system prompt.
    /// </summary>
    public string? AppendSystemPrompt { get; set; }

    /// <summary>
    /// Gets thinking mode.
    /// </summary>
    public ThinkingMode ThinkingMode { get; set; } = ThinkingMode.Adaptive;

    /// <summary>
    /// Gets thinking budget tokens.
    /// </summary>
    public int ThinkingBudgetTokens { get; set; } = 10240;

    /// <summary>
    /// Gets use streaming api.
    /// </summary>
    public bool UseStreamingApi { get; set; }

    /// <summary>
    /// Gets enable auto compact.
    /// </summary>
    public bool EnableAutoCompact { get; set; } = true;

    /// <summary>
    /// Gets approx context window tokens.
    /// </summary>
    public int ApproxContextWindowTokens { get; set; } = 200_000;

    /// <summary>
    /// Gets auto compact buffer tokens.
    /// </summary>
    public int AutoCompactBufferTokens { get; set; } = 12_000;

    /// <summary>
    /// Gets auto compact preserve tail count.
    /// </summary>
    public int AutoCompactPreserveTailCount { get; set; } = 8;

    /// <summary>
    /// Gets enable session memory compact.
    /// </summary>
    public bool EnableSessionMemoryCompact { get; set; } = true;

    /// <summary>
    /// Gets auto compact minimum message count.
    /// </summary>
    public int AutoCompactMinimumMessageCount { get; set; } = 12;

    /// <summary>
    /// Gets approx chars per token.
    /// </summary>
    public int ApproxCharsPerToken { get; set; } = 4;

    /// <summary>
    /// Gets auto compact warning ratio.
    /// </summary>
    public double AutoCompactWarningRatio { get; set; } = 0.72;

    /// <summary>
    /// Gets auto compact blocking ratio.
    /// </summary>
    public double AutoCompactBlockingRatio { get; set; } = 0.82;

    /// <summary>
    /// Gets auto compact failure limit.
    /// </summary>
    public int AutoCompactFailureLimit { get; set; } = 3;

    /// <summary>
    /// Gets the per-request timeout applied to chat provider calls.
    /// </summary>
    public TimeSpan ApiRequestTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Gets the maximum number of retry attempts after the initial API call fails.
    /// </summary>
    public int ApiMaxRetryCount { get; set; } = 3;

    /// <summary>
    /// Gets the initial delay used by exponential retry backoff.
    /// </summary>
    public TimeSpan ApiRetryBaseDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets the maximum delay used by exponential retry backoff.
    /// </summary>
    public TimeSpan ApiRetryMaxDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets the exponential backoff multiplier applied on each retry.
    /// </summary>
    public double ApiRetryBackoffMultiplier { get; set; } = 2.0;
}

/// <summary>
/// Defines thinking mode values.
/// </summary>
public enum ThinkingMode
{
    /// <summary>
    /// Disables the feature.
    /// </summary>
    Disabled,

    /// <summary>
    /// Always enables the feature.
    /// </summary>
    Enabled,

    /// <summary>
    /// Lets the model decide when to use the feature.
    /// </summary>
    Adaptive,
}

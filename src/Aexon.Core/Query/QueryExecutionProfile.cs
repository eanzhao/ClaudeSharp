namespace Aexon.Core.Query;

/// <summary>
/// Represents the resolved execution profile for a query request.
/// </summary>
public sealed record QueryExecutionProfile(
    QueryEffortLevel Effort,
    string ModelId,
    int MaxOutputTokens,
    ThinkingMode ThinkingMode,
    int? ThinkingBudgetTokens,
    ClaudeModelDescriptor? ClaudeModel,
    QueryPromptDetail PromptDetail)
{
    public ClaudeModelFamily? ClaudeFamily => ClaudeModel?.Family;

    public bool SupportsExtendedThinking =>
        ThinkingMode != ThinkingMode.Disabled;
}

/// <summary>
/// Defines how much prompt detail should be emitted for the current request.
/// </summary>
public enum QueryPromptDetail
{
    Compact,
    Standard,
    Detailed,
}

/// <summary>
/// Resolves model-aware runtime settings from the active query-engine config.
/// </summary>
public static class QueryExecutionProfileResolver
{
    public static QueryExecutionProfile Resolve(QueryEngineConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var descriptor = ClaudeModelCatalog.TryResolve(config.Model);
        var modelCap = ResolveModelMaxOutputTokens(descriptor);
        var effortCap = config.Effort == QueryEffortLevel.Fast
            ? 4096
            : int.MaxValue;
        var effectiveMaxTokens = Math.Max(1, Math.Min(config.MaxTokens, Math.Min(modelCap, effortCap)));
        var promptDetail = ResolvePromptDetail(config.Effort, descriptor?.Family);
        var supportsExtendedThinking = SupportsExtendedThinking(descriptor);
        var effectiveThinkingMode = ResolveThinkingMode(
            config.ThinkingMode,
            config.Effort,
            supportsExtendedThinking);
        var effectiveThinkingBudget = effectiveThinkingMode == ThinkingMode.Disabled
            ? (int?)null
            : Math.Min(
                Math.Max(1, config.ThinkingBudgetTokens),
                Math.Max(1, effectiveMaxTokens));

        return new QueryExecutionProfile(
            config.Effort,
            config.Model,
            effectiveMaxTokens,
            effectiveThinkingMode,
            effectiveThinkingBudget,
            descriptor,
            promptDetail);
    }

    private static QueryPromptDetail ResolvePromptDetail(
        QueryEffortLevel effort,
        ClaudeModelFamily? family)
    {
        if (effort == QueryEffortLevel.Fast || family == ClaudeModelFamily.Haiku)
            return QueryPromptDetail.Compact;

        if (effort == QueryEffortLevel.Thorough || family == ClaudeModelFamily.Opus)
            return QueryPromptDetail.Detailed;

        return QueryPromptDetail.Standard;
    }

    private static ThinkingMode ResolveThinkingMode(
        ThinkingMode configuredMode,
        QueryEffortLevel effort,
        bool supportsExtendedThinking)
    {
        if (!supportsExtendedThinking || effort == QueryEffortLevel.Fast)
            return ThinkingMode.Disabled;

        if (effort == QueryEffortLevel.Thorough && configuredMode != ThinkingMode.Disabled)
            return ThinkingMode.Enabled;

        return configuredMode;
    }

    private static bool SupportsExtendedThinking(ClaudeModelDescriptor? descriptor) =>
        descriptor?.StableId switch
        {
            "claude-3-7-sonnet" => true,
            "claude-sonnet-4" => true,
            "claude-sonnet-4-5" => true,
            "claude-sonnet-4-6" => true,
            "claude-opus-4" => true,
            "claude-opus-4-1" => true,
            "claude-opus-4-5" => true,
            "claude-opus-4-6" => true,
            _ => false,
        };

    private static int ResolveModelMaxOutputTokens(ClaudeModelDescriptor? descriptor) =>
        descriptor?.StableId switch
        {
            "claude-3-5-haiku" => 8192,
            "claude-3-5-sonnet" => 8192,
            "claude-haiku-4-5" => 8192,
            "claude-3-7-sonnet" => 64000,
            "claude-sonnet-4" => 64000,
            "claude-sonnet-4-5" => 64000,
            "claude-sonnet-4-6" => 64000,
            "claude-opus-4" => 32000,
            "claude-opus-4-1" => 32000,
            "claude-opus-4-5" => 32000,
            "claude-opus-4-6" => 32000,
            _ => int.MaxValue,
        };
}

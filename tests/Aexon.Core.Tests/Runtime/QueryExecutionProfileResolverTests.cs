using Aexon.Core.Query;

namespace Aexon.Core.Tests.Runtime;

public sealed class QueryExecutionProfileResolverTests
{
    [Fact]
    public void Resolve_UsesCompactProfileForHaiku()
    {
        var profile = QueryExecutionProfileResolver.Resolve(
            new QueryEngineConfig
            {
                Model = "haiku",
                MaxTokens = 16384,
                Effort = QueryEffortLevel.Balanced,
                ThinkingMode = ThinkingMode.Enabled,
            });

        Assert.Equal(QueryPromptDetail.Compact, profile.PromptDetail);
        Assert.Equal(8192, profile.MaxOutputTokens);
        Assert.Equal(ThinkingMode.Disabled, profile.ThinkingMode);
        Assert.Equal("claude-haiku-4-5", profile.ClaudeModel?.StableId);
    }

    [Fact]
    public void Resolve_AppliesFastAndThoroughEffortAdjustments()
    {
        var fastProfile = QueryExecutionProfileResolver.Resolve(
            new QueryEngineConfig
            {
                Model = "claude-sonnet-4-6",
                MaxTokens = 16384,
                Effort = QueryEffortLevel.Fast,
                ThinkingMode = ThinkingMode.Adaptive,
            });
        var thoroughProfile = QueryExecutionProfileResolver.Resolve(
            new QueryEngineConfig
            {
                Model = "claude-sonnet-4-6",
                MaxTokens = 16384,
                Effort = QueryEffortLevel.Thorough,
                ThinkingMode = ThinkingMode.Adaptive,
            });

        Assert.Equal(4096, fastProfile.MaxOutputTokens);
        Assert.Equal(ThinkingMode.Disabled, fastProfile.ThinkingMode);
        Assert.Equal(QueryPromptDetail.Compact, fastProfile.PromptDetail);

        Assert.Equal(16384, thoroughProfile.MaxOutputTokens);
        Assert.Equal(ThinkingMode.Enabled, thoroughProfile.ThinkingMode);
        Assert.Equal(QueryPromptDetail.Detailed, thoroughProfile.PromptDetail);
        Assert.Equal(10240, thoroughProfile.ThinkingBudgetTokens);
    }
}

using Aexon.Core.Query;

namespace Aexon.Core.Tests.Foundations;

/// <summary>
/// Contains tests for query Model Catalog.
/// </summary>
public sealed class QueryModelCatalogTests
{
    [Fact]
    public void ResolveModelOrAlias_ReturnsDefaultModelWhenInputIsEmpty()
    {
        Assert.Equal(ClaudeModels.DefaultMainModel, ClaudeModelCatalog.ResolveModelOrAlias(null));
        Assert.Equal(ClaudeModels.DefaultMainModel, ClaudeModelCatalog.ResolveModelOrAlias("   "));
    }

    [Theory]
    [InlineData("sonnet", "claude-sonnet-4-6")]
    [InlineData("opus", "claude-opus-4-6")]
    [InlineData("haiku", "claude-haiku-4-5")]
    [InlineData("claude-sonnet-4", "claude-sonnet-4")]
    public void ResolveModelOrAlias_ResolvesKnownAliases(string input, string expected)
    {
        Assert.Equal(expected, ClaudeModelCatalog.ResolveModelOrAlias(input));
        Assert.Equal(expected, ClaudeModels.Resolve(input));
    }

    [Fact]
    public void TryResolve_ReturnsDescriptorWithProviderIds()
    {
        var descriptor = ClaudeModelCatalog.TryResolve("opus");

        Assert.NotNull(descriptor);
        Assert.Equal("claude-opus-4-6", descriptor!.StableId);
        Assert.Equal("claude-opus-4-6", descriptor.ProviderIds.FirstParty);
        Assert.Contains("opus", descriptor.Aliases);
        Assert.Equal(ClaudeModelFamily.Opus, descriptor.Family);
    }

    [Fact]
    public void Canonicalize_UsesLongestMatchFirst()
    {
        Assert.Equal("claude-opus-4-6", ClaudeModelCatalog.Canonicalize("anthropic.claude-opus-4-6-v1"));
        Assert.Equal("claude-sonnet-4-6", ClaudeModelCatalog.Canonicalize("claude-sonnet-4-6"));
    }

    [Fact]
    public void ResolveModelOrAlias_ReturnsInputWhenNoModelMatches()
    {
        Assert.Equal("made-up-model", ClaudeModelCatalog.ResolveModelOrAlias("made-up-model"));
        Assert.Null(ClaudeModelCatalog.TryResolve("made-up-model"));
        Assert.Null(ClaudeModelCatalog.Canonicalize("made-up-model"));
    }

    [Fact]
    public void AllAndCommonAliasesExposeExpectedValues()
    {
        Assert.Contains(ClaudeModelCatalog.All, model => model.StableId == "claude-sonnet-4-6");
        Assert.Equal(["sonnet", "opus", "haiku"], ClaudeModels.CommonAliases);
    }

    [Fact]
    public void DescriptorMatchersIncludeProviderIds()
    {
        var descriptor = ClaudeModelCatalog.All.First(model => model.StableId == "claude-sonnet-4-6");
        var matchers = descriptor.GetMatchers().ToList();

        Assert.Contains(matchers, item => item.MatchText == "claude-sonnet-4-6");
        Assert.Contains(matchers, item => item.MatchText == descriptor.ProviderIds.Bedrock);
        Assert.Contains(matchers, item => item.MatchText == descriptor.ProviderIds.Vertex);
    }

    [Fact]
    public void EveryDescriptorResolvesThroughStableIdSourceIdAliasesAndProviderIds()
    {
        foreach (var descriptor in ClaudeModelCatalog.All)
        {
            Assert.Equal(descriptor.StableId, ClaudeModelCatalog.ResolveModelOrAlias(descriptor.StableId));
            Assert.Equal(descriptor.StableId, ClaudeModelCatalog.ResolveModelOrAlias(descriptor.SourceCanonicalId));
            Assert.Equal(descriptor.StableId, ClaudeModelCatalog.ResolveModelOrAlias(descriptor.ProviderIds.FirstParty));
            Assert.Equal(descriptor.StableId, ClaudeModelCatalog.ResolveModelOrAlias(descriptor.ProviderIds.Bedrock));
            Assert.Equal(descriptor.StableId, ClaudeModelCatalog.ResolveModelOrAlias(descriptor.ProviderIds.Vertex));
            Assert.Equal(descriptor.StableId, ClaudeModelCatalog.ResolveModelOrAlias(descriptor.ProviderIds.Foundry));

            foreach (var alias in descriptor.Aliases)
                Assert.Equal(descriptor.StableId, ClaudeModelCatalog.ResolveModelOrAlias(alias));
        }
    }
}

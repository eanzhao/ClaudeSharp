using Aexon.Core.Providers;
using Aexon.Core.Query;

namespace Aexon.Core.Tests.Providers;

/// <summary>
/// Contains tests for provider Capability Router.
/// </summary>
public sealed class ProviderCapabilityRouterTests
{
    [Fact]
    public void Resolve_MapsAliasesToFirstPartyModelsAndWebSearch()
    {
        var router = new DefaultProviderCapabilityRouter();

        var route = router.Resolve("sonnet");

        Assert.Equal("claude-sonnet-4-6", route.StableModelId);
        Assert.Equal(ProviderKind.FirstParty, route.Provider);
        Assert.Equal("claude-sonnet-4-6", route.ProviderModelId);
        Assert.True(route.Supports(ModelCapability.WebFetch));
        Assert.True(route.Supports(ModelCapability.WebSearch));
    }

    [Fact]
    public void Resolve_UsesBedrockIdAndDisablesWebSearch()
    {
        var router = new DefaultProviderCapabilityRouter();
        var descriptor = ClaudeModelCatalog.All.First(model => model.StableId == "claude-sonnet-4-6");

        var route = router.Resolve(descriptor.ProviderIds.Bedrock);

        Assert.Equal(ProviderKind.Bedrock, route.Provider);
        Assert.Equal(descriptor.ProviderIds.Bedrock, route.ProviderModelId);
        Assert.True(route.Supports(ModelCapability.WebFetch));
        Assert.False(route.Supports(ModelCapability.WebSearch));
    }

    [Fact]
    public void Resolve_EnablesVertexWebSearchForClaude4Series()
    {
        var router = new DefaultProviderCapabilityRouter();
        var descriptor = ClaudeModelCatalog.All.First(model => model.StableId == "claude-opus-4-6");

        var route = router.Resolve(descriptor.ProviderIds.Vertex);

        Assert.Equal(ProviderKind.Vertex, route.Provider);
        Assert.Equal(descriptor.ProviderIds.Vertex, route.ProviderModelId);
        Assert.True(route.Supports(ModelCapability.WebSearch));
    }

    [Fact]
    public void Resolve_Sonnet46_IncludesStreamingAndPromptCaching()
    {
        var router = new DefaultProviderCapabilityRouter();

        var route = router.Resolve("sonnet");

        Assert.True(route.Supports(ModelCapability.Streaming));
        Assert.True(route.Supports(ModelCapability.ToolCalling));
        Assert.True(route.Supports(ModelCapability.PromptCaching));
        Assert.True(route.Supports(ModelCapability.ImageInput));
        Assert.True(route.Supports(ModelCapability.Reasoning));
    }

    [Fact]
    public void Resolve_Sonnet46_PricingMatchesCatalog()
    {
        var route = new DefaultProviderCapabilityRouter().Resolve("sonnet");
        var descriptor = ClaudeModelCatalog.TryResolve(route.StableModelId);

        Assert.NotNull(descriptor);
        Assert.NotNull(descriptor!.Pricing);
        Assert.Equal(3.0m, descriptor.Pricing!.InputPer1M);
        Assert.Equal(15.0m, descriptor.Pricing.OutputPer1M);
        Assert.Equal(0.3m, descriptor.Pricing.CacheReadPer1M);
        Assert.Equal(3.75m, descriptor.Pricing.CacheWritePer1M);
    }

    [Fact]
    public void Resolve_Haiku35_DoesNotIncludeReasoning()
    {
        var route = new DefaultProviderCapabilityRouter().Resolve("claude-3-5-haiku");

        Assert.False(route.Supports(ModelCapability.Reasoning));
        Assert.True(route.Supports(ModelCapability.Streaming));
    }
}

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
}

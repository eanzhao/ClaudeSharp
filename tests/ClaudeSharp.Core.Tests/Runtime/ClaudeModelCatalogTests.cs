using ClaudeSharp.Core.Query;

namespace ClaudeSharp.Core.Tests.Runtime;

public sealed class ClaudeModelCatalogTests
{
    [Fact]
    public void ResolveModelOrAlias_ReturnsDefaultForEmptyInput()
    {
        Assert.Equal(ClaudeModels.DefaultMainModel, ClaudeModelCatalog.ResolveModelOrAlias(null));
        Assert.Equal(ClaudeModels.DefaultMainModel, ClaudeModelCatalog.ResolveModelOrAlias("   "));
    }

    [Theory]
    [InlineData("sonnet", "claude-sonnet-4-6")]
    [InlineData("opus", "claude-opus-4-6")]
    [InlineData("haiku", "claude-haiku-4-5")]
    [InlineData("claude-opus-4-6", "claude-opus-4-6")]
    public void ResolveModelOrAlias_ResolvesKnownInputs(string input, string expected)
    {
        Assert.Equal(expected, ClaudeModelCatalog.ResolveModelOrAlias(input));
        Assert.Equal(expected, ClaudeModels.Resolve(input));
    }

    [Fact]
    public void ResolveModelOrAlias_PassesThroughUnknownInputs()
    {
        Assert.Equal("custom-model", ClaudeModelCatalog.ResolveModelOrAlias("custom-model"));
        Assert.Null(ClaudeModelCatalog.Canonicalize("custom-model"));
    }

    [Fact]
    public void TryResolve_ReturnsDescriptorForStableAndProviderIds()
    {
        var descriptor = ClaudeModelCatalog.TryResolve("us.anthropic.claude-opus-4-20250514-v1:0");

        Assert.NotNull(descriptor);
        Assert.Equal("claude-opus-4", descriptor!.StableId);
        Assert.Equal("claude-opus-4-20250514", descriptor.ProviderIds.FirstParty);
        Assert.Contains("opus-4", descriptor.Aliases);
    }

    [Fact]
    public void AllAndCommonAliasesExposeExpectedData()
    {
        Assert.Contains(ClaudeModelCatalog.All, model => model.StableId == "claude-sonnet-4-6");
        Assert.Equal(["sonnet", "opus", "haiku"], ClaudeModels.CommonAliases);
    }
}

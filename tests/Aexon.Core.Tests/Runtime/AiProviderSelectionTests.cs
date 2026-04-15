using Aexon.Core.Query;

namespace Aexon.Core.Tests.Runtime;

/// <summary>
/// Contains tests for AI provider/model selection.
/// </summary>
public sealed class AiProviderSelectionTests
{
    [Fact]
    public void ResolveSessionTarget_PrefersPersistedProviderForCustomOpenAiModel()
    {
        var target = AiProviderSelection.ResolveSessionTarget(
            providerFlag: null,
            modelOverride: null,
            resumedProvider: "openai",
            resumedModel: "qwen2.5-coder");

        Assert.Equal(AiProvider.OpenAI, target.Provider);
        Assert.Equal("qwen2.5-coder", target.Model);
    }

    [Fact]
    public void ResolveSessionTarget_UsesProviderDefaultWhenResumeProviderChangesWithoutModelOverride()
    {
        var target = AiProviderSelection.ResolveSessionTarget(
            providerFlag: "openai",
            modelOverride: null,
            resumedProvider: "anthropic",
            resumedModel: "claude-opus-4-6");

        Assert.Equal(AiProvider.OpenAI, target.Provider);
        Assert.Equal("gpt-4o", target.Model);
    }

    [Fact]
    public void ResolveSessionTarget_UsesOllamaDefaultWhenProviderIsExplicit()
    {
        var target = AiProviderSelection.ResolveSessionTarget(
            providerFlag: "ollama",
            modelOverride: null,
            resumedProvider: "anthropic",
            resumedModel: "claude-sonnet-4-6");

        Assert.Equal(AiProvider.Ollama, target.Provider);
        Assert.Equal("qwen3:4b", target.Model);
    }

    [Fact]
    public void DetectProvider_PrefersExplicitAnthropicAliasesOverOpenAiFallback()
    {
        var provider = AiProviderSelection.DetectProvider(
            providerHint: null,
            model: "opus",
            fallbackProvider: AiProvider.OpenAI);

        Assert.Equal(AiProvider.Anthropic, provider);
    }

    [Fact]
    public void TryParse_RecognizesOllamaProvider()
    {
        var success = AiProviderSelection.TryParse("ollama", out var provider);

        Assert.True(success);
        Assert.Equal(AiProvider.Ollama, provider);
        Assert.Equal("ollama", AiProviderSelection.ToStorageValue(provider));
    }
}

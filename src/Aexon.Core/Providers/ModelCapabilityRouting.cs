using Aexon.Core.Query;

namespace Aexon.Core.Providers;

[Flags]
/// <summary>
/// Defines model capability values.
/// </summary>
public enum ModelCapability
{
    None = 0,
    WebFetch = 1 << 0,
    WebSearch = 1 << 1,
}

/// <summary>
/// Defines provider kind values.
/// </summary>
public enum ProviderKind
{
    Unknown,
    FirstParty,
    Bedrock,
    Vertex,
    Foundry,
}

/// <summary>
/// Represents provider model route.
/// </summary>
public sealed record ProviderModelRoute(
    string Input,
    string StableModelId,
    string ProviderModelId,
    ProviderKind Provider,
    ClaudeModelFamily Family,
    ModelCapability Capabilities)
{
    public bool Supports(ModelCapability capability) =>
        (Capabilities & capability) == capability;
}

/// <summary>
/// Defines the contract for provider capability router.
/// </summary>
public interface IProviderCapabilityRouter
{
    ProviderModelRoute Resolve(string? modelOrAlias);

    bool Supports(string? modelOrAlias, ModelCapability capability);
}

/// <summary>
/// Provides default provider capability router.
/// </summary>
public sealed class DefaultProviderCapabilityRouter : IProviderCapabilityRouter
{
    public ProviderModelRoute Resolve(string? modelOrAlias)
    {
        var resolvedInput = string.IsNullOrWhiteSpace(modelOrAlias)
            ? ClaudeModels.DefaultMainModel
            : modelOrAlias.Trim();

        var descriptor = ClaudeModelCatalog.TryResolve(resolvedInput)
            ?? ClaudeModelCatalog.TryResolve(ClaudeModels.DefaultMainModel)
            ?? throw new InvalidOperationException("Claude model catalog is empty.");

        var provider = ResolveProvider(descriptor, resolvedInput);
        var providerModelId = ResolveProviderModelId(descriptor, provider);

        var capabilities = ModelCapability.WebFetch;
        if (SupportsWebSearch(descriptor, provider))
            capabilities |= ModelCapability.WebSearch;

        return new ProviderModelRoute(
            Input: resolvedInput,
            StableModelId: descriptor.StableId,
            ProviderModelId: providerModelId,
            Provider: provider,
            Family: descriptor.Family,
            Capabilities: capabilities);
    }

    public bool Supports(string? modelOrAlias, ModelCapability capability) =>
        Resolve(modelOrAlias).Supports(capability);

    private static ProviderKind ResolveProvider(
        ClaudeModelDescriptor descriptor,
        string input)
    {
        if (Matches(input, descriptor.ProviderIds.Bedrock))
            return ProviderKind.Bedrock;

        if (Matches(input, descriptor.ProviderIds.Vertex))
            return ProviderKind.Vertex;

        if (Matches(input, descriptor.ProviderIds.Foundry))
            return ProviderKind.Foundry;

        return ProviderKind.FirstParty;
    }

    private static string ResolveProviderModelId(
        ClaudeModelDescriptor descriptor,
        ProviderKind provider) =>
        provider switch
        {
            ProviderKind.Bedrock => descriptor.ProviderIds.Bedrock,
            ProviderKind.Vertex => descriptor.ProviderIds.Vertex,
            ProviderKind.Foundry => descriptor.ProviderIds.Foundry,
            _ => descriptor.ProviderIds.FirstParty,
        };

    private static bool SupportsWebSearch(
        ClaudeModelDescriptor descriptor,
        ProviderKind provider)
    {
        if (provider is ProviderKind.FirstParty or ProviderKind.Foundry)
            return true;

        if (provider == ProviderKind.Vertex)
            return IsClaude4Series(descriptor);

        return false;
    }

    private static bool IsClaude4Series(ClaudeModelDescriptor descriptor)
    {
        var stableId = descriptor.StableId;
        return stableId.Contains("-4", StringComparison.Ordinal)
            && !stableId.StartsWith("claude-3-5", StringComparison.Ordinal);
    }

    private static bool Matches(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}

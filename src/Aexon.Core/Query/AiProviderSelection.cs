namespace Aexon.Core.Query;

/// <summary>
/// Defines the supported AI providers for chat-client selection.
/// </summary>
public enum AiProvider
{
    Anthropic,
    OpenAI,
}

/// <summary>
/// Represents the resolved provider/model pair for a session.
/// </summary>
public sealed record AiSessionTarget(
    AiProvider Provider,
    string Model);

/// <summary>
/// Centralizes provider detection and model resolution rules.
/// </summary>
public static class AiProviderSelection
{
    public static AiSessionTarget ResolveSessionTarget(
        string? providerFlag,
        string? modelOverride,
        string? resumedProvider,
        string? resumedModel)
    {
        var persistedProvider = TryParse(resumedProvider, out var parsedPersistedProvider)
            ? parsedPersistedProvider
            : (AiProvider?)null;

        var explicitProvider = TryParse(providerFlag, out var parsedExplicitProvider)
            ? parsedExplicitProvider
            : (AiProvider?)null;

        var providerChanged = explicitProvider.HasValue &&
                              persistedProvider.HasValue &&
                              explicitProvider.Value != persistedProvider.Value;

        var modelInput = !string.IsNullOrWhiteSpace(modelOverride)
            ? modelOverride.Trim()
            : providerChanged
                ? null
                : resumedModel;

        var provider = DetectProvider(providerFlag, modelInput, persistedProvider);
        var model = ResolveModel(modelInput, provider);
        return new AiSessionTarget(provider, model);
    }

    public static AiProvider DetectProvider(
        string? providerHint,
        string? model,
        AiProvider? fallbackProvider = null)
    {
        if (TryParse(providerHint, out var explicitProvider))
            return explicitProvider;

        if (LooksLikeOpenAiModel(model))
            return AiProvider.OpenAI;

        if (LooksLikeAnthropicModel(model))
            return AiProvider.Anthropic;

        return fallbackProvider ?? AiProvider.Anthropic;
    }

    public static string ResolveModel(string? input, AiProvider provider)
    {
        if (provider == AiProvider.OpenAI)
            return string.IsNullOrWhiteSpace(input) ? "gpt-4o" : input.Trim();

        return ClaudeModels.Resolve(input);
    }

    public static bool TryParse(string? value, out AiProvider provider)
    {
        if (string.Equals(value, "openai", StringComparison.OrdinalIgnoreCase))
        {
            provider = AiProvider.OpenAI;
            return true;
        }

        if (string.Equals(value, "anthropic", StringComparison.OrdinalIgnoreCase))
        {
            provider = AiProvider.Anthropic;
            return true;
        }

        provider = default;
        return false;
    }

    public static string ToStorageValue(AiProvider provider) =>
        provider == AiProvider.OpenAI ? "openai" : "anthropic";

    public static bool LooksLikeOpenAiModel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
            return false;

        var trimmed = model.Trim();
        return trimmed.StartsWith("gpt-", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("o1", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("o3", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("o4", StringComparison.OrdinalIgnoreCase);
    }

    public static bool LooksLikeAnthropicModel(string? model) =>
        !string.IsNullOrWhiteSpace(model) &&
        ClaudeModelCatalog.TryResolve(model.Trim()) != null;
}

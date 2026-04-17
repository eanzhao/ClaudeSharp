using Aexon.Core.Providers;

namespace Aexon.Core.Configuration;

/// <summary>
/// Represents the effective runtime policy derived from managed settings.
/// </summary>
public sealed record ManagedRuntimePolicyDecision(
    AnthropicTokenSourceSnapshot? ActiveTokenSource,
    bool AllowWebSearch,
    bool AllowExternalMcpServers,
    bool AllowPlugins,
    bool IsProviderAllowed,
    IReadOnlyList<string> Diagnostics)
{
    public string? StartupSummary
    {
        get
        {
            var messages = new List<string>(Diagnostics);

            if (!AllowWebSearch)
                messages.Add("Managed settings: web search disabled by organization policy.");

            if (!AllowExternalMcpServers)
                messages.Add("Managed settings: external MCP servers disabled by organization policy.");

            if (!AllowPlugins)
                messages.Add("Managed settings: configured hooks/plugins disabled by organization policy.");

            return messages.Count == 0
                ? null
                : string.Join(Environment.NewLine, messages);
        }
    }
}

/// <summary>
/// Resolves effective runtime policy decisions from managed settings and the current model route.
/// </summary>
public static class ManagedRuntimePolicy
{
    public static ManagedRuntimePolicyDecision Resolve(
        ManagedSettingsSnapshot managedSettings,
        ProviderModelRoute route)
    {
        var diagnostics = new List<string>();
        var policy = managedSettings.OrganizationPolicy;
        var activeTokenSource = ResolveActiveTokenSource(managedSettings);

        var providerAllowed = IsProviderAllowed(route.Provider, policy.AllowedProviderKinds);
        if (!providerAllowed)
        {
            diagnostics.Add(
                $"Managed settings: provider '{FormatProviderKind(route.Provider)}' is not in the organization allowlist.");
        }

        if (policy.RequiresManagedAccess && activeTokenSource == null)
        {
            diagnostics.Add(
                "Managed settings: organization policy requires managed access, but no managed token source is active.");
        }

        return new ManagedRuntimePolicyDecision(
            activeTokenSource,
            policy.AllowWebSearch,
            policy.AllowExternalMcpServers,
            policy.AllowPlugins,
            providerAllowed,
            diagnostics);
    }

    internal static AnthropicTokenSourceSnapshot? ResolveActiveTokenSource(
        ManagedSettingsSnapshot managedSettings) =>
        managedSettings.TokenSources.FirstOrDefault(source => source.IsActive)
            ?? managedSettings.TokenSources.FirstOrDefault(source => source.IsDefault);

    internal static bool IsProviderAllowed(
        ProviderKind provider,
        IReadOnlyList<string> allowedProviderKinds)
    {
        if (allowedProviderKinds.Count == 0)
            return true;

        var normalizedProvider = NormalizeProviderKind(provider);
        return allowedProviderKinds
            .Select(ParseProviderKind)
            .Any(candidate => candidate == normalizedProvider);
    }

    private static ProviderKind ParseProviderKind(string raw)
    {
        var normalized = raw
            .Trim()
            .Replace("-", "", StringComparison.Ordinal)
            .Replace("_", "", StringComparison.Ordinal)
            .ToLowerInvariant();

        return normalized switch
        {
            "anthropic" => ProviderKind.FirstParty,
            "firstparty" => ProviderKind.FirstParty,
            "bedrock" => ProviderKind.Bedrock,
            "vertex" => ProviderKind.Vertex,
            "foundry" => ProviderKind.Foundry,
            _ => ProviderKind.Unknown,
        };
    }

    private static ProviderKind NormalizeProviderKind(ProviderKind provider) =>
        provider switch
        {
            ProviderKind.FirstParty => ProviderKind.FirstParty,
            ProviderKind.Bedrock => ProviderKind.Bedrock,
            ProviderKind.Vertex => ProviderKind.Vertex,
            ProviderKind.Foundry => ProviderKind.Foundry,
            _ => ProviderKind.Unknown,
        };

    private static string FormatProviderKind(ProviderKind provider) =>
        provider switch
        {
            ProviderKind.FirstParty => "anthropic",
            ProviderKind.Bedrock => "bedrock",
            ProviderKind.Vertex => "vertex",
            ProviderKind.Foundry => "foundry",
            _ => "unknown",
        };
}

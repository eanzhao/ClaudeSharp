using ClaudeSharp.Core.Providers;

namespace ClaudeSharp.Core.Configuration;

/// <summary>
/// Represents the effective runtime policy derived from managed settings.
/// </summary>
public sealed record ManagedRuntimePolicyDecision(
    AnthropicClientSettings AnthropicSettings,
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
        AnthropicClientSettings anthropicSettings,
        ProviderModelRoute route)
    {
        var diagnostics = new List<string>();
        var policy = managedSettings.OrganizationPolicy;
        var activeTokenSource = ResolveActiveTokenSource(managedSettings, anthropicSettings);
        var effectiveAnthropicSettings = anthropicSettings;

        if (!policy.AllowUserProvidedTokenSources &&
            activeTokenSource is { } blockedTokenSource &&
            IsUserProvidedTokenSource(blockedTokenSource.Kind))
        {
            diagnostics.Add("Managed settings: blocked user-provided Anthropic credentials by organization policy.");
            effectiveAnthropicSettings = anthropicSettings with
            {
                ApiKey = null,
                ApiKeyFromEnvironment = false,
                ApiKeyFromAppSettings = false,
                Diagnostics = anthropicSettings.Diagnostics
                    .Concat(["Managed settings: blocked user-provided Anthropic credentials by organization policy."])
                    .ToArray(),
            };
        }

        var providerAllowed = IsProviderAllowed(route.Provider, policy.AllowedProviderKinds);
        if (!providerAllowed)
        {
            diagnostics.Add(
                $"Managed settings: provider '{FormatProviderKind(route.Provider)}' is not in the organization allowlist.");
        }

        if (policy.RequiresManagedAccess)
        {
            if (activeTokenSource == null)
            {
                diagnostics.Add("Managed settings: organization policy requires managed access, but no token source is active.");
            }
            else if (IsUserProvidedTokenSource(activeTokenSource.Kind))
            {
                diagnostics.Add(
                    "Managed settings: organization policy requires managed access, but the active Anthropic credential is still user-provided.");
            }
            else if (!effectiveAnthropicSettings.HasApiKey)
            {
                diagnostics.Add(
                    $"Managed settings: token source '{activeTokenSource.Id}' is selected, but no usable Anthropic credential was resolved.");
            }
        }

        return new ManagedRuntimePolicyDecision(
            effectiveAnthropicSettings,
            activeTokenSource,
            policy.AllowWebSearch,
            policy.AllowExternalMcpServers,
            policy.AllowPlugins,
            providerAllowed,
            diagnostics);
    }

    internal static AnthropicTokenSourceSnapshot? ResolveActiveTokenSource(
        ManagedSettingsSnapshot managedSettings,
        AnthropicClientSettings anthropicSettings)
    {
        if (anthropicSettings.ApiKeyFromEnvironment)
        {
            return FindManagedTokenSource(managedSettings, AnthropicTokenSourceKind.EnvironmentVariable)
                ?? new AnthropicTokenSourceSnapshot
                {
                    Id = "environment",
                    Kind = AnthropicTokenSourceKind.EnvironmentVariable,
                    DisplayName = "Environment variable",
                    IsDefault = true,
                    IsActive = true,
                    Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["name"] = "ANTHROPIC_API_KEY",
                    },
                };
        }

        if (anthropicSettings.ApiKeyFromAppSettings)
        {
            return FindManagedTokenSource(
                       managedSettings,
                       AnthropicTokenSourceKind.AppSettings,
                       anthropicSettings.SourcePath)
                   ?? new AnthropicTokenSourceSnapshot
                   {
                       Id = "appsettings",
                       Kind = AnthropicTokenSourceKind.AppSettings,
                       DisplayName = "App settings",
                       SourcePath = anthropicSettings.SourcePath,
                       IsActive = true,
                   };
        }

        return managedSettings.TokenSources.FirstOrDefault(source => source.IsActive)
            ?? managedSettings.TokenSources.FirstOrDefault(source => source.IsDefault);
    }

    internal static bool IsUserProvidedTokenSource(AnthropicTokenSourceKind kind) =>
        kind is AnthropicTokenSourceKind.EnvironmentVariable or
            AnthropicTokenSourceKind.AppSettings or
            AnthropicTokenSourceKind.ApiKey;

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

    private static AnthropicTokenSourceSnapshot? FindManagedTokenSource(
        ManagedSettingsSnapshot managedSettings,
        AnthropicTokenSourceKind kind,
        string? sourcePath = null)
    {
        var match = managedSettings.TokenSources.FirstOrDefault(
            tokenSource =>
                tokenSource.Kind == kind &&
                (string.IsNullOrWhiteSpace(sourcePath) ||
                 string.Equals(
                     tokenSource.SourcePath,
                     sourcePath,
                     StringComparison.OrdinalIgnoreCase)));

        if (match == null)
            return null;

        return match with { IsActive = true };
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

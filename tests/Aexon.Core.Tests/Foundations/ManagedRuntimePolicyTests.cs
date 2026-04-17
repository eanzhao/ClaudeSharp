using Aexon.Core.Configuration;
using Aexon.Core.Providers;
using Aexon.Core.Query;

namespace Aexon.Core.Tests.Foundations;

/// <summary>
/// Contains tests for managed runtime policy resolution.
/// </summary>
public sealed class ManagedRuntimePolicyTests
{
    [Fact]
    public void Resolve_ReturnsNoActiveTokenSourceWhenManagedSettingsAreEmpty()
    {
        var managedSettings = new ManagedSettingsSnapshot
        {
            OrganizationPolicy = new OrganizationPolicySnapshot
            {
                RequiresManagedAccess = true,
                AllowUserProvidedTokenSources = false,
            },
        };

        var decision = ManagedRuntimePolicy.Resolve(managedSettings, CreateFirstPartyRoute());

        Assert.Null(decision.ActiveTokenSource);
        Assert.Contains(
            decision.Diagnostics,
            message => message.Contains("requires managed access", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Resolve_UsesManagedActiveTokenSourceAndPolicyFlags()
    {
        var managedSettings = new ManagedSettingsSnapshot
        {
            OrganizationPolicy = new OrganizationPolicySnapshot
            {
                RequiresManagedAccess = true,
                AllowWebSearch = false,
                AllowExternalMcpServers = false,
                AllowPlugins = false,
                AllowedProviderKinds = ["anthropic"],
            },
            TokenSources =
            [
                new AnthropicTokenSourceSnapshot
                {
                    Id = "managed-login",
                    Kind = AnthropicTokenSourceKind.UserLogin,
                    DisplayName = "Managed login",
                    IsActive = true,
                },
            ],
        };

        var decision = ManagedRuntimePolicy.Resolve(managedSettings, CreateFirstPartyRoute());

        Assert.Equal("managed-login", decision.ActiveTokenSource?.Id);
        Assert.False(decision.AllowWebSearch);
        Assert.False(decision.AllowExternalMcpServers);
        Assert.False(decision.AllowPlugins);
        Assert.True(decision.IsProviderAllowed);
        Assert.NotNull(decision.StartupSummary);
        Assert.Contains("web search disabled", decision.StartupSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_FlagsProviderOutsideManagedAllowlist()
    {
        var managedSettings = new ManagedSettingsSnapshot
        {
            OrganizationPolicy = new OrganizationPolicySnapshot
            {
                AllowedProviderKinds = ["bedrock"],
            },
        };

        var decision = ManagedRuntimePolicy.Resolve(managedSettings, CreateFirstPartyRoute());

        Assert.False(decision.IsProviderAllowed);
        Assert.Contains(
            decision.Diagnostics,
            message => message.Contains("not in the organization allowlist", StringComparison.OrdinalIgnoreCase));
    }

    private static ProviderModelRoute CreateFirstPartyRoute() =>
        new(
            Input: "claude-sonnet-4-20250514",
            StableModelId: "claude-sonnet-4-20250514",
            ProviderModelId: "claude-sonnet-4-20250514",
            Provider: ProviderKind.FirstParty,
            Family: ClaudeModelFamily.Sonnet,
            Capabilities: ModelCapability.WebFetch | ModelCapability.WebSearch);
}

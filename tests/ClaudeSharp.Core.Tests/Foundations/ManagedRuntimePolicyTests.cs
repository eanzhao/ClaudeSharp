using ClaudeSharp.Core.Configuration;
using ClaudeSharp.Core.Providers;
using ClaudeSharp.Core.Query;

namespace ClaudeSharp.Core.Tests.Foundations;

/// <summary>
/// Contains tests for managed runtime policy resolution.
/// </summary>
public sealed class ManagedRuntimePolicyTests
{
    [Fact]
    public void Resolve_BlocksUserProvidedEnvironmentCredentialsWhenPolicyDisallowsThem()
    {
        var managedSettings = new ManagedSettingsSnapshot
        {
            OrganizationPolicy = new OrganizationPolicySnapshot
            {
                RequiresManagedAccess = true,
                AllowUserProvidedTokenSources = false,
            },
        };
        var anthropicSettings = new AnthropicClientSettings(
            ApiKey: "secret",
            BaseUrl: null,
            ApiKeyFromEnvironment: true,
            ApiKeyFromAppSettings: false,
            SourcePath: null,
            Diagnostics: []);

        var decision = ManagedRuntimePolicy.Resolve(
            managedSettings,
            anthropicSettings,
            CreateFirstPartyRoute());

        Assert.False(decision.AnthropicSettings.HasApiKey);
        Assert.Equal(AnthropicTokenSourceKind.EnvironmentVariable, decision.ActiveTokenSource?.Kind);
        Assert.Contains(
            decision.Diagnostics,
            message => message.Contains("blocked user-provided", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            decision.Diagnostics,
            message => message.Contains("requires managed access", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Resolve_UsesManagedActiveTokenSourceAndPolicyFlagsWhenNoLocalCredentialsExist()
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
        var anthropicSettings = new AnthropicClientSettings(
            ApiKey: null,
            BaseUrl: null,
            ApiKeyFromEnvironment: false,
            ApiKeyFromAppSettings: false,
            SourcePath: null,
            Diagnostics: []);

        var decision = ManagedRuntimePolicy.Resolve(
            managedSettings,
            anthropicSettings,
            CreateFirstPartyRoute());

        Assert.Equal("managed-login", decision.ActiveTokenSource?.Id);
        Assert.False(decision.AllowWebSearch);
        Assert.False(decision.AllowExternalMcpServers);
        Assert.False(decision.AllowPlugins);
        Assert.True(decision.IsProviderAllowed);
        Assert.Contains(
            decision.Diagnostics,
            message => message.Contains("no usable Anthropic credential", StringComparison.OrdinalIgnoreCase));
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
        var anthropicSettings = new AnthropicClientSettings(
            ApiKey: "secret",
            BaseUrl: null,
            ApiKeyFromEnvironment: true,
            ApiKeyFromAppSettings: false,
            SourcePath: null,
            Diagnostics: []);

        var decision = ManagedRuntimePolicy.Resolve(
            managedSettings,
            anthropicSettings,
            CreateFirstPartyRoute());

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

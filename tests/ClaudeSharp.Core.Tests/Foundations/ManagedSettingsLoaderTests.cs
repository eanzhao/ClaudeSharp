using ClaudeSharp.Core.Configuration;
using ClaudeSharp.Core.Tests.Runtime;

namespace ClaudeSharp.Core.Tests.Foundations;

/// <summary>
/// Contains tests for managed settings loader.
/// </summary>
public sealed class ManagedSettingsLoaderTests
{
    [Fact]
    public void LoadFromFiles_MergesPolicyAndTokenSourcesAcrossFiles()
    {
        using var temp = new TempDirectory(nameof(LoadFromFiles_MergesPolicyAndTokenSourcesAcrossFiles));
        var firstConfig = temp.WriteFile("settings.json", """
{
  "managedSettings": {
    "organizationPolicy": {
      "organizationId": "org-1",
      "workspaceId": "workspace-1",
      "requiresManagedAccess": true,
      "allowUserProvidedTokenSources": true,
      "allowWebSearch": true,
      "allowExternalMcpServers": true,
      "allowPlugins": true,
      "allowedProviderKinds": [
        "Anthropic"
      ]
    },
    "tokenSources": [
      {
        "id": "environment",
        "kind": "environmentVariable",
        "displayName": "Environment variable",
        "isDefault": true,
        "isActive": true,
        "metadata": {
          "name": "ANTHROPIC_API_KEY"
        }
      }
    ]
  }
}
""");

        var secondConfig = temp.WriteFile("nested/settings.json", """
{
  "ClaudeSharp": {
    "managedSettings": {
      "organizationPolicy": {
        "allowWebSearch": false,
        "allowPlugins": false
      },
      "tokenSources": {
        "login": {
          "kind": "userLogin",
          "displayName": "User login",
          "parentId": "environment",
          "isActive": true
        }
      }
    }
  }
}
""");

        var result = ManagedSettingsLoader.LoadFromFiles([firstConfig, secondConfig], temp.Root);

        Assert.Equal([Path.GetFullPath(firstConfig), Path.GetFullPath(secondConfig)], result.SourcePaths);
        Assert.Equal("org-1", result.Settings.OrganizationPolicy.OrganizationId);
        Assert.Equal("workspace-1", result.Settings.OrganizationPolicy.WorkspaceId);
        Assert.True(result.Settings.OrganizationPolicy.RequiresManagedAccess);
        Assert.False(result.Settings.OrganizationPolicy.AllowWebSearch);
        Assert.False(result.Settings.OrganizationPolicy.AllowPlugins);
        Assert.Equal(["Anthropic"], result.Settings.OrganizationPolicy.AllowedProviderKinds);
        Assert.Equal(2, result.Settings.TokenSources.Count);
        Assert.Equal("environment", result.Settings.TokenSources[0].Id);
        Assert.Equal("login", result.Settings.TokenSources[1].Id);
        Assert.Equal(AnthropicTokenSourceKind.UserLogin, result.Settings.TokenSources[1].Kind);
        Assert.Equal("environment", result.Settings.TokenSources[1].ParentId);
        Assert.Equal("ANTHROPIC_API_KEY", result.Settings.TokenSources[0].Metadata["name"]);
        Assert.NotNull(result.StartupSummary);
        Assert.Contains("loaded 2 token source(s)", result.StartupSummary);
    }

    [Fact]
    public void Load_ReportsDiagnosticsForInvalidManagedSettingsShape()
    {
        using var temp = new TempDirectory(nameof(Load_ReportsDiagnosticsForInvalidManagedSettingsShape));
        var configPath = temp.WriteFile("settings.json", """
{
  "ClaudeSharp": {
    "managedSettings": []
  }
}
""");

        var result = ManagedSettingsLoader.Load(temp.Root, configPath);

        Assert.Empty(result.Settings.TokenSources);
        Assert.Equal(ManagedSettingsSnapshot.Empty.OrganizationPolicy, result.Settings.OrganizationPolicy);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Contains("managedSettings must be a JSON object", StringComparison.OrdinalIgnoreCase));
    }
}

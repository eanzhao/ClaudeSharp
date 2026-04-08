using ClaudeSharp.Core.Configuration;
using ClaudeSharp.Core.Tests.Runtime;

namespace ClaudeSharp.Core.Tests.Foundations;

/// <summary>
/// Contains tests for Anthropic client settings loading.
/// </summary>
public sealed class AnthropicClientSettingsLoaderTests
{
    [Fact]
    public void Load_ReadsAnthropicSectionFromAppSettings()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("appsettings.json", """
{
  "Anthropic": {
    "apiKey": "config-key",
    "baseUrl": "https://example.anthropic.local"
  }
}
""");

        var settings = AnthropicClientSettingsLoader.Load(
            temp.Root,
            _ => null);

        Assert.Equal("config-key", settings.ApiKey);
        Assert.Equal("https://example.anthropic.local", settings.BaseUrl);
        Assert.True(settings.ApiKeyFromAppSettings);
        Assert.False(settings.ApiKeyFromEnvironment);
        Assert.NotNull(settings.SourcePath);
        Assert.Contains("API key from appsettings.json", settings.StartupSummary, StringComparison.Ordinal);
        Assert.Contains("base URL from appsettings.json", settings.StartupSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_ReadsNestedClaudeSharpAnthropicSection()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("appsettings.json", """
{
  "ClaudeSharp": {
    "Anthropic": {
      "apiKey": "nested-key",
      "baseUrl": "https://nested.example.com"
    }
  }
}
""");

        var settings = AnthropicClientSettingsLoader.Load(
            temp.Root,
            _ => null);

        Assert.Equal("nested-key", settings.ApiKey);
        Assert.Equal("https://nested.example.com", settings.BaseUrl);
    }

    [Fact]
    public void Load_PrefersEnvironmentApiKeyOverAppSettings()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("appsettings.json", """
{
  "Anthropic": {
    "apiKey": "config-key",
    "baseUrl": "https://example.anthropic.local"
  }
}
""");

        var settings = AnthropicClientSettingsLoader.Load(
            temp.Root,
            name => name == "ANTHROPIC_API_KEY" ? "env-key" : null);

        Assert.Equal("env-key", settings.ApiKey);
        Assert.Equal("https://example.anthropic.local", settings.BaseUrl);
        Assert.True(settings.ApiKeyFromEnvironment);
        Assert.False(settings.ApiKeyFromAppSettings);
        Assert.Contains("ANTHROPIC_API_KEY", settings.StartupSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_PrefersAppSettingsSecretsOverAppSettings()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("appsettings.json", """
{
  "Anthropic": {
    "apiKey": "config-key",
    "baseUrl": "https://config.example.com"
  }
}
""");
        temp.WriteFile("appsettings.secrets.json", """
{
  "Anthropic": {
    "apiKey": "secret-key",
    "baseUrl": "https://secret.example.com"
  }
}
""");

        var settings = AnthropicClientSettingsLoader.Load(
            temp.Root,
            _ => null);

        Assert.Equal("secret-key", settings.ApiKey);
        Assert.Equal("https://secret.example.com", settings.BaseUrl);
        Assert.True(settings.ApiKeyFromAppSettings);
        Assert.False(settings.ApiKeyFromEnvironment);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(temp.Root, "appsettings.secrets.json")),
            settings.SourcePath);
        Assert.Contains("appsettings.secrets.json", settings.StartupSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_FallsBackToAppBaseDirectoryAppSettings()
    {
        using var temp = new TempDirectory();
        var appBaseDirectory = temp.CreateDirectory("bin/Debug/net10.0");
        temp.WriteFile("bin/Debug/net10.0/appsettings.json", """
{
  "Anthropic": {
    "apiKey": "output-key",
    "baseUrl": "https://output.example.com"
  }
}
""");

        var settings = AnthropicClientSettingsLoader.Load(
            temp.CreateDirectory("work"),
            _ => null,
            appBaseDirectory);

        Assert.Equal("output-key", settings.ApiKey);
        Assert.Equal("https://output.example.com", settings.BaseUrl);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(appBaseDirectory, "appsettings.json")),
            settings.SourcePath);
    }

    [Fact]
    public void Load_FallsBackToAppBaseDirectorySecretsFile()
    {
        using var temp = new TempDirectory();
        var appBaseDirectory = temp.CreateDirectory("bin/Debug/net10.0");
        temp.WriteFile("bin/Debug/net10.0/appsettings.secrets.json", """
{
  "Anthropic": {
    "apiKey": "output-secret-key",
    "baseUrl": "https://output-secret.example.com"
  }
}
""");

        var settings = AnthropicClientSettingsLoader.Load(
            temp.CreateDirectory("work"),
            _ => null,
            appBaseDirectory);

        Assert.Equal("output-secret-key", settings.ApiKey);
        Assert.Equal("https://output-secret.example.com", settings.BaseUrl);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(appBaseDirectory, "appsettings.secrets.json")),
            settings.SourcePath);
    }

    [Fact]
    public void Load_IgnoresInvalidBaseUrlAndReportsDiagnostic()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("appsettings.json", """
{
  "Anthropic": {
    "apiKey": "config-key",
    "baseUrl": "not-a-url"
  }
}
""");

        var settings = AnthropicClientSettingsLoader.Load(
            temp.Root,
            _ => null);

        Assert.Equal("config-key", settings.ApiKey);
        Assert.Null(settings.BaseUrl);
        Assert.Contains(
            settings.Diagnostics,
            diagnostic => diagnostic.Contains("must be an absolute URL", StringComparison.Ordinal));
    }
}

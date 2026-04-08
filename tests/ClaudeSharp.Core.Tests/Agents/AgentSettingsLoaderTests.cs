using ClaudeSharp.Core.Agents;
using ClaudeSharp.Core.Tests.Runtime;

namespace ClaudeSharp.Core.Tests.Agents;

/// <summary>
/// Contains tests for agent settings loading.
/// </summary>
public sealed class AgentSettingsLoaderTests
{
    [Fact]
    public void LoadFromFiles_MergesBackgroundConcurrencyAcrossFiles()
    {
        using var temp = new TempDirectory();
        var globalConfig = temp.WriteFile("global/settings.json", """
{
  "agents": {
    "backgroundConcurrency": 2
  }
}
""");

        var projectConfig = temp.WriteFile("project/.claude/settings.json", """
{
  "agents": {
    "background_run_concurrency": 4
  }
}
""");

        var result = AgentSettingsLoader.LoadFromFiles(
            [globalConfig, projectConfig],
            temp.FullPath("project"));

        Assert.Equal(4, result.Settings.BackgroundRunConcurrency);
        Assert.Equal(
            [Path.GetFullPath(globalConfig), Path.GetFullPath(projectConfig)],
            result.SourcePaths);
    }

    [Fact]
    public void LoadFromFiles_ReportsInvalidConcurrencyAndKeepsPreviousValue()
    {
        using var temp = new TempDirectory();
        var globalConfig = temp.WriteFile("global/settings.json", """
{
  "agents": {
    "backgroundConcurrency": 3
  }
}
""");

        var projectConfig = temp.WriteFile("project/.claude/settings.json", """
{
  "agents": {
    "backgroundConcurrency": 0
  }
}
""");

        var result = AgentSettingsLoader.LoadFromFiles(
            [globalConfig, projectConfig],
            temp.FullPath("project"));

        Assert.Equal(3, result.Settings.BackgroundRunConcurrency);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Contains("invalid background concurrency", StringComparison.OrdinalIgnoreCase));
    }
}

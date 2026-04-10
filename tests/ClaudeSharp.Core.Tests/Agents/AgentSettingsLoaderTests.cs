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
    "backgroundConcurrency": 2,
    "retainCompletedBackgroundRuns": 12,
    "autoResumeMode": "latest"
  }
}
""");

        var projectConfig = temp.WriteFile("project/.claude/settings.json", """
{
  "agents": {
    "background_run_concurrency": 4,
    "retain_completed_background_runs": 6,
    "retain_completed_work_items": 8
  }
}
""");

        var result = AgentSettingsLoader.LoadFromFiles(
            [globalConfig, projectConfig],
            temp.FullPath("project"));

        Assert.Equal(4, result.Settings.BackgroundRunConcurrency);
        Assert.Equal(6, result.Settings.RetainCompletedBackgroundRuns);
        Assert.Equal(8, result.Settings.RetainCompletedWorkItems);
        Assert.Equal(AgentAutoResumeMode.Latest, result.Settings.AutoResumeMode);
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
    "backgroundConcurrency": 3,
    "retainCompletedWorkItems": 5
  }
}
""");

        var projectConfig = temp.WriteFile("project/.claude/settings.json", """
{
  "agents": {
    "backgroundConcurrency": 0,
    "retainCompletedWorkItems": -1
  }
}
""");

        var result = AgentSettingsLoader.LoadFromFiles(
            [globalConfig, projectConfig],
            temp.FullPath("project"));

        Assert.Equal(3, result.Settings.BackgroundRunConcurrency);
        Assert.Equal(5, result.Settings.RetainCompletedWorkItems);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Contains("invalid background concurrency", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Contains("invalid completed work-item retention", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LoadFromFiles_ParsesAutoResumeAliasesAndRejectsInvalidValues()
    {
        using var temp = new TempDirectory();
        var globalConfig = temp.WriteFile("global/settings.json", """
{
  "agents": {
    "auto_resume_policy": "serial"
  }
}
""");

        var projectConfig = temp.WriteFile("project/.claude/settings.json", """
{
  "agents": {
    "auto_resume_mode": "manual"
  }
}
""");

        var badConfig = temp.WriteFile("project/.claude/override.json", """
{
  "agents": {
    "autoResumeMode": "mystery"
  }
}
""");

        var result = AgentSettingsLoader.LoadFromFiles(
            [globalConfig, projectConfig, badConfig],
            temp.FullPath("project"));

        Assert.Equal(AgentAutoResumeMode.Disabled, result.Settings.AutoResumeMode);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Contains("invalid auto-resume mode", StringComparison.OrdinalIgnoreCase));
    }
}

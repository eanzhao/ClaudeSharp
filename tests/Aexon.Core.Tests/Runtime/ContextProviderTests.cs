using Aexon.Core.Context;
using Aexon.Core.Query;
using Aexon.Core.Tools;

namespace Aexon.Core.Tests.Runtime;

/// <summary>
/// Contains tests for context Provider.
/// </summary>
public class ContextProviderTests
{
    [Fact]
    public async Task LoadMemoryAsync_LoadsClaudeFilesFromRootToLeafInOrder()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("CLAUDE.md", "root instructions");
        temp.WriteFile(".claude/CLAUDE.md", """
---
paths: src/**
---
nested instructions
""");
        temp.WriteFile(".claude/rules/b.md", "rule b");
        temp.WriteFile(".claude/rules/a.md", "rule a");
        temp.WriteFile("workspace/project/CLAUDE.local.md", "local instructions");

        var provider = new ContextProvider
        {
            WorkingDirectory = temp.FullPath("workspace", "project"),
        };

        await provider.LoadMemoryAsync();

        var memory = provider.MemoryContent!;
        var rootIndex = memory.IndexOf("root instructions", StringComparison.Ordinal);
        var nestedIndex = memory.IndexOf("nested instructions", StringComparison.Ordinal);
        var ruleAIndex = memory.IndexOf("rule a", StringComparison.Ordinal);
        var ruleBIndex = memory.IndexOf("rule b", StringComparison.Ordinal);
        var localIndex = memory.IndexOf("local instructions", StringComparison.Ordinal);

        Assert.True(rootIndex >= 0);
        Assert.True(nestedIndex > rootIndex);
        Assert.True(ruleAIndex > nestedIndex);
        Assert.True(ruleBIndex > ruleAIndex);
        Assert.True(localIndex > ruleBIndex);
        Assert.Contains("# Applies to: src/**", memory);
    }

    [Fact]
    public async Task BuildSystemPromptAsync_IncludesToolPromptMemoryAndGitlessEnvironment()
    {
        using var temp = new TempDirectory();
        var provider = new ContextProvider
        {
            WorkingDirectory = temp.Root,
            MemoryContent = "memory block",
        };

        var tool = new FakeTool
        {
            Name = "Search",
            Description = "search tool",
            PromptText = "search prompt",
        };

        var prompt = await provider.BuildSystemPromptAsync(
            [tool],
            new QueryEngineConfig());

        Assert.Contains("You are Aexon", prompt);
        Assert.Contains("# Search Tool", prompt);
        Assert.Contains("search prompt", prompt);
        Assert.Contains("# User's project instructions (CLAUDE.md)", prompt);
        Assert.Contains("memory block", prompt);
        Assert.Contains("Working Directory", prompt);
    }

    [Fact]
    public async Task BuildSystemPromptAsync_CustomPromptOverridesGeneratedSections()
    {
        using var temp = new TempDirectory();
        var provider = new ContextProvider
        {
            WorkingDirectory = temp.Root,
            MemoryContent = "memory block",
        };

        var prompt = await provider.BuildSystemPromptAsync(
            [new FakeTool { Name = "Search", PromptText = "search prompt" }],
            new QueryEngineConfig
            {
                CustomSystemPrompt = "custom only",
                AppendSystemPrompt = "append part",
            });

        Assert.Contains("custom only", prompt);
        Assert.Contains("append part", prompt);
        Assert.DoesNotContain("You are Aexon", prompt);
        Assert.DoesNotContain("memory block", prompt);
        Assert.DoesNotContain("search prompt", prompt);
    }
}

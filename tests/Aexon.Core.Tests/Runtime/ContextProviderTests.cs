using Aexon.Core.Context;
using Aexon.Core.Query;
using Aexon.Core.Todos;
using Aexon.Core.Tools;
using Aexon.Tools;

namespace Aexon.Core.Tests.Runtime;

/// <summary>
/// Contains tests for context Provider.
/// </summary>
public class ContextProviderTests
{
    [Fact]
    public async Task LoadMemoryAsync_LoadsSystemUserAndProjectFilesInOverrideOrder()
    {
        using var temp = new TempDirectory();
        temp.CreateDirectory("system");
        temp.WriteFile("system/CLAUDE.md", "system instructions");
        temp.WriteFile("home/.claude/CLAUDE.md", "user instructions");
        temp.WriteFile("CLAUDE.md", "outside git root");
        temp.CreateDirectory("workspace/.git");
        temp.WriteFile("workspace/CLAUDE.md", "root instructions");
        temp.WriteFile("workspace/project/CLAUDE.md", "project instructions");
        temp.WriteFile("workspace/.claude/CLAUDE.md", """
---
paths: src/**
---
nested instructions
""");
        temp.WriteFile("workspace/.claude/rules/b.md", "rule b");
        temp.WriteFile("workspace/.claude/rules/a.md", "rule a");
        temp.WriteFile("workspace/project/CLAUDE.local.md", "local instructions");

        var provider = new ContextProvider
        {
            WorkingDirectory = temp.FullPath("workspace", "project"),
            UserClaudeDirectory = temp.FullPath("home", ".claude"),
            SystemClaudeDirectory = temp.FullPath("system"),
        };

        await provider.LoadMemoryAsync();

        var memory = provider.MemoryContent!;
        var systemIndex = memory.IndexOf("system instructions", StringComparison.Ordinal);
        var userIndex = memory.IndexOf("user instructions", StringComparison.Ordinal);
        var rootIndex = memory.IndexOf("root instructions", StringComparison.Ordinal);
        var projectIndex = memory.IndexOf("project instructions", StringComparison.Ordinal);
        var nestedIndex = memory.IndexOf("nested instructions", StringComparison.Ordinal);
        var ruleAIndex = memory.IndexOf("rule a", StringComparison.Ordinal);
        var ruleBIndex = memory.IndexOf("rule b", StringComparison.Ordinal);
        var localIndex = memory.IndexOf("local instructions", StringComparison.Ordinal);

        Assert.True(systemIndex >= 0);
        Assert.True(userIndex > systemIndex);
        Assert.True(rootIndex > userIndex);
        Assert.True(nestedIndex > rootIndex);
        Assert.True(ruleAIndex > nestedIndex);
        Assert.True(ruleBIndex > ruleAIndex);
        Assert.True(projectIndex > ruleBIndex);
        Assert.True(localIndex > projectIndex);
        Assert.Contains("# Applies to: src/**", memory);
        Assert.DoesNotContain("outside git root", memory, StringComparison.Ordinal);
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

        Assert.Contains("# Identity", prompt);
        Assert.Contains("# Tools", prompt);
        Assert.Contains("## Search", prompt);
        Assert.Contains("search prompt", prompt);
        Assert.Contains("# Memory", prompt);
        Assert.Contains("## CLAUDE.md Instructions", prompt);
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

    [Fact]
    public async Task BuildSystemPromptAsync_IncludesCurrentTodoListFromTodoWriteTool()
    {
        using var temp = new TempDirectory();
        var provider = new ContextProvider
        {
            WorkingDirectory = temp.Root,
        };
        var runtime = new InMemoryTodoRuntime();
        runtime.CreateTodo("issue-4", "Implement TodoWrite", TodoStatus.InProgress, "Wire runtime");

        var prompt = await provider.BuildSystemPromptAsync(
            [new TodoWriteTool(runtime)],
            new QueryEngineConfig());

        Assert.Contains("# Tools", prompt);
        Assert.Contains("## TodoWrite", prompt);
        Assert.Contains("Current todo list:", prompt);
        Assert.Contains("issue-4 [in_progress] Implement TodoWrite", prompt);
        Assert.Contains("Wire runtime", prompt);
    }
}

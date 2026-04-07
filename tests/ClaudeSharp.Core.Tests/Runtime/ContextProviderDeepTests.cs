using System.Diagnostics;
using System.Text.Json;
using ClaudeSharp.Core.Context;
using ClaudeSharp.Core.Permissions;
using ClaudeSharp.Core.Query;
using ClaudeSharp.Core.Tools;

namespace ClaudeSharp.Core.Tests.Runtime;

public sealed class ContextProviderDeepTests
{
    [Fact]
    public async Task LoadMemoryAsync_ClearsMemoryWhenNoClaudeFilesExist()
    {
        using var temp = new TempDirectory();
        var provider = new ContextProvider
        {
            WorkingDirectory = temp.Root,
            MemoryContent = "stale",
        };

        await provider.LoadMemoryAsync();

        Assert.Null(provider.MemoryContent);
    }

    [Fact]
    public void GetPermissionContext_UsesCurrentWorkingDirectory()
    {
        using var temp = new TempDirectory();
        var provider = new ContextProvider
        {
            WorkingDirectory = temp.Root,
            PermissionContext = new PermissionContext
            {
                Mode = PermissionMode.Plan,
            },
        };

        var context = provider.GetPermissionContext();

        Assert.Equal(temp.Root, context.WorkingDirectory);
        Assert.Equal(PermissionMode.Plan, context.Mode);
        Assert.Same(provider.PermissionContext, context);
    }

    [Fact]
    public async Task BuildSystemPromptAsync_SkipsFailingAndBlankToolPrompts()
    {
        using var temp = new TempDirectory();
        var provider = new ContextProvider
        {
            WorkingDirectory = temp.Root,
        };

        var prompt = await provider.BuildSystemPromptAsync(
            [
                new FakeTool
                {
                    Name = "Visible",
                    PromptText = "visible prompt",
                },
                new BlankPromptTool(),
                new ThrowingPromptTool(),
            ],
            new QueryEngineConfig
            {
                AppendSystemPrompt = "append tail",
            });

        Assert.Contains("# Visible Tool", prompt);
        Assert.Contains("visible prompt", prompt);
        Assert.DoesNotContain("Blank Tool", prompt);
        Assert.DoesNotContain("Throw Tool", prompt);
        Assert.Contains("append tail", prompt);
    }

    [Fact]
    public async Task BuildSystemPromptAsync_IncludesGitStatusAndTruncatesLargeStatus()
    {
        using var temp = new TempDirectory();
        await InitializeGitRepositoryAsync(temp.Root);

        File.WriteAllText(temp.FullPath("tracked.txt"), "updated");
        for (var index = 0; index < 220; index++)
            File.WriteAllText(temp.FullPath($"untracked-{index:D3}.txt"), "content");

        var provider = new ContextProvider
        {
            WorkingDirectory = temp.Root,
        };

        var prompt = await provider.BuildSystemPromptAsync([], new QueryEngineConfig());

        Assert.Contains("# Current git status", prompt);
        Assert.Contains("Current branch:", prompt);
        Assert.Contains("Status:", prompt);
        Assert.Contains("Recent commits:", prompt);
        Assert.Contains("... (truncated)", prompt);
    }

    private static async Task InitializeGitRepositoryAsync(string workingDirectory)
    {
        await RunGitAsync(workingDirectory, "init");
        await RunGitAsync(workingDirectory, "config", "user.email", "test@example.com");
        await RunGitAsync(workingDirectory, "config", "user.name", "ClaudeSharp Tests");
        File.WriteAllText(Path.Combine(workingDirectory, "tracked.txt"), "initial");
        await RunGitAsync(workingDirectory, "add", "tracked.txt");
        await RunGitAsync(workingDirectory, "commit", "-m", "init");
    }

    private static async Task RunGitAsync(string workingDirectory, params string[] arguments)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        process.Start();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.True(
            process.ExitCode == 0,
            $"git {string.Join(' ', arguments)} failed: {stderr}");
    }

    private sealed class BlankPromptTool : ITool
    {
        public string Name => "Blank";
        public Task<string> GetDescriptionAsync() => Task.FromResult("blank tool");
        public JsonElement GetInputSchema() => JsonSerializer.SerializeToElement(new { type = "object" });
        public Task<string> GetPromptAsync(ToolPromptContext context) => Task.FromResult("   ");
        public Task<ToolResult> ExecuteAsync(JsonElement input, ToolExecutionContext context, IProgress<ToolProgress>? progress = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(ToolResult.Success("blank"));
    }

    private sealed class ThrowingPromptTool : ITool
    {
        public string Name => "Throw";
        public Task<string> GetDescriptionAsync() => Task.FromResult("throw tool");
        public JsonElement GetInputSchema() => JsonSerializer.SerializeToElement(new { type = "object" });
        public Task<string> GetPromptAsync(ToolPromptContext context) =>
            throw new InvalidOperationException("prompt boom");
        public Task<ToolResult> ExecuteAsync(JsonElement input, ToolExecutionContext context, IProgress<ToolProgress>? progress = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(ToolResult.Success("throw"));
    }
}

using System.Diagnostics;
using System.Text.Json;
using Aexon.Core.Context;
using Aexon.Core.Query;
using Aexon.Core.Permissions;
using Aexon.Core.Tools;

namespace Aexon.Core.Tests.Runtime;

/// <summary>
/// Contains tests for context Provider Branch.
/// </summary>
public sealed class ContextProviderBranchTests
{
    [Fact]
    public async Task BuildSystemPromptAsync_IncludesTruncatedGitStatus_AndSkipsBrokenToolPrompts()
    {
        using var temp = new TempDirectory();
        var repo = temp.CreateDirectory("repo");
        File.WriteAllText(Path.Combine(repo, "tracked.txt"), "hello");
        RunGit(repo, "init");
        RunGit(repo, "add", "tracked.txt");
        RunGit(repo, "-c", "user.name=Test", "-c", "user.email=test@example.com", "commit", "-m", "init");

        for (var i = 0; i < 120; i++)
        {
            File.WriteAllText(Path.Combine(repo, $"untracked-{i:000}.txt"), new string('x', 32));
        }

        var provider = new ContextProvider
        {
            WorkingDirectory = repo,
            MemoryContent = "memory block",
        };

        var prompt = await provider.BuildSystemPromptAsync(
            [new FakeTool { Name = "Good", PromptText = "good prompt" }, new ThrowingPromptTool()],
            new QueryEngineConfig());

        Assert.Contains("Current git status", prompt);
        Assert.Contains("Current branch:", prompt);
        Assert.Contains("Recent commits:", prompt);
        Assert.Contains("... (truncated)", prompt);
        Assert.Contains("good prompt", prompt);
        Assert.DoesNotContain("broken prompt", prompt);
    }

    [Fact]
    public async Task LoadMemoryAsync_ReturnsNullWhenNoFilesExist()
    {
        using var temp = new TempDirectory();
        var provider = new ContextProvider
        {
            WorkingDirectory = temp.Root,
        };

        await provider.LoadMemoryAsync();

        Assert.Null(provider.MemoryContent);
    }

    [Fact]
    public async Task LoadMemoryAsync_LoadsPlainFilesWithoutFrontmatter()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("CLAUDE.md", "plain body");

        var provider = new ContextProvider
        {
            WorkingDirectory = temp.Root,
        };

        await provider.LoadMemoryAsync();

        Assert.NotNull(provider.MemoryContent);
        Assert.Contains("plain body", provider.MemoryContent);
    }

    private static void RunGit(string workingDirectory, params string[] args)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start git.");
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(" ", args)} failed: {error}");
    }

    private sealed class ThrowingPromptTool : ITool
    {
        public string Name => "Broken";
        public string[] Aliases => [];
        public string Description => "broken";
        public string PromptText => "broken prompt";
        public JsonElement InputSchema => JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone();
        public bool Enabled => true;
        public bool ReadOnly => false;
        public bool ConcurrencySafe => false;
        public int MaxResultSize => 100;

        public Task<string> GetDescriptionAsync() => Task.FromResult("broken");

        public JsonElement GetInputSchema() => InputSchema;

        public Task<string> GetPromptAsync(ToolPromptContext context) =>
            throw new InvalidOperationException("boom");

        public Task<ToolResult> ExecuteAsync(
            JsonElement input,
            ToolExecutionContext context,
            IProgress<ToolProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ToolResult.Success("broken"));

        public Task<ValidationResult> ValidateInputAsync(JsonElement input, ToolExecutionContext context) =>
            Task.FromResult(ValidationResult.Valid());

        public Task<PermissionResult> CheckPermissionsAsync(JsonElement input, ToolExecutionContext context) =>
            Task.FromResult(PermissionResult.Allow());

        public bool IsEnabled() => Enabled;

        public bool IsReadOnly(JsonElement input) => ReadOnly;

        public bool IsConcurrencySafe(JsonElement input) => ConcurrencySafe;

        public int MaxResultSizeChars => MaxResultSize;
    }
}

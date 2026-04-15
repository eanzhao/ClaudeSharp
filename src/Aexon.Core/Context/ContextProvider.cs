using Aexon.Core.Markdown;
using Aexon.Core.Permissions;
using Aexon.Core.Query;
using Aexon.Core.Tools;

namespace Aexon.Core.Context;

/// <summary>
/// Provides context provider.
/// </summary>
public class ContextProvider
{
    public string WorkingDirectory { get; set; } = Environment.CurrentDirectory;
    public PermissionContext PermissionContext { get; set; } = new();
    public string? MemoryContent { get; set; }
    public string? SessionMemoryContent { get; set; }

    public PermissionContext GetPermissionContext()
    {
        PermissionContext.WorkingDirectory = WorkingDirectory;
        return PermissionContext;
    }

    /// <summary>
    /// Builds system prompt.
    /// </summary>
    public async Task<string> BuildSystemPromptAsync(
        IReadOnlyList<ITool> tools,
        QueryEngineConfig config)
    {
        var parts = new List<string>();

        // 1. Add the core identity prompt.
        parts.Add(GetIdentityPrompt());

        // 2. Add the environment section.
        parts.Add(GetEnvironmentSection());

        // 3. Add tool-specific usage guidance.
        var toolPromptCtx = new ToolPromptContext
        {
            PermissionContext = GetPermissionContext(),
            Tools = tools,
        };

        foreach (var tool in tools)
        {
            try
            {
                var toolPrompt = await tool.GetPromptAsync(toolPromptCtx);
                if (!string.IsNullOrWhiteSpace(toolPrompt))
                {
                    parts.Add($"# {tool.Name} Tool\n{toolPrompt}");
                }
            }
            catch
            {
                // Skip tools that fail to generate prompts
            }
        }

        // 4. Add the git status snapshot.
        var gitStatus = await GetGitStatusAsync();
        if (!string.IsNullOrWhiteSpace(gitStatus))
        {
            parts.Add($"# Current git status\n{gitStatus}");
        }

        // 5. Add project instructions loaded from CLAUDE.md-style files.
        if (!string.IsNullOrWhiteSpace(MemoryContent))
        {
            parts.Add($"# User's project instructions (CLAUDE.md)\n{MemoryContent}");
        }

        if (!string.IsNullOrWhiteSpace(SessionMemoryContent))
        {
            parts.Add($"# Session memory\n{SessionMemoryContent}");
        }

        // 6. Apply custom or appended system prompts.
        if (!string.IsNullOrWhiteSpace(config.CustomSystemPrompt))
        {
            parts.Clear();
            parts.Add(config.CustomSystemPrompt);
        }

        if (!string.IsNullOrWhiteSpace(config.AppendSystemPrompt))
        {
            parts.Add(config.AppendSystemPrompt);
        }

        return string.Join("\n\n", parts);
    }

    /// <summary>
    /// Builds the core identity prompt used by Aexon.
    /// </summary>
    private string GetIdentityPrompt()
    {
        return """
            You are Aexon, a powerful agentic CLI coding assistant.

            You are pair programming with a USER to solve their coding task. You have access to a set of tools that allow you to interact with the user's codebase, execute commands, and manage files.

            Key behaviors:
            - Always use tools to complete tasks rather than just describing what to do
            - When you need to read or modify files, use the appropriate file tools
            - When you need to run commands, use the Bash tool
            - Ask for clarification if the task is ambiguous
            - Be concise in your responses
            - Show file changes clearly

            Important rules:
            - NEVER execute destructive commands without user confirmation
            - ALWAYS verify file paths before writing
            - When editing files, preserve existing formatting and style
            - Quote file paths containing spaces
            """;
    }

    private string GetEnvironmentSection()
    {
        var os = Environment.OSVersion.Platform switch
        {
            PlatformID.Unix => "macOS/Linux",
            PlatformID.Win32NT => "Windows",
            _ => "Unknown",
        };

        return $"""
            # Environment
            - Operating System: {os}
            - Working Directory: {WorkingDirectory}
            - Current Date: {DateTime.Now:yyyy-MM-dd}
            - Shell: {(os == "Windows" ? "cmd/powershell" : "bash/zsh")}
            """;
    }

    /// <summary>
    /// Collects the current Git branch, status, and recent commits.
    /// </summary>
    private async Task<string?> GetGitStatusAsync()
    {
        try
        {
            var gitDir = Path.Combine(WorkingDirectory, ".git");
            if (!Directory.Exists(gitDir))
                return null;

            var shell = OperatingSystem.IsWindows() ? "cmd" : "bash";
            var shellArg = OperatingSystem.IsWindows() ? "/c" : "-c";

            var tasks = new[]
            {
                RunGitCommandAsync("git rev-parse --abbrev-ref HEAD"),
                RunGitCommandAsync("git status --short"),
                RunGitCommandAsync("git log --oneline -n 5"),
            };

            var results = await Task.WhenAll(tasks);

            var branch = results[0]?.Trim() ?? "unknown";
            var status = results[1]?.Trim() ?? "";
            var log = results[2]?.Trim() ?? "";

            if (status.Length > 2000)
                status = status[..2000] + "\n... (truncated)";

            return string.Join("\n\n", new[]
            {
                $"Current branch: {branch}",
                $"Status:\n{(string.IsNullOrEmpty(status) ? "(clean)" : status)}",
                $"Recent commits:\n{log}",
            });
        }
        catch
        {
            return null;
        }
    }

    private async Task<string?> RunGitCommandAsync(string command)
    {
        try
        {
            using var process = new System.Diagnostics.Process();
            process.StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "cmd" : "bash",
                Arguments = OperatingSystem.IsWindows() ? $"/c {command}" : $"-c \"{command}\"",
                WorkingDirectory = WorkingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            return process.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Loads memory.
    /// </summary>
    public async Task LoadMemoryAsync()
    {
        var files = await MemoryInstructionScanner.ScanAsync(WorkingDirectory);
        if (files.Count == 0)
        {
            MemoryContent = null;
            return;
        }

        MemoryContent = string.Join(
            "\n\n",
            files.Select(FormatMemoryFile));
    }

    private static string FormatMemoryFile(MemoryInstructionFile file)
    {
        var parts = new List<string> { $"# From {file.Path}" };

        if (file.Frontmatter.TryGetValue("paths", out var pathsValue))
        {
            var paths = FrontmatterParser.SplitPathValue(pathsValue);
            if (paths.Count > 0)
                parts.Add($"# Applies to: {string.Join(", ", paths)}");
        }

        parts.Add(file.Content);
        return string.Join("\n", parts);
    }
}

using ClaudeSharp.Core.Permissions;
using ClaudeSharp.Core.Query;
using ClaudeSharp.Core.Tools;
using ClaudeSharp.Core.Markdown;

namespace ClaudeSharp.Core.Context;

/// <summary>
/// 上下文提供器 — 对应 Claude Code 的 context.ts + systemPrompt (getSystemPrompt)
///
/// 负责收集并组装系统提示，包括：
/// 1. 核心身份与能力说明
/// 2. 每个工具的 prompt() 输出
/// 3. Git 状态快照
/// 4. Memory 文件 (CLAUDE.md)
/// 5. 当前日期
/// </summary>
public class ContextProvider
{
    public string WorkingDirectory { get; set; } = Environment.CurrentDirectory;
    public PermissionContext PermissionContext { get; set; } = new();
    public string? MemoryContent { get; set; }

    public PermissionContext GetPermissionContext()
    {
        PermissionContext.WorkingDirectory = WorkingDirectory;
        return PermissionContext;
    }

    /// <summary>
    /// 构建完整的系统提示 — 对应 Claude Code 的 getSystemPrompt()
    ///
    /// Claude Code 的系统提示结构:
    /// [Identity] + [Tool prompts] + [Environment info] + [Git status] + [CLAUDE.md]
    /// </summary>
    public async Task<string> BuildSystemPromptAsync(
        IReadOnlyList<ITool> tools,
        QueryEngineConfig config)
    {
        var parts = new List<string>();

        // 1. 核心身份提示
        parts.Add(GetIdentityPrompt());

        // 2. 环境信息
        parts.Add(GetEnvironmentSection());

        // 3. 工具使用说明 (每个工具自己的 prompt)
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

        // 4. Git 状态快照
        var gitStatus = await GetGitStatusAsync();
        if (!string.IsNullOrWhiteSpace(gitStatus))
        {
            parts.Add($"# Current git status\n{gitStatus}");
        }

        // 5. CLAUDE.md / Memory 内容 — 对应 getUserContext()
        if (!string.IsNullOrWhiteSpace(MemoryContent))
        {
            parts.Add($"# User's project instructions (CLAUDE.md)\n{MemoryContent}");
        }

        // 6. 自定义提示 / 追加提示
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
    /// 核心身份提示 — 对应 Claude Code 的 identityPrompt (constants/prompts.ts)
    /// </summary>
    private string GetIdentityPrompt()
    {
        return """
            You are ClaudeSharp, a powerful agentic CLI coding assistant.

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
    /// 获取 Git 状态 — 对应 Claude Code 的 getGitStatus() (context.ts)
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
    /// 从 CLAUDE.md 加载 Memory 内容 — 对应 getMemoryFiles() + getClaudeMds()
    /// Claude Code 会搜索 cwd 向上沿路径查找所有 CLAUDE.md
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

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClaudeSharp.Core.Permissions;
using ClaudeSharp.Core.Tools;

namespace ClaudeSharp.Tools;

/// <summary>
/// BashTool 输入参数 — 对应 Claude Code 的 BashTool inputSchema (Zod)
/// </summary>
public class BashToolInput
{
    [JsonPropertyName("command")]
    public string Command { get; set; } = "";

    [JsonPropertyName("timeout")]
    public int? TimeoutMs { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

/// <summary>
/// BashTool — 对应 Claude Code 的 tools/BashTool/BashTool.tsx (~160KB)
///
/// Claude Code 中最复杂的工具，负责：
/// 1. 执行用户 shell 命令
/// 2. 流式读取 stdout/stderr
/// 3. 超时管理
/// 4. 复杂的权限检查 (只读命令自动允许，写命令需要批准)
/// 5. Sandbox 安全限制
///
/// 本实现为简化版，保留核心执行逻辑和基础权限检查
/// </summary>
public class BashTool : ITool
{
    private const int DefaultTimeoutMs = 120_000;  // 2 分钟
    private const int MaxTimeoutMs = 600_000;      // 10 分钟

    /// <summary>已知的只读命令前缀</summary>
    private static readonly string[] ReadOnlyCommands =
    {
        "ls", "cat", "head", "tail", "wc", "find", "grep", "rg",
        "git status", "git log", "git diff", "git show", "git branch",
        "git remote", "git rev-parse", "git describe",
        "which", "where", "type", "file", "stat",
        "echo", "printf", "pwd", "env", "printenv",
        "whoami", "hostname", "uname", "date",
        "dotnet --version", "node --version", "python --version",
        "gh pr view", "gh issue view", "gh run view",
    };

    public string Name => "Bash";

    public Task<string> GetDescriptionAsync() =>
        Task.FromResult("Execute a bash command in the user's shell.");

    public JsonElement GetInputSchema()
    {
        var schema = """
        {
            "type": "object",
            "properties": {
                "command": {
                    "type": "string",
                    "description": "The bash command to execute"
                },
                "timeout": {
                    "type": "integer",
                    "description": "Optional timeout in milliseconds (default: 120000, max: 600000)"
                },
                "description": {
                    "type": "string",
                    "description": "Brief description of what this command does (for the user)"
                }
            },
            "required": ["command"],
            "additionalProperties": false
        }
        """;
        return JsonDocument.Parse(schema).RootElement.Clone();
    }

    public Task<string> GetPromptAsync(ToolPromptContext context)
    {
        return Task.FromResult("""
            Executes a given bash command and returns its output.

            Important guidelines:
            - Use this tool to run shell commands
            - Prefer dedicated tools (Read, Write, Edit, Grep) over bash equivalents
            - Always quote file paths with spaces
            - Use absolute paths when possible
            - When running multiple independent commands, make separate tool calls
            - For sequential commands, chain with &&
            - Do not use interactive commands (git rebase -i, vim, etc.)
            - Default timeout is 2 minutes, maximum is 10 minutes
            """);
    }

    public async Task<ToolResult> ExecuteAsync(
        JsonElement input,
        ToolExecutionContext context,
        IProgress<ToolProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var parsed = JsonSerializer.Deserialize<BashToolInput>(input);
        if (parsed == null || string.IsNullOrWhiteSpace(parsed.Command))
            return ToolResult.Error("Command is required.");

        var timeout = TimeSpan.FromMilliseconds(
            Math.Min(parsed.TimeoutMs ?? DefaultTimeoutMs, MaxTimeoutMs));

        var shell = GetShell();
        var shellArgs = GetShellArgs(parsed.Command);

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = shell,
            Arguments = shellArgs,
            WorkingDirectory = context.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            Environment = { ["PAGER"] = "cat" },
        };

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                stdout.AppendLine(e.Data);
                progress?.Report(new ToolProgress("", "bash_output", e.Data));
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
                stderr.AppendLine(e.Data);
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);

            await process.WaitForExitAsync(cts.Token);

            return ToolResult.Success(FormatOutput(
                stdout.ToString(), stderr.ToString(), process.ExitCode));
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }

            return ToolResult.Error(
                $"Command timed out after {timeout.TotalSeconds}s.\n" +
                $"Partial output:\n{stdout}");
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Failed to execute command: {ex.Message}");
        }
    }

    /// <summary>
    /// 权限检查 — 对应 Claude Code 的 bashPermissions.ts (~99KB)
    /// 简化版: 只读命令自动允许，其他命令需要用户批准
    /// </summary>
    public Task<PermissionResult> CheckPermissionsAsync(
        JsonElement input, ToolExecutionContext context)
    {
        var command = input.TryGetProperty("command", out var cmdProp)
            ? cmdProp.GetString() ?? ""
            : "";

        // 只读命令自动允许
        if (IsReadOnlyCommand(command))
            return Task.FromResult(PermissionResult.Allow());

        // 其他命令需要用户批准
        return Task.FromResult(PermissionResult.Ask($"Allow running: {command}"));
    }

    public bool IsReadOnly(JsonElement input)
    {
        var command = input.TryGetProperty("command", out var cmdProp)
            ? cmdProp.GetString() ?? ""
            : "";
        return IsReadOnlyCommand(command);
    }

    public bool IsConcurrencySafe(JsonElement input) => IsReadOnly(input);

    public string GetUserFacingName(JsonElement? input)
    {
        if (input?.TryGetProperty("command", out var cmd) == true)
        {
            var command = cmd.GetString() ?? "";
            return command.Length > 60 ? $"Bash: {command[..57]}..." : $"Bash: {command}";
        }
        return "Bash";
    }

    public string? GetActivityDescription(JsonElement? input)
    {
        if (input?.TryGetProperty("command", out var cmd) == true)
            return $"Running {cmd.GetString()}";
        return "Running command";
    }

    public int MaxResultSizeChars => 200_000;

    // ─── Private helpers ──────────────────────────────

    private static bool IsReadOnlyCommand(string command)
    {
        var trimmed = command.TrimStart();
        return ReadOnlyCommands.Any(prefix =>
            trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetShell()
    {
        if (OperatingSystem.IsWindows())
            return "cmd.exe";

        var shell = Environment.GetEnvironmentVariable("SHELL") ?? "/bin/bash";
        return shell;
    }

    private static string GetShellArgs(string command)
    {
        if (OperatingSystem.IsWindows())
            return $"/c {command}";
        return $"-c \"{command.Replace("\"", "\\\"")}\"";
    }

    private static string FormatOutput(string stdout, string stderr, int exitCode)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(stdout))
            sb.Append(stdout.TrimEnd());

        if (!string.IsNullOrWhiteSpace(stderr))
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.Append($"STDERR:\n{stderr.TrimEnd()}");
        }

        if (exitCode != 0)
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.Append($"Exit code: {exitCode}");
        }

        return sb.Length > 0 ? sb.ToString() : "(no output)";
    }
}

using System.Text;
using System.Text.Json;
using Anthropic;
using ClaudeSharp.Commands;
using ClaudeSharp.Core.Agents;
using ClaudeSharp.Core.Hooks;
using ClaudeSharp.Core.Memory;
using ClaudeSharp.Core.Commands;
using ClaudeSharp.Core.Context;
using ClaudeSharp.Core.Mcp;
using ClaudeSharp.Core.Permissions;
using ClaudeSharp.Core.Providers;
using ClaudeSharp.Core.Query;
using ClaudeSharp.Core.Storage;
using ClaudeSharp.Core.Tools;
using ClaudeSharp.Tools;

namespace ClaudeSharp.Cli;

/// <summary>
/// Hosts the ClaudeSharp CLI entry point.
/// </summary>
internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        var options = CliOptions.Parse(args);
        if (options.ShowHelp)
        {
            PrintHelp();
            return 0;
        }

        if (options.ContinueLatest && !string.IsNullOrWhiteSpace(options.ResumeTarget))
        {
            Console.Error.WriteLine("--continue 和 --resume 不能同时使用。");
            return 1;
        }

        if (options.ForkSession &&
            !options.ContinueLatest &&
            string.IsNullOrWhiteSpace(options.ResumeTarget))
        {
            Console.Error.WriteLine("--fork-session 只能和 --continue 或 --resume 一起使用。");
            return 1;
        }

        var transcriptStore = new JsonlTranscriptStore();
        var resumeLoader = new SessionResumeLoader(transcriptStore, new ConversationRecovery());
        var restorePipeline = new SessionRestorePipeline();

        ProcessedResume? resumed = null;
        if (options.ContinueLatest || !string.IsNullOrWhiteSpace(options.ResumeTarget))
        {
            var source = options.ContinueLatest
                ? ResumeSource.Latest()
                : ResumeSource.Session(options.ResumeTarget!);

            var loadResult = await resumeLoader.LoadAsync(source);
            if (loadResult == null)
            {
                Console.Error.WriteLine("没有找到可恢复的会话。");
                return 1;
            }

            resumed = await restorePipeline.RestoreAsync(
                loadResult,
                new ResumeOptions
                {
                    WorkingDirectoryOverride = options.WorkingDirectory,
                    ModelOverride = string.IsNullOrWhiteSpace(options.Model)
                        ? null
                        : ClaudeModels.Resolve(options.Model),
                    ForkSession = options.ForkSession,
                });
        }

        var workingDirectory = Path.GetFullPath(
            resumed?.WorkingDirectory ??
            (string.IsNullOrWhiteSpace(options.WorkingDirectory)
                ? Environment.CurrentDirectory
                : options.WorkingDirectory));

        Environment.CurrentDirectory = workingDirectory;

        var contextProvider = new ContextProvider
        {
            WorkingDirectory = workingDirectory,
        };
        if (resumed?.Metadata.Mode is PermissionMode resumedMode)
            contextProvider.PermissionContext.Mode = resumedMode;
        await contextProvider.LoadMemoryAsync();

        var model = resumed?.Model ?? ClaudeModels.Resolve(options.Model);
        var config = new QueryEngineConfig
        {
            Model = model,
            UseStreamingApi = true,
        };
        var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        using var client = string.IsNullOrWhiteSpace(apiKey)
            ? new AnthropicClient()
            : new AnthropicClient { ApiKey = apiKey };
        var providerRouter = new DefaultProviderCapabilityRouter();
        var hookBuild = HookRuntimeBuilder.Build(
            workingDirectory,
            options.SettingsPath);
        var hooks = hookBuild.Runtime;
        var agentTaskRuntime = new InMemoryAgentTaskRuntime();
        var toolRegistry = BuildToolRegistry(
            providerRouter,
            () => config.Model,
            client,
            hooks,
            agentTaskRuntime);
        var commandRegistry = BuildCommandRegistry();
        await using var mcpRuntime = await McpRuntime.CreateAsync(
            toolRegistry,
            workingDirectory,
            options.SettingsPath);

        var session = resumed != null && resumed.ContinueExistingSession
            ? resumed.SourceSession
            : await transcriptStore.CreateSessionAsync(workingDirectory, model);

        session.WorkingDirectory = workingDirectory;
        session.Model = model;
        session.Metadata = resumed?.Metadata.Clone() ?? session.Metadata;
        await transcriptStore.UpdateSessionAsync(session);
        var journal = new ConversationJournal(transcriptStore, session);
        var memoryLayout = CreateMemdirLayout(workingDirectory);
        memoryLayout.EnsureDirectories();
        var sessionMemoryFile = memoryLayout.CreateSessionMemoryFile(session.SessionId);
        contextProvider.SessionMemoryContent = await sessionMemoryFile.LoadAsync();
        if (resumed != null && !resumed.ContinueExistingSession)
        {
            await journal.SeedAsync(
                resumed.Messages,
                resumed.Metadata,
                workingDirectory,
                model);
        }

        await using var queryEngine = new QueryEngine(
            client,
            toolRegistry,
            new DefaultPermissionChecker(),
            config,
            contextProvider,
            hooks: hooks,
            journal: journal,
            sessionMemoryFile: sessionMemoryFile,
            initialMessages: resumed?.Messages,
            initialUsage: resumed?.TotalUsage,
            initialMetadata: resumed?.Metadata);

        var startupParts = new List<string>();
        switch (resumed)
        {
            case { ContinueExistingSession: true }:
                startupParts.Add(
                    $"Resumed session {resumed.SourceSession.SessionId} ({resumed.Messages.Count} messages)");
                break;

            case not null:
                startupParts.Add(
                    $"Forked session {session.SessionId} from {resumed.SourceSession.SessionId} ({resumed.Messages.Count} messages)");
                break;
        }

        if (!string.IsNullOrWhiteSpace(mcpRuntime.StartupSummary))
            startupParts.Add(mcpRuntime.StartupSummary);
        if (!string.IsNullOrWhiteSpace(hookBuild.StartupSummary))
            startupParts.Add(hookBuild.StartupSummary);

        var startupNote = startupParts.Count == 0
            ? null
            : string.Join(Environment.NewLine, startupParts);

        var shell = new ClaudeSharpShell(
            workingDirectory,
            apiKey,
            toolRegistry,
            commandRegistry,
            queryEngine,
            contextProvider.GetPermissionContext(),
            startupNote);

        return await shell.RunAsync(options.InitialPrompt);
    }

    private static ToolRegistry BuildToolRegistry(
        IProviderCapabilityRouter providerRouter,
        Func<string?> currentModelAccessor,
        AnthropicClient client,
        IHookRuntime hooks,
        IAgentTaskRuntime agentTaskRuntime)
    {
        var registry = new ToolRegistry();
        registry.Register(new BashTool());
        registry.Register(new FileReadTool());
        registry.Register(new FileWriteTool());
        registry.Register(new FileEditTool());
        registry.Register(new GlobTool());
        registry.Register(new GrepTool());
        registry.Register(new WebFetchTool());
        registry.Register(new WebSearchTool(providerRouter, currentModelAccessor));
        registry.Register(new AgentTool(
            new QueryEngineAgentRunner(client, hooks: hooks),
            providerRouter,
            agentTaskRuntime,
            hooks));
        return registry;
    }

    private static MemdirLayout CreateMemdirLayout(string workingDirectory)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var memoryBaseDirectory = Path.Combine(home, ".claudesharp", "memory");
        return new MemdirLayout
        {
            MemoryBaseDirectory = memoryBaseDirectory,
            ProjectRootDirectory = workingDirectory,
        };
    }

    private static CommandRegistry BuildCommandRegistry()
    {
        var registry = new CommandRegistry();
        registry.Register(new HelpCommand());
        registry.Register(new ClearCommand());
        registry.Register(new CompactCommand());
        registry.Register(new CostCommand());
        registry.Register(new ExitCommand());
        registry.Register(new ModelCommand());
        registry.Register(new MicrocompactCommand());
        registry.Register(new ModeCommand());
        registry.Register(new PartialCompactCommand());
        registry.Register(new SessionCommand());
        registry.Register(new SessionMemoryCompactCommand());
        registry.Register(new TitleCommand());
        registry.Register(new TagCommand());
        return registry;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
Usage:
  ClaudeSharp [--cwd <path>] [--model <model>] [--resume <session>] [--continue] [--fork-session] [--settings <path>] [prompt]

Options:
  --cwd <path>       Working directory for this session
  --model <model>    Main model or alias (sonnet / opus / haiku)
  --resume <id>      Resume a specific session by id, directory, manifest, or transcript path
  --continue         Resume the most recently updated session
  --fork-session     Fork the resumed transcript into a brand new session
  --settings <path>  Load hooks and MCP servers from a specific settings.json file
  --mcp-config       Alias for --settings
  --help             Show this help
""");
    }

    private sealed class ClaudeSharpShell
    {
        private readonly string _workingDirectory;
        private readonly string? _apiKey;
        private readonly ToolRegistry _toolRegistry;
        private readonly CommandRegistry _commandRegistry;
        private readonly QueryEngine _queryEngine;
        private readonly PermissionContext _permissionContext;
        private readonly string? _startupNote;
        private bool _exitRequested;

        public ClaudeSharpShell(
            string workingDirectory,
            string? apiKey,
            ToolRegistry toolRegistry,
            CommandRegistry commandRegistry,
            QueryEngine queryEngine,
            PermissionContext permissionContext,
            string? startupNote)
        {
            _workingDirectory = workingDirectory;
            _apiKey = apiKey;
            _toolRegistry = toolRegistry;
            _commandRegistry = commandRegistry;
            _queryEngine = queryEngine;
            _permissionContext = permissionContext;
            _startupNote = startupNote;
        }

        public async Task<int> RunAsync(string? initialPrompt)
        {
            var interactive = !Console.IsInputRedirected && string.IsNullOrWhiteSpace(initialPrompt);
            if (interactive)
                PrintBanner();

            var commandContext = new CommandContext
            {
                WriteLine = Console.WriteLine,
                Tools = _toolRegistry,
                QueryEngine = _queryEngine,
                PermissionContext = _permissionContext,
                Commands = _commandRegistry.GetAll(),
                RequestExit = () => _exitRequested = true,
                RequestClear = () =>
                {
                    if (!Console.IsOutputRedirected)
                        Console.Clear();
                },
            };

            if (!string.IsNullOrWhiteSpace(initialPrompt))
            {
                await HandleInputAsync(initialPrompt, commandContext);
                return 0;
            }

            while (!_exitRequested)
            {
                if (interactive)
                    Console.Write("\nclaudesharp> ");

                var input = Console.ReadLine();
                if (input == null)
                    break;

                if (string.IsNullOrWhiteSpace(input))
                    continue;

                await HandleInputAsync(input, commandContext);
            }

            return 0;
        }

        private async Task HandleInputAsync(string input, CommandContext commandContext)
        {
            if (_commandRegistry.IsCommand(input))
            {
                var parts = input.Split(' ', 2, StringSplitOptions.TrimEntries);
                var name = parts[0];
                var args = parts.Length > 1 ? parts[1] : "";
                var command = _commandRegistry.Get(name);
                if (command != null)
                    await command.ExecuteAsync(args, commandContext);
                return;
            }

            await RunQueryAsync(input);
        }

        private async Task RunQueryAsync(string input)
        {
            var wroteAssistantText = false;

            await foreach (var evt in _queryEngine.SubmitMessageAsync(input))
            {
                switch (evt)
                {
                    case TextDeltaEvent text:
                        wroteAssistantText = true;
                        Console.Write(text.Text);
                        break;

                    case ThinkingDeltaEvent:
                        // Hide raw thinking text for now to keep the terminal output clean.
                        break;

                    case ToolUseStartEvent toolUse:
                        if (wroteAssistantText)
                            Console.WriteLine();

                        Console.WriteLine(
                            $"\n[{toolUse.ToolName}] {SummarizeToolInput(toolUse.Input)}");
                        break;

                    case PermissionRequestEvent permissionRequest:
                        permissionRequest.SetResponse(AskForPermission(permissionRequest));
                        break;

                    case ToolResultEvent toolResult:
                        var status = toolResult.IsError ? "failed" : "done";
                        Console.WriteLine($"[{toolResult.ToolName}] {status}");
                        if (toolResult.IsError)
                            Console.WriteLine(toolResult.Result);
                        break;

                    case ContextCompactionEvent compaction:
                        var prefix = compaction.Automatic ? "[context:auto]" : "[context]";
                        if (compaction.Mode == "microcompact")
                        {
                            Console.WriteLine(
                                $"{prefix} microcompact: cleared {compaction.ClearedToolResults} tool results, {compaction.ClearedThinkingBlocks} thinking blocks");
                        }
                        else if (compaction.Mode == "session_memory")
                        {
                            Console.WriteLine(
                                $"{prefix} session memory: folded {compaction.RemovedMessages} older messages and kept {compaction.PreservedMessages} messages verbatim");
                        }
                        else if (compaction.Mode == "compact")
                        {
                            Console.WriteLine(
                                $"{prefix} compact: summarized {compaction.RemovedMessages} messages and kept {compaction.PreservedMessages} messages active");
                        }
                        else if (compaction.Mode == "failed")
                        {
                            Console.WriteLine($"{prefix} failed: {compaction.Reason}");
                        }
                        else if (compaction.Mode == "skipped")
                        {
                            Console.WriteLine($"{prefix} skipped: {compaction.Reason}");
                        }
                        break;

                    case MessageEndEvent:
                        if (wroteAssistantText)
                            Console.WriteLine();
                        break;

                    case QueryCompleteEvent complete when !complete.Success:
                        Console.WriteLine();
                        Console.WriteLine($"请求失败: {complete.ErrorMessage}");
                        break;
                }
            }
        }

        private bool AskForPermission(PermissionRequestEvent request)
        {
            if (Console.IsInputRedirected)
                return false;

            Console.Write($"{request.Description}，是否允许？ [y/N] ");
            var answer = Console.ReadLine()?.Trim();
            return answer is "y" or "Y" or "yes" or "YES";
        }

        private static string SummarizeToolInput(JsonElement input)
        {
            var raw = input.GetRawText();
            return raw.Length <= 120 ? raw : $"{raw[..117]}...";
        }

        private void PrintBanner()
        {
            Console.WriteLine("ClaudeSharp (.NET 10)");
            Console.WriteLine($"Working directory: {_workingDirectory}");
            Console.WriteLine($"Model: {_queryEngine.CurrentModel}");
            if (!string.IsNullOrWhiteSpace(_queryEngine.SessionId))
                Console.WriteLine($"Session: {_queryEngine.SessionId}");
            if (!string.IsNullOrWhiteSpace(_startupNote))
                Console.WriteLine(_startupNote);
            Console.WriteLine("输入 /help 查看内置命令，/exit 退出。");

            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                Console.WriteLine(
                    "未检测到 ANTHROPIC_API_KEY。你仍然可以使用本地斜杠命令，但真正发起 Claude 请求会失败。");
            }
        }
    }

    private sealed record CliOptions(
        bool ShowHelp,
        bool ContinueLatest,
        bool ForkSession,
        string? WorkingDirectory,
        string? Model,
        string? ResumeTarget,
        string? SettingsPath,
        string? InitialPrompt)
    {
        public static CliOptions Parse(string[] args)
        {
            var showHelp = false;
            var continueLatest = false;
            var forkSession = false;
            string? workingDirectory = null;
            string? model = null;
            string? resumeTarget = null;
            string? settingsPath = null;
            var remaining = new List<string>();

            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--help":
                    case "-h":
                        showHelp = true;
                        break;

                    case "--cwd":
                        if (i + 1 < args.Length)
                            workingDirectory = args[++i];
                        break;

                    case "--resume":
                        if (i + 1 < args.Length)
                            resumeTarget = args[++i];
                        break;

                    case "--continue":
                        continueLatest = true;
                        break;

                    case "--fork-session":
                        forkSession = true;
                        break;

                    case "--model":
                    case "-m":
                        if (i + 1 < args.Length)
                            model = args[++i];
                        break;

                    case "--settings":
                    case "--mcp-config":
                        if (i + 1 < args.Length)
                            settingsPath = args[++i];
                        break;

                    default:
                        remaining.Add(args[i]);
                        break;
                }
            }

            return new CliOptions(
                showHelp,
                continueLatest,
                forkSession,
                workingDirectory,
                model,
                resumeTarget,
                settingsPath,
                remaining.Count > 0 ? string.Join(' ', remaining) : null);
        }
    }
}

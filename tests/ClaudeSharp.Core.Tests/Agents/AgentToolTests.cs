using System.Text.Json;
using ClaudeSharp.Core.Agents;
using ClaudeSharp.Core.Hooks;
using ClaudeSharp.Core.Messages;
using ClaudeSharp.Core.Permissions;
using ClaudeSharp.Core.Providers;
using ClaudeSharp.Core.Query;
using ClaudeSharp.Core.Tests.Runtime;
using ClaudeSharp.Core.Tools;
using ClaudeSharp.Tools;

namespace ClaudeSharp.Core.Tests.Agents;

/// <summary>
/// Contains tests for the Agent tool.
/// </summary>
public sealed class AgentToolTests
{
    [Fact]
    public async Task ExecuteAsync_RunsReadOnlySubagentAndMarksWorkItemCompleted()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var runner = new RecordingRunner(new AgentExecutionResult(
            Summary: "child summary",
            Success: true,
            Usage: new TokenUsage
            {
                InputTokens = 5,
                OutputTokens = 7,
            },
            TurnCount: 2));

        var tool = new AgentTool(
            runner,
            new DefaultProviderCapabilityRouter(),
            runtime,
            HookRuntime.Empty);

        var result = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new
            {
                prompt = "Inspect the query pipeline",
                subagent_type = "research",
            }),
            CreateContext());

        Assert.False(result.IsError);
        Assert.Contains("Subagent work-item-1 completed.", result.Data, StringComparison.Ordinal);
        Assert.Contains("child summary", result.Data, StringComparison.Ordinal);
        Assert.Contains("Turns: 2", result.Data, StringComparison.Ordinal);
        Assert.Contains("Usage: in=5, out=7", result.Data, StringComparison.Ordinal);

        var request = Assert.Single(runner.Requests);
        Assert.Equal("Inspect the query pipeline", request.Prompt);
        Assert.Equal("claude-sonnet-4-6", request.Model);
        Assert.True(request.UseIsolatedWorkspace);
        Assert.Equal(
            ["Glob", "Grep", "Read", "WebFetch", "WebSearch"],
            request.Tools.GetAllTools().Select(tool => tool.Name).OrderBy(name => name, StringComparer.Ordinal));
        Assert.Contains("Subagent type hint: research", request.SystemPromptAppendix, StringComparison.Ordinal);

        var workItem = Assert.Single(runtime.ListWorkItems());
        Assert.Equal(AgentWorkItemStatus.Completed, workItem.Status);
        Assert.Equal("Inspect the query pipeline", workItem.Title);
    }

    [Fact]
    public async Task ExecuteAsync_ReportsRunnerFailuresAndMarksWorkItemBlocked()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var runner = new RecordingRunner(new AgentExecutionResult(
            Summary: string.Empty,
            Success: false,
            Usage: TokenUsage.Empty,
            TurnCount: 1,
            ErrorMessage: "child failed"));

        var tool = new AgentTool(
            runner,
            new DefaultProviderCapabilityRouter(),
            runtime,
            HookRuntime.Empty);

        var result = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new { prompt = "Trace the bug" }),
            CreateContext());

        Assert.True(result.IsError);
        Assert.Contains("child failed", result.Data, StringComparison.Ordinal);
        Assert.Equal(AgentWorkItemStatus.Blocked, Assert.Single(runtime.ListWorkItems()).Status);
    }

    [Fact]
    public async Task ExecuteAsync_CanLaunchBackgroundSubagent()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var runner = new AsyncRecordingRunner(async (_, cancellationToken) =>
        {
            await Task.Delay(25, cancellationToken);
            return new AgentExecutionResult(
                Summary: "background summary",
                Success: true,
                Usage: new TokenUsage
                {
                    InputTokens = 11,
                    OutputTokens = 13,
                },
                TurnCount: 3);
        });

        var tool = new AgentTool(
            runner,
            new DefaultProviderCapabilityRouter(),
            runtime,
            HookRuntime.Empty);

        var result = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new
            {
                prompt = "Inspect the runtime",
                run_in_background = true,
            }),
            CreateContext());

        Assert.False(result.IsError);
        Assert.Contains("background-run-1", result.Data, StringComparison.Ordinal);

        await WaitForAsync(() =>
            runtime.GetBackgroundRun("background-run-1")?.Status == AgentBackgroundRunStatus.Stopped);

        var workItem = Assert.Single(runtime.ListWorkItems());
        Assert.Equal(AgentWorkItemStatus.Completed, workItem.Status);

        var backgroundRun = Assert.Single(runtime.ListBackgroundRuns());
        Assert.Equal(AgentBackgroundRunStatus.Stopped, backgroundRun.Status);
        Assert.Contains(backgroundRun.Output, chunk =>
            chunk.Contains("background summary", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_BackgroundSubagentStreamsProgressToRuntime()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var runner = new AsyncRecordingRunner(async (request, cancellationToken) =>
        {
            request.Progress?.Report(new AgentExecutionProgress("status", "Child agent started"));
            request.Progress?.Report(new AgentExecutionProgress("text", "First line"));
            request.Progress?.Report(new AgentExecutionProgress("text", "\nSecond line"));
            request.Progress?.Report(new AgentExecutionProgress(
                "tool_start",
                "{\"file_path\":\"/tmp/example.txt\"}",
                ToolName: "Read",
                ToolUseId: "tool-1"));
            request.Progress?.Report(new AgentExecutionProgress(
                "tool_result",
                string.Empty,
                ToolName: "Read",
                ToolUseId: "tool-1"));
            await Task.Delay(25, cancellationToken);
            return new AgentExecutionResult(
                Summary: "background summary",
                Success: true,
                Usage: TokenUsage.Empty,
                TurnCount: 2);
        });

        var tool = new AgentTool(
            runner,
            new DefaultProviderCapabilityRouter(),
            runtime,
            HookRuntime.Empty);

        var result = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new
            {
                prompt = "Inspect the runtime",
                run_in_background = true,
            }),
            CreateContext());

        Assert.False(result.IsError);

        await WaitForAsync(() =>
            runtime.GetBackgroundRun("background-run-1")?.Status == AgentBackgroundRunStatus.Stopped);

        var backgroundRun = Assert.Single(runtime.ListBackgroundRuns());
        Assert.Contains(backgroundRun.Output, chunk =>
            chunk.Contains("[status] Child agent started", StringComparison.Ordinal));
        Assert.Contains(backgroundRun.Output, chunk =>
            chunk.Contains("First line", StringComparison.Ordinal));
        Assert.Contains(backgroundRun.Output, chunk =>
            chunk.Contains("Second line", StringComparison.Ordinal));
        Assert.Contains(backgroundRun.Output, chunk =>
            chunk.Contains("[Read] {\"file_path\":\"/tmp/example.txt\"}", StringComparison.Ordinal));
        Assert.Contains(backgroundRun.Output, chunk =>
            chunk.Contains("[Read] done", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AgentStatusTool_ReturnsOverviewAndDetails()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var workItem = runtime.CreateWorkItem("Inspect tools", owner: "subagent");
        runtime.UpdateWorkItem(workItem.Id, item => item.Status = AgentWorkItemStatus.Completed);

        var backgroundRun = runtime.StartBackgroundRun("Inspect tools", "subagent");
        runtime.AppendBackgroundRunOutput(backgroundRun.Id, "Summary: all good");
        runtime.StopBackgroundRun(backgroundRun.Id, "completed");

        var tool = new AgentStatusTool(runtime);

        var overview = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new { }),
            CreateContext());
        var details = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new
            {
                id = backgroundRun.Id,
                include_output = true,
            }),
            CreateContext());

        Assert.False(overview.IsError);
        Assert.Contains(workItem.Id, overview.Data, StringComparison.Ordinal);
        Assert.Contains(backgroundRun.Id, overview.Data, StringComparison.Ordinal);

        Assert.False(details.IsError);
        Assert.Contains("Background run:", details.Data, StringComparison.Ordinal);
        Assert.Contains("Summary: all good", details.Data, StringComparison.Ordinal);
        Assert.Contains("completed", details.Data, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AgentStatusTool_ReturnsErrorForMissingId()
    {
        var tool = new AgentStatusTool(new InMemoryAgentTaskRuntime());

        var result = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new { id = "missing" }),
            CreateContext());

        Assert.True(result.IsError);
        Assert.Contains("missing", result.Data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AgentStatusTool_CanReturnOutputWindow()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var backgroundRun = runtime.StartBackgroundRun("Inspect tools", "subagent");
        runtime.AppendBackgroundRunOutput(backgroundRun.Id, "line 1");
        runtime.AppendBackgroundRunOutput(backgroundRun.Id, "line 2");
        runtime.AppendBackgroundRunOutput(backgroundRun.Id, "line 3");
        runtime.StopBackgroundRun(backgroundRun.Id, "completed");

        var tool = new AgentStatusTool(runtime);
        var result = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new
            {
                id = backgroundRun.Id,
                include_output = true,
                output_offset = 1,
                output_limit = 1,
            }),
            CreateContext());

        Assert.False(result.IsError);
        Assert.Contains("Showing output entries 2-2 of 3.", result.Data, StringComparison.Ordinal);
        Assert.DoesNotContain("line 1", result.Data, StringComparison.Ordinal);
        Assert.Contains("line 2", result.Data, StringComparison.Ordinal);
        Assert.DoesNotContain("line 3", result.Data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_CanDisableWorkspaceIsolation()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var runner = new RecordingRunner(new AgentExecutionResult(
            Summary: "child summary",
            Success: true,
            Usage: TokenUsage.Empty,
            TurnCount: 1));

        var tool = new AgentTool(
            runner,
            new DefaultProviderCapabilityRouter(),
            runtime,
            HookRuntime.Empty);

        var result = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new
            {
                prompt = "Inspect the query pipeline",
                use_isolated_workspace = false,
            }),
            CreateContext());

        Assert.False(result.IsError);
        Assert.False(Assert.Single(runner.Requests).UseIsolatedWorkspace);
    }

    [Fact]
    public async Task QueryEngineAgentRunner_UsesChildQueryEngineAndReturnsSummary()
    {
        using var temp = new TempDirectory();
        var handler = new FakeAnthropicHandler();
        handler.EnqueueResponse(FakeAnthropicHandler.CreateMessageResponse("child answer", inputTokens: 3, outputTokens: 4));
        var client = TestSupport.CreateAnthropicClient(handler);

        var registry = new ToolRegistry();
        registry.Register(new FakeTool { Name = "search" });

        var runner = new QueryEngineAgentRunner(client);
        var result = await runner.RunAsync(
            new AgentExecutionRequest
            {
                Prompt = "Summarize the subsystem",
                WorkingDirectory = temp.Root,
                Model = "claude-sonnet-4-6",
                Tools = registry,
                PermissionContext = new PermissionContext(),
                SystemPromptAppendix = "child appendix",
            });

        Assert.True(result.Success);
        Assert.Equal("child answer", result.Summary);
        Assert.Equal(1, result.TurnCount);
        Assert.Equal(3, result.Usage.InputTokens);
        Assert.Equal(4, result.Usage.OutputTokens);

        var request = JsonDocument.Parse(handler.Bodies[0]).RootElement;
        Assert.Equal("claude-sonnet-4-6", request.GetProperty("model").GetString());
        Assert.Equal("Summarize the subsystem", request.GetProperty("messages")[0].GetProperty("content")[0].GetProperty("text").GetString());
        Assert.Equal("search", request.GetProperty("tools")[0].GetProperty("name").GetString());
        Assert.Contains("child appendix", request.GetProperty("system").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task QueryEngineAgentRunner_UsesWorkspaceManagerWorkingDirectory()
    {
        using var temp = new TempDirectory();
        var source = temp.CreateDirectory("source");
        var isolated = temp.CreateDirectory("isolated");
        var handler = new FakeAnthropicHandler();
        handler.EnqueueResponse(FakeAnthropicHandler.CreateMessageResponse("child answer", inputTokens: 3, outputTokens: 4));
        var client = TestSupport.CreateAnthropicClient(handler);

        var workspaceManager = new RecordingWorkspaceManager(isolated);
        var runner = new QueryEngineAgentRunner(client, workspaceManager: workspaceManager);

        var result = await runner.RunAsync(
            new AgentExecutionRequest
            {
                Prompt = "Summarize the subsystem",
                WorkingDirectory = source,
                Model = "claude-sonnet-4-6",
                Tools = new ToolRegistry(),
                PermissionContext = new PermissionContext(),
                SystemPromptAppendix = "child appendix",
            });

        Assert.True(result.Success);
        Assert.True(workspaceManager.DisposeCalled);

        var request = JsonDocument.Parse(handler.Bodies[0]).RootElement;
        Assert.Contains(
            $"Working Directory: {isolated}",
            request.GetProperty("system").GetString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task QueryEngineAgentRunner_ReportsTextProgress()
    {
        using var temp = new TempDirectory();
        var handler = new FakeAnthropicHandler();
        handler.EnqueueResponse(FakeAnthropicHandler.CreateMessageResponse("child answer", inputTokens: 3, outputTokens: 4));
        var client = TestSupport.CreateAnthropicClient(handler);

        var progressEvents = new List<AgentExecutionProgress>();
        var runner = new QueryEngineAgentRunner(client);

        var result = await runner.RunAsync(
            new AgentExecutionRequest
            {
                Prompt = "Summarize the subsystem",
                WorkingDirectory = temp.Root,
                Model = "claude-sonnet-4-6",
                Tools = new ToolRegistry(),
                PermissionContext = new PermissionContext(),
                Progress = new RecordingProgress(progressEvents),
            });

        Assert.True(result.Success);
        Assert.Contains(progressEvents, evt =>
            evt.Type == "text" &&
            evt.Message.Contains("child answer", StringComparison.Ordinal));
    }

    private static ToolExecutionContext CreateContext() =>
        new()
        {
            WorkingDirectory = "/tmp",
            PermissionContext = new PermissionContext(),
            Tools = [],
            Messages = [],
            CancellationToken = CancellationToken.None,
            MainLoopModel = "claude-sonnet-4-6",
        };

    private sealed class RecordingRunner : IAgentExecutionRunner
    {
        private readonly AgentExecutionResult _result;

        public RecordingRunner(AgentExecutionResult result)
        {
            _result = result;
        }

        public List<AgentExecutionRequest> Requests { get; } = [];

        public Task<AgentExecutionResult> RunAsync(
            AgentExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(_result);
        }
    }

    private sealed class AsyncRecordingRunner : IAgentExecutionRunner
    {
        private readonly Func<AgentExecutionRequest, CancellationToken, Task<AgentExecutionResult>> _handler;

        public AsyncRecordingRunner(
            Func<AgentExecutionRequest, CancellationToken, Task<AgentExecutionResult>> handler)
        {
            _handler = handler;
        }

        public List<AgentExecutionRequest> Requests { get; } = [];

        public async Task<AgentExecutionResult> RunAsync(
            AgentExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return await _handler(request, cancellationToken);
        }
    }

    private static async Task WaitForAsync(Func<bool> predicate)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            if (predicate())
                return;

            await Task.Delay(25);
        }

        Assert.True(predicate(), "Condition was not met before timeout.");
    }

    private sealed class RecordingWorkspaceManager : IAgentWorkspaceManager
    {
        private readonly string _workingDirectory;

        public RecordingWorkspaceManager(string workingDirectory)
        {
            _workingDirectory = workingDirectory;
        }

        public bool DisposeCalled { get; private set; }

        public Task<AgentWorkspaceLease> AcquireAsync(
            string workingDirectory,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                new AgentWorkspaceLease(
                    _workingDirectory,
                    _workingDirectory,
                    isIsolated: true,
                    disposeAsync: () =>
                    {
                        DisposeCalled = true;
                        return ValueTask.CompletedTask;
                    }));
        }
    }

    private sealed class RecordingProgress : IProgress<AgentExecutionProgress>
    {
        private readonly List<AgentExecutionProgress> _events;

        public RecordingProgress(List<AgentExecutionProgress> events)
        {
            _events = events;
        }

        public void Report(AgentExecutionProgress value) => _events.Add(value);
    }
}

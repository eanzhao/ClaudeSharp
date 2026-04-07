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
}

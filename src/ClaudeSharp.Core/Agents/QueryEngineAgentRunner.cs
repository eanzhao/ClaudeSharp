using System.Text;
using Anthropic;
using ClaudeSharp.Core.Context;
using ClaudeSharp.Core.Hooks;
using ClaudeSharp.Core.Messages;
using ClaudeSharp.Core.Permissions;
using ClaudeSharp.Core.Query;

namespace ClaudeSharp.Core.Agents;

/// <summary>
/// Executes subagent tasks through an isolated QueryEngine instance.
/// </summary>
public sealed class QueryEngineAgentRunner : IAgentExecutionRunner
{
    private readonly AnthropicClient _client;
    private readonly IPermissionChecker _permissions;
    private readonly IHookRuntime _fallbackHooks;
    private readonly IAgentWorkspaceManager _workspaceManager;

    public QueryEngineAgentRunner(
        AnthropicClient client,
        IPermissionChecker? permissions = null,
        IHookRuntime? hooks = null,
        IAgentWorkspaceManager? workspaceManager = null)
    {
        _client = client;
        _permissions = permissions ?? new DefaultPermissionChecker();
        _fallbackHooks = hooks ?? HookRuntime.Empty;
        _workspaceManager = workspaceManager ?? new GitWorktreeAgentWorkspaceManager();
    }

    public async Task<AgentExecutionResult> RunAsync(
        AgentExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var workspace = request.UseIsolatedWorkspace
            ? await _workspaceManager.AcquireAsync(request.WorkingDirectory, cancellationToken)
            : AgentWorkspaceLease.Passthrough(
                Path.GetFullPath(request.WorkingDirectory),
                "Workspace isolation disabled.");

        var contextProvider = new ContextProvider
        {
            WorkingDirectory = workspace.WorkingDirectory,
            PermissionContext = ClonePermissionContext(
                request.PermissionContext,
                workspace.WorkingDirectory),
            MemoryContent = request.MemoryContent,
            SessionMemoryContent = request.SessionMemoryContent,
        };

        if (string.IsNullOrWhiteSpace(contextProvider.MemoryContent))
            await contextProvider.LoadMemoryAsync();

        var config = new QueryEngineConfig
        {
            Model = request.Model,
            UseStreamingApi = false,
            MaxTurns = 12,
            EnableAutoCompact = false,
            EnableSessionMemoryCompact = false,
            AppendSystemPrompt = request.SystemPromptAppendix,
        };

        await using var engine = new QueryEngine(
            _client,
            request.Tools,
            _permissions,
            config,
            contextProvider,
            hooks: request.Hooks ?? _fallbackHooks);

        var text = new StringBuilder();
        var sawError = false;
        string? errorMessage = null;
        var turnCount = 0;

        await foreach (var evt in engine.SubmitMessageAsync(request.Prompt, cancellationToken))
        {
            switch (evt)
            {
                case TextDeltaEvent delta:
                    text.Append(delta.Text);
                    break;

                case QueryCompleteEvent complete:
                    turnCount = complete.TurnCount;
                    if (!complete.Success)
                    {
                        sawError = true;
                        errorMessage = complete.ErrorMessage;
                    }

                    break;
            }
        }

        var summary = text.ToString().Trim();
        if (string.IsNullOrWhiteSpace(summary))
        {
            summary = engine.Messages
                .OfType<AssistantMessage>()
                .LastOrDefault()?
                .Content
                .OfType<TextBlock>()
                .Select(block => block.Text)
                .FirstOrDefault()
                ?.Trim()
                ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(summary))
            summary = sawError ? string.Empty : "(Subagent completed without a text summary.)";

        return new AgentExecutionResult(
            Summary: summary,
            Success: !sawError,
            Usage: engine.TotalUsage,
            TurnCount: turnCount == 0 ? 1 : turnCount,
            ErrorMessage: errorMessage);
    }

    private static PermissionContext ClonePermissionContext(
        PermissionContext source,
        string workingDirectory)
    {
        var clone = new PermissionContext
        {
            Mode = source.Mode,
            WorkingDirectory = workingDirectory,
        };

        foreach (var path in source.AdditionalWorkingDirectories)
            clone.AdditionalWorkingDirectories.Add(path);

        foreach (var rule in source.AlwaysAllowRules)
            clone.AlwaysAllowRules.Add(rule);

        foreach (var rule in source.AlwaysAskRules)
            clone.AlwaysAskRules.Add(rule);

        foreach (var rule in source.AlwaysDenyRules)
            clone.AlwaysDenyRules.Add(rule);

        foreach (var rule in source.Rules)
            clone.Rules.Add(rule);

        return clone;
    }
}

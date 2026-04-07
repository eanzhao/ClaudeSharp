using System.Text.Json;

namespace ClaudeSharp.Core.Hooks;

/// <summary>
/// Executes configured shell commands for hook events.
/// </summary>
public sealed class CommandHookObserver : HookObserver
{
    private readonly IHookCommandRunner _runner;
    private readonly Dictionary<HookEventKind, IReadOnlyList<HookCommandDefinition>> _commandsByEvent;

    public CommandHookObserver(
        IEnumerable<HookCommandDefinition> commands,
        IHookCommandRunner? runner = null)
    {
        _runner = runner ?? new ProcessHookCommandRunner();
        _commandsByEvent = commands
            .GroupBy(command => command.EventKind)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<HookCommandDefinition>)group.ToArray());
    }

    public override async ValueTask<PreToolUseHookResult> OnPreToolUseAsync(
        PreToolUseHookContext context,
        CancellationToken cancellationToken = default)
    {
        var currentInput = context.Input;
        foreach (var command in GetCommands(HookEventKind.PreToolUse))
        {
            var execution = await ExecuteAsync(
                command,
                context,
                context.ToolExecutionContext.WorkingDirectory,
                currentInput,
                cancellationToken);

            if (!execution.Succeeded)
            {
                if (!command.FailOpen)
                    return PreToolUseHookResult.Block(FormatFailureMessage(command, execution)) with
                    {
                        UpdatedInput = currentInput,
                    };

                continue;
            }

            if (!TryParseJsonObject(execution.Stdout, out var response, out var parseError))
            {
                if (!command.FailOpen && !string.IsNullOrWhiteSpace(parseError))
                    return PreToolUseHookResult.Block(parseError) with
                    {
                        UpdatedInput = currentInput,
                    };

                continue;
            }

            if (TryGetString(response, "action") is { } action &&
                string.Equals(action, "block", StringComparison.OrdinalIgnoreCase))
            {
                return PreToolUseHookResult.Block(TryGetString(response, "message")) with
                {
                    UpdatedInput = currentInput,
                };
            }

            if (TryGetProperty(response, "updatedInput", out var updatedInput) ||
                TryGetProperty(response, "input", out updatedInput))
            {
                currentInput = updatedInput.Clone();
            }
        }

        return PreToolUseHookResult.Continue(currentInput);
    }

    public override async ValueTask<PermissionRequestHookResult> OnPermissionRequestAsync(
        PermissionRequestHookContext context,
        CancellationToken cancellationToken = default)
    {
        foreach (var command in GetCommands(HookEventKind.PermissionRequest))
        {
            var execution = await ExecuteAsync(
                command,
                context,
                context.ToolExecutionContext.WorkingDirectory,
                context.Input,
                cancellationToken);

            if (!execution.Succeeded)
            {
                if (!command.FailOpen)
                    return PermissionRequestHookResult.Deny(FormatFailureMessage(command, execution));

                continue;
            }

            if (!TryParseJsonObject(execution.Stdout, out var response, out var parseError))
            {
                if (!command.FailOpen && !string.IsNullOrWhiteSpace(parseError))
                    return PermissionRequestHookResult.Deny(parseError);

                continue;
            }

            if (TryGetProperty(response, "approved", out var approvedProperty) &&
                approvedProperty.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return approvedProperty.GetBoolean()
                    ? PermissionRequestHookResult.Allow(TryGetString(response, "message"))
                    : PermissionRequestHookResult.Deny(TryGetString(response, "message"));
            }

            var decision = TryGetString(response, "decision") ?? TryGetString(response, "action");
            if (string.IsNullOrWhiteSpace(decision))
                continue;

            if (decision.Equals("allow", StringComparison.OrdinalIgnoreCase) ||
                decision.Equals("approve", StringComparison.OrdinalIgnoreCase))
            {
                return PermissionRequestHookResult.Allow(TryGetString(response, "message"));
            }

            if (decision.Equals("deny", StringComparison.OrdinalIgnoreCase) ||
                decision.Equals("block", StringComparison.OrdinalIgnoreCase))
            {
                return PermissionRequestHookResult.Deny(TryGetString(response, "message"));
            }
        }

        return PermissionRequestHookResult.NoDecision();
    }

    public override ValueTask OnPostToolUseAsync(
        PostToolUseHookContext context,
        CancellationToken cancellationToken = default) =>
        RunNonBlockingHooksAsync(
            HookEventKind.PostToolUse,
            context,
            context.ToolExecutionContext.WorkingDirectory,
            context.Input,
            cancellationToken);

    public override ValueTask OnPostToolUseFailureAsync(
        PostToolUseHookContext context,
        CancellationToken cancellationToken = default) =>
        RunNonBlockingHooksAsync(
            HookEventKind.PostToolUseFailure,
            context,
            context.ToolExecutionContext.WorkingDirectory,
            context.Input,
            cancellationToken);

    public override ValueTask OnSessionStartAsync(
        SessionHookContext context,
        CancellationToken cancellationToken = default) =>
        RunNonBlockingHooksAsync(
            HookEventKind.SessionStart,
            context,
            context.WorkingDirectory,
            null,
            cancellationToken);

    public override ValueTask OnSessionEndAsync(
        SessionEndHookContext context,
        CancellationToken cancellationToken = default) =>
        RunNonBlockingHooksAsync(
            HookEventKind.SessionEnd,
            context,
            context.WorkingDirectory,
            null,
            cancellationToken);

    public override ValueTask OnPreCompactAsync(
        CompactHookContext context,
        CancellationToken cancellationToken = default) =>
        RunNonBlockingHooksAsync(
            HookEventKind.PreCompact,
            context,
            Environment.CurrentDirectory,
            null,
            cancellationToken);

    public override ValueTask OnPostCompactAsync(
        CompactHookContext context,
        CancellationToken cancellationToken = default) =>
        RunNonBlockingHooksAsync(
            HookEventKind.PostCompact,
            context,
            Environment.CurrentDirectory,
            null,
            cancellationToken);

    public override ValueTask OnStopAsync(
        StopHookContext context,
        CancellationToken cancellationToken = default) =>
        RunNonBlockingHooksAsync(
            HookEventKind.Stop,
            context,
            context.WorkingDirectory,
            null,
            cancellationToken);

    public override ValueTask OnStopFailureAsync(
        StopHookContext context,
        CancellationToken cancellationToken = default) =>
        RunNonBlockingHooksAsync(
            HookEventKind.StopFailure,
            context,
            context.WorkingDirectory,
            null,
            cancellationToken);

    private async ValueTask RunNonBlockingHooksAsync(
        HookEventKind eventKind,
        HookContext context,
        string fallbackWorkingDirectory,
        JsonElement? inputOverride,
        CancellationToken cancellationToken)
    {
        foreach (var command in GetCommands(eventKind))
        {
            await ExecuteAsync(
                command,
                context,
                fallbackWorkingDirectory,
                inputOverride,
                cancellationToken);
        }
    }

    private Task<HookCommandExecutionResult> ExecuteAsync(
        HookCommandDefinition command,
        HookContext context,
        string fallbackWorkingDirectory,
        JsonElement? inputOverride,
        CancellationToken cancellationToken)
    {
        var payload = BuildPayloadJson(context, inputOverride);
        var environment = BuildAmbientEnvironment(context, inputOverride);
        return _runner.ExecuteAsync(
            command,
            payload,
            fallbackWorkingDirectory,
            environment,
            cancellationToken);
    }

    private IReadOnlyList<HookCommandDefinition> GetCommands(HookEventKind eventKind) =>
        _commandsByEvent.TryGetValue(eventKind, out var commands)
            ? commands
            : [];

    private static IReadOnlyDictionary<string, string> BuildAmbientEnvironment(
        HookContext context,
        JsonElement? inputOverride)
    {
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["CLAUDESHARP_HOOK_EVENT"] = context.Kind.ToString(),
            ["CLAUDESHARP_HOOK_TIMESTAMP"] = context.Timestamp.ToString("O"),
        };

        switch (context)
        {
            case PreToolUseHookContext pre:
                environment["CLAUDESHARP_TOOL_NAME"] = pre.Tool.Name;
                environment["CLAUDESHARP_WORKDIR"] = pre.ToolExecutionContext.WorkingDirectory;
                environment["CLAUDESHARP_MODEL"] = pre.ToolExecutionContext.MainLoopModel;
                break;

            case PostToolUseHookContext post:
                environment["CLAUDESHARP_TOOL_NAME"] = post.Tool.Name;
                environment["CLAUDESHARP_WORKDIR"] = post.ToolExecutionContext.WorkingDirectory;
                environment["CLAUDESHARP_MODEL"] = post.ToolExecutionContext.MainLoopModel;
                break;

            case PermissionRequestHookContext permission:
                environment["CLAUDESHARP_TOOL_NAME"] = permission.Tool.Name;
                environment["CLAUDESHARP_WORKDIR"] = permission.ToolExecutionContext.WorkingDirectory;
                environment["CLAUDESHARP_MODEL"] = permission.ToolExecutionContext.MainLoopModel;
                break;

            case SessionHookContext session:
                environment["CLAUDESHARP_WORKDIR"] = session.WorkingDirectory;
                environment["CLAUDESHARP_MODEL"] = session.Model;
                if (!string.IsNullOrWhiteSpace(session.SessionId))
                    environment["CLAUDESHARP_SESSION_ID"] = session.SessionId;
                break;

            case SessionEndHookContext sessionEnd:
                environment["CLAUDESHARP_WORKDIR"] = sessionEnd.WorkingDirectory;
                environment["CLAUDESHARP_MODEL"] = sessionEnd.Model;
                if (!string.IsNullOrWhiteSpace(sessionEnd.SessionId))
                    environment["CLAUDESHARP_SESSION_ID"] = sessionEnd.SessionId;
                break;

            case StopHookContext stop:
                environment["CLAUDESHARP_WORKDIR"] = stop.WorkingDirectory;
                environment["CLAUDESHARP_MODEL"] = stop.Model;
                if (!string.IsNullOrWhiteSpace(stop.SessionId))
                    environment["CLAUDESHARP_SESSION_ID"] = stop.SessionId;
                break;
        }

        if (inputOverride.HasValue &&
            inputOverride.Value.ValueKind != JsonValueKind.Undefined)
        {
            environment["CLAUDESHARP_HAS_INPUT"] = "true";
        }

        return environment;
    }

    private static string BuildPayloadJson(HookContext context, JsonElement? inputOverride)
    {
        return context switch
        {
            PreToolUseHookContext pre => JsonSerializer.Serialize(new
            {
                @event = pre.Kind.ToString(),
                timestamp = pre.Timestamp,
                workingDirectory = pre.ToolExecutionContext.WorkingDirectory,
                model = pre.ToolExecutionContext.MainLoopModel,
                tool = new
                {
                    name = pre.Tool.Name,
                    displayName = pre.Tool.GetUserFacingName(inputOverride ?? pre.Input),
                },
                invocation = new
                {
                    id = pre.Invocation.ToolUseId,
                    name = pre.Invocation.Name,
                },
                input = inputOverride ?? pre.Input,
            }),
            PostToolUseHookContext post => JsonSerializer.Serialize(new
            {
                @event = post.Kind.ToString(),
                timestamp = post.Timestamp,
                workingDirectory = post.ToolExecutionContext.WorkingDirectory,
                model = post.ToolExecutionContext.MainLoopModel,
                tool = new
                {
                    name = post.Tool.Name,
                    displayName = post.Tool.GetUserFacingName(inputOverride ?? post.Input),
                },
                invocation = new
                {
                    id = post.Invocation.ToolUseId,
                    name = post.Invocation.Name,
                },
                input = inputOverride ?? post.Input,
                result = new
                {
                    data = post.Result.Data,
                    isError = post.Result.IsError,
                },
            }),
            PermissionRequestHookContext permission => JsonSerializer.Serialize(new
            {
                @event = permission.Kind.ToString(),
                timestamp = permission.Timestamp,
                workingDirectory = permission.ToolExecutionContext.WorkingDirectory,
                model = permission.ToolExecutionContext.MainLoopModel,
                tool = new
                {
                    name = permission.Tool.Name,
                    displayName = permission.Tool.GetUserFacingName(permission.Input),
                },
                invocation = new
                {
                    id = permission.Invocation.ToolUseId,
                    name = permission.Invocation.Name,
                },
                input = inputOverride ?? permission.Input,
                description = permission.Description,
            }),
            SessionHookContext session => JsonSerializer.Serialize(new
            {
                @event = session.Kind.ToString(),
                timestamp = session.Timestamp,
                sessionId = session.SessionId,
                workingDirectory = session.WorkingDirectory,
                model = session.Model,
                messageCount = session.MessageCount,
            }),
            SessionEndHookContext sessionEnd => JsonSerializer.Serialize(new
            {
                @event = sessionEnd.Kind.ToString(),
                timestamp = sessionEnd.Timestamp,
                sessionId = sessionEnd.SessionId,
                workingDirectory = sessionEnd.WorkingDirectory,
                model = sessionEnd.Model,
                messageCount = sessionEnd.MessageCount,
                dueToClear = sessionEnd.DueToClear,
            }),
            CompactHookContext compact => JsonSerializer.Serialize(new
            {
                @event = compact.Kind.ToString(),
                timestamp = compact.Timestamp,
                kind = compact.KindOfCompaction.ToString(),
                automatic = compact.Automatic,
                reason = compact.Reason,
                preserveTailCount = compact.PreserveTailCount,
                messageCount = compact.MessageCount,
            }),
            StopHookContext stop => JsonSerializer.Serialize(new
            {
                @event = stop.Kind.ToString(),
                timestamp = stop.Timestamp,
                sessionId = stop.SessionId,
                workingDirectory = stop.WorkingDirectory,
                model = stop.Model,
                success = stop.Success,
                errorMessage = stop.ErrorMessage,
                durationMs = stop.Duration.TotalMilliseconds,
                turnCount = stop.TurnCount,
                usage = new
                {
                    inputTokens = stop.TotalUsage.InputTokens,
                    outputTokens = stop.TotalUsage.OutputTokens,
                    cacheCreationInputTokens = stop.TotalUsage.CacheCreationInputTokens,
                    cacheReadInputTokens = stop.TotalUsage.CacheReadInputTokens,
                },
            }),
            _ => JsonSerializer.Serialize(new
            {
                @event = context.Kind.ToString(),
                timestamp = context.Timestamp,
            }),
        };
    }

    private static string FormatFailureMessage(
        HookCommandDefinition command,
        HookCommandExecutionResult execution)
    {
        if (execution.TimedOut)
            return $"Hook command timed out after {command.TimeoutMs}ms: {command.Command}";

        var stderr = string.IsNullOrWhiteSpace(execution.Stderr)
            ? string.Empty
            : $" STDERR: {execution.Stderr.Trim()}";
        return $"Hook command failed with exit code {execution.ExitCode}: {command.Command}.{stderr}".Trim();
    }

    private static bool TryParseJsonObject(
        string text,
        out JsonElement element,
        out string? error)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            element = default;
            error = null;
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                element = default;
                error = "Hook command output must be a JSON object.";
                return false;
            }

            element = document.RootElement.Clone();
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            element = default;
            error = $"Hook command returned invalid JSON: {ex.Message}";
            return false;
        }
    }

    private static bool TryGetProperty(
        JsonElement element,
        string propertyName,
        out JsonElement property)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            property = default;
            return false;
        }

        foreach (var candidate in element.EnumerateObject())
        {
            if (string.Equals(candidate.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                property = candidate.Value;
                return true;
            }
        }

        property = default;
        return false;
    }

    private static string? TryGetString(JsonElement element, string propertyName) =>
        TryGetProperty(element, propertyName, out var property) &&
        property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
}

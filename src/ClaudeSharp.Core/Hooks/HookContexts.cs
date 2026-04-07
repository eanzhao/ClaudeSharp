using System.Text.Json;
using ClaudeSharp.Core.Compaction;
using ClaudeSharp.Core.Messages;
using ClaudeSharp.Core.Permissions;
using ClaudeSharp.Core.Storage;
using ClaudeSharp.Core.Tools;

namespace ClaudeSharp.Core.Hooks;

/// <summary>
/// Represents hook context.
/// </summary>
public abstract class HookContext
{
    protected HookContext(HookEventKind kind, DateTimeOffset? timestamp = null)
    {
        Kind = kind;
        Timestamp = timestamp ?? DateTimeOffset.UtcNow;
    }

    public HookEventKind Kind { get; }
    public DateTimeOffset Timestamp { get; }
}

/// <summary>
/// Represents pre tool use hook context.
/// </summary>
public sealed class PreToolUseHookContext : HookContext
{
    public PreToolUseHookContext(
        ToolUseBlock invocation,
        ITool tool,
        ToolExecutionContext toolExecutionContext,
        JsonElement input,
        DateTimeOffset? timestamp = null)
        : base(HookEventKind.PreToolUse, timestamp)
    {
        Invocation = invocation;
        Tool = tool;
        ToolExecutionContext = toolExecutionContext;
        Input = input;
    }

    public ToolUseBlock Invocation { get; }
    public ITool Tool { get; }
    public ToolExecutionContext ToolExecutionContext { get; }
    public JsonElement Input { get; set; }
}

/// <summary>
/// Represents post tool use hook context.
/// </summary>
public sealed class PostToolUseHookContext : HookContext
{
    public PostToolUseHookContext(
        ToolUseBlock invocation,
        ITool tool,
        ToolExecutionContext toolExecutionContext,
        JsonElement input,
        ToolResult result,
        DateTimeOffset? timestamp = null)
        : base(result.IsError ? HookEventKind.PostToolUseFailure : HookEventKind.PostToolUse, timestamp)
    {
        Invocation = invocation;
        Tool = tool;
        ToolExecutionContext = toolExecutionContext;
        Input = input;
        Result = result;
    }

    public ToolUseBlock Invocation { get; }
    public ITool Tool { get; }
    public ToolExecutionContext ToolExecutionContext { get; }
    public JsonElement Input { get; }
    public ToolResult Result { get; }
}

/// <summary>
/// Represents permission request hook context.
/// </summary>
public sealed class PermissionRequestHookContext : HookContext
{
    public PermissionRequestHookContext(
        ToolUseBlock invocation,
        ITool tool,
        ToolExecutionContext toolExecutionContext,
        JsonElement input,
        string description,
        DateTimeOffset? timestamp = null)
        : base(HookEventKind.PermissionRequest, timestamp)
    {
        Invocation = invocation;
        Tool = tool;
        ToolExecutionContext = toolExecutionContext;
        Input = input;
        Description = description;
    }

    public ToolUseBlock Invocation { get; }
    public ITool Tool { get; }
    public ToolExecutionContext ToolExecutionContext { get; }
    public JsonElement Input { get; }
    public string Description { get; }
}

/// <summary>
/// Represents session hook context.
/// </summary>
public sealed class SessionHookContext : HookContext
{
    public SessionHookContext(
        string? sessionId,
        string workingDirectory,
        string model,
        ConversationSessionMetadata metadata,
        int messageCount,
        DateTimeOffset? timestamp = null)
        : base(HookEventKind.SessionStart, timestamp)
    {
        SessionId = sessionId;
        WorkingDirectory = workingDirectory;
        Model = model;
        Metadata = metadata;
        MessageCount = messageCount;
    }

    public string? SessionId { get; }
    public string WorkingDirectory { get; }
    public string Model { get; }
    public ConversationSessionMetadata Metadata { get; }
    public int MessageCount { get; }
}

/// <summary>
/// Represents session end hook context.
/// </summary>
public sealed class SessionEndHookContext : HookContext
{
    public SessionEndHookContext(
        string? sessionId,
        string workingDirectory,
        string model,
        ConversationSessionMetadata metadata,
        int messageCount,
        bool dueToClear,
        DateTimeOffset? timestamp = null)
        : base(HookEventKind.SessionEnd, timestamp)
    {
        SessionId = sessionId;
        WorkingDirectory = workingDirectory;
        Model = model;
        Metadata = metadata;
        MessageCount = messageCount;
        DueToClear = dueToClear;
    }

    public string? SessionId { get; }
    public string WorkingDirectory { get; }
    public string Model { get; }
    public ConversationSessionMetadata Metadata { get; }
    public int MessageCount { get; }
    public bool DueToClear { get; }
}

/// <summary>
/// Defines compaction lifecycle kind values.
/// </summary>
public enum CompactionLifecycleKind
{
    Conversation,
    SessionMemory,
    Microcompact,
}

/// <summary>
/// Represents compact hook context.
/// </summary>
public sealed class CompactHookContext : HookContext
{
    public CompactHookContext(
        CompactionLifecycleKind kind,
        bool automatic,
        string reason,
        int preserveTailCount,
        int messageCount,
        ConversationCompactionResult? conversationResult = null,
        SessionMemoryCompactionResult? sessionMemoryResult = null,
        MicrocompactResult? microcompactResult = null,
        DateTimeOffset? timestamp = null)
        : base(
            conversationResult != null ||
            sessionMemoryResult != null ||
            microcompactResult != null
                ? HookEventKind.PostCompact
                : HookEventKind.PreCompact,
            timestamp)
    {
        KindOfCompaction = kind;
        Automatic = automatic;
        Reason = reason;
        PreserveTailCount = preserveTailCount;
        MessageCount = messageCount;
        ConversationResult = conversationResult;
        SessionMemoryResult = sessionMemoryResult;
        MicrocompactResult = microcompactResult;
    }

    public CompactionLifecycleKind KindOfCompaction { get; }
    public bool Automatic { get; }
    public string Reason { get; }
    public int PreserveTailCount { get; }
    public int MessageCount { get; }
    public ConversationCompactionResult? ConversationResult { get; }
    public SessionMemoryCompactionResult? SessionMemoryResult { get; }
    public MicrocompactResult? MicrocompactResult { get; }
}

/// <summary>
/// Represents stop hook context.
/// </summary>
public sealed class StopHookContext : HookContext
{
    public StopHookContext(
        string? sessionId,
        string workingDirectory,
        string model,
        bool success,
        string? errorMessage,
        TimeSpan duration,
        int turnCount,
        TokenUsage totalUsage,
        DateTimeOffset? timestamp = null)
        : base(success ? HookEventKind.Stop : HookEventKind.StopFailure, timestamp)
    {
        SessionId = sessionId;
        WorkingDirectory = workingDirectory;
        Model = model;
        Success = success;
        ErrorMessage = errorMessage;
        Duration = duration;
        TurnCount = turnCount;
        TotalUsage = totalUsage;
    }

    public string? SessionId { get; }
    public string WorkingDirectory { get; }
    public string Model { get; }
    public bool Success { get; }
    public string? ErrorMessage { get; }
    public TimeSpan Duration { get; }
    public int TurnCount { get; }
    public TokenUsage TotalUsage { get; }
}


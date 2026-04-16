using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aexon.Core.Agents;
using Aexon.Core.Tools;

namespace Aexon.Tools;

public sealed class EnterWorktreeToolInput
{
    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("list_only")]
    public bool ListOnly { get; set; }

    [JsonPropertyName("cleanup_unchanged")]
    public bool CleanupUnchanged { get; set; } = true;
}

public sealed class ExitWorktreeToolInput
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("force")]
    public bool Force { get; set; }
}

public sealed class EnterWorktreeTool : ITool
{
    private readonly IAgentManagedWorktreeRuntime _runtime;

    public EnterWorktreeTool(IAgentManagedWorktreeRuntime runtime)
    {
        _runtime = runtime;
    }

    public string Name => "EnterWorktree";

    public string[] Aliases => ["EnterWorktreeTool"];

    public Task<string> GetDescriptionAsync() =>
        Task.FromResult("Create a managed git worktree, or list the currently active managed worktrees.");

    public JsonElement GetInputSchema() => JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "path": { "type": "string" },
            "name": { "type": "string" },
            "list_only": { "type": "boolean" },
            "cleanup_unchanged": { "type": "boolean" }
          },
          "additionalProperties": false
        }
        """).RootElement.Clone();

    public Task<string> GetPromptAsync(ToolPromptContext context) =>
        Task.FromResult("""
            Use EnterWorktree to create an isolated git worktree for task-specific work.

            The response includes a worktree id and the working directory you can pass to Agent via worktree_id.
            Set list_only=true to inspect currently active managed worktrees.
            """);

    public bool IsReadOnly(JsonElement input)
    {
        var parsed = JsonSerializer.Deserialize<EnterWorktreeToolInput>(input);
        return parsed?.ListOnly == true;
    }

    public bool IsConcurrencySafe(JsonElement input)
    {
        var parsed = JsonSerializer.Deserialize<EnterWorktreeToolInput>(input);
        return parsed?.ListOnly == true;
    }

    public async Task<ToolResult> ExecuteAsync(
        JsonElement input,
        ToolExecutionContext context,
        IProgress<ToolProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var parsed = JsonSerializer.Deserialize<EnterWorktreeToolInput>(input) ?? new EnterWorktreeToolInput();
        if (parsed.ListOnly)
            return ToolResult.Success(FormatWorktrees(_runtime.List()));

        var workingDirectory = string.IsNullOrWhiteSpace(parsed.Path)
            ? context.WorkingDirectory
            : Path.GetFullPath(Path.Combine(context.WorkingDirectory, parsed.Path));

        try
        {
            var result = await _runtime.EnterAsync(
                workingDirectory,
                parsed.Name,
                parsed.CleanupUnchanged,
                cancellationToken);
            var builder = new StringBuilder();
            builder.AppendLine("Created worktree");
            builder.AppendLine();
            builder.AppendLine($"- id: {result.Worktree.Id}");
            builder.AppendLine($"  source: {result.Worktree.SourceWorkingDirectory}");
            builder.AppendLine($"  root: {result.Worktree.RootDirectory}");
            builder.AppendLine($"  working_directory: {result.Worktree.WorkingDirectory}");
            if (!string.IsNullOrWhiteSpace(result.Worktree.Name))
                builder.AppendLine($"  name: {result.Worktree.Name}");
            if (result.AutoCleanedCount > 0)
                builder.AppendLine($"  auto_cleaned: {result.AutoCleanedCount}");
            builder.AppendLine($"  use_with_agent: worktree_id=\"{result.Worktree.Id}\"");
            return ToolResult.Success(builder.ToString().TrimEnd());
        }
        catch (Exception ex)
        {
            return ToolResult.Error(ex.Message);
        }
    }

    public string GetUserFacingName(JsonElement? input = null) => "Enter worktree";

    public string? GetActivityDescription(JsonElement? input) => "Creating worktree";

    private static string FormatWorktrees(IReadOnlyList<AgentManagedWorktree> worktrees)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Managed worktrees:");
        if (worktrees.Count == 0)
        {
            builder.AppendLine("(none)");
        }
        else
        {
            foreach (var worktree in worktrees)
            {
                builder.Append("- ")
                    .Append(worktree.Id)
                    .Append(" -> ")
                    .Append(worktree.WorkingDirectory);
                if (!string.IsNullOrWhiteSpace(worktree.Name))
                    builder.Append(" (").Append(worktree.Name).Append(')');
                builder.AppendLine();
            }
        }

        return builder.ToString().TrimEnd();
    }
}

public sealed class ExitWorktreeTool : ITool
{
    private readonly IAgentManagedWorktreeRuntime _runtime;

    public ExitWorktreeTool(IAgentManagedWorktreeRuntime runtime)
    {
        _runtime = runtime;
    }

    public string Name => "ExitWorktree";

    public string[] Aliases => ["ExitWorktreeTool"];

    public Task<string> GetDescriptionAsync() =>
        Task.FromResult("Exit a managed git worktree and clean it up. Dirty worktrees require force=true.");

    public JsonElement GetInputSchema() => JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "id": { "type": "string" },
            "force": { "type": "boolean" }
          },
          "required": ["id"],
          "additionalProperties": false
        }
        """).RootElement.Clone();

    public Task<string> GetPromptAsync(ToolPromptContext context) =>
        Task.FromResult("Use ExitWorktree to clean up a managed worktree when you are done with it.");

    public Task<ValidationResult> ValidateInputAsync(JsonElement input, ToolExecutionContext context)
    {
        var parsed = JsonSerializer.Deserialize<ExitWorktreeToolInput>(input);
        return Task.FromResult(string.IsNullOrWhiteSpace(parsed?.Id)
            ? ValidationResult.Invalid("id is required.")
            : ValidationResult.Valid());
    }

    public async Task<ToolResult> ExecuteAsync(
        JsonElement input,
        ToolExecutionContext context,
        IProgress<ToolProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var parsed = JsonSerializer.Deserialize<ExitWorktreeToolInput>(input);
        if (string.IsNullOrWhiteSpace(parsed?.Id))
            return ToolResult.Error("id is required.");

        var result = await _runtime.ExitAsync(parsed.Id.Trim(), parsed.Force, cancellationToken);
        return result.Status switch
        {
            AgentManagedWorktreeExitStatus.NotFound =>
                ToolResult.Error($"No managed worktree matched id '{parsed.Id.Trim()}'."),
            AgentManagedWorktreeExitStatus.HasChanges =>
                ToolResult.Error($"Managed worktree '{parsed.Id.Trim()}' has uncommitted changes. Re-run with force=true to remove it."),
            _ => ToolResult.Success($"Exited and cleaned worktree '{parsed.Id.Trim()}'."),
        };
    }

    public string GetUserFacingName(JsonElement? input = null) => "Exit worktree";

    public string? GetActivityDescription(JsonElement? input) => "Cleaning worktree";
}

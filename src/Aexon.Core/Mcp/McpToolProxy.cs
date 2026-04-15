using System.Text.Json;
using Aexon.Core.Permissions;
using Aexon.Core.Tools;

namespace Aexon.Core.Mcp;

/// <summary>
/// Exposes a remote MCP tool as a local Aexon tool.
/// </summary>
public sealed class McpToolProxy : ITool
{
    private readonly IMcpClientSession _session;
    private readonly string _description;

    public McpToolProxy(
        string name,
        string serverId,
        string remoteToolName,
        McpToolDescriptor descriptor,
        IMcpClientSession session)
    {
        Name = name;
        ServerId = serverId;
        RemoteToolName = remoteToolName;
        Descriptor = descriptor;
        _session = session;
        _description = BuildDescription(descriptor);
    }

    public string Name { get; }
    public string ServerId { get; }
    public string RemoteToolName { get; }
    public McpToolDescriptor Descriptor { get; }

    public Task<string> GetDescriptionAsync() => Task.FromResult(_description);

    public JsonElement GetInputSchema() => Descriptor.InputSchema.Clone();

    public Task<string> GetPromptAsync(ToolPromptContext context)
    {
        return Task.FromResult(
            $"Calls the MCP tool {RemoteToolName} from server {ServerId}. Use it when its remote capability is the best match for the task.");
    }

    public async Task<ToolResult> ExecuteAsync(
        JsonElement input,
        ToolExecutionContext context,
        IProgress<ToolProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _session.CallToolAsync(
            RemoteToolName,
            input,
            cancellationToken);

        return result.IsError
            ? ToolResult.Error(result.Text)
            : ToolResult.Success(result.Text);
    }

    public Task<PermissionResult> CheckPermissionsAsync(JsonElement input, ToolExecutionContext context)
    {
        if (Descriptor.ReadOnlyHint && !Descriptor.OpenWorldHint)
            return Task.FromResult(PermissionResult.Allow());

        return Task.FromResult(PermissionResult.Ask($"Allow {GetUserFacingName(input)}?"));
    }

    public bool IsReadOnly(JsonElement input) => Descriptor.ReadOnlyHint;

    public bool IsConcurrencySafe(JsonElement input) => false;

    public string GetUserFacingName(JsonElement? input = null) =>
        $"{RemoteToolName} (MCP:{ServerId})";

    public string? GetActivityDescription(JsonElement? input) =>
        $"Calling {RemoteToolName} on MCP server {ServerId}";

    private string BuildDescription(McpToolDescriptor descriptor)
    {
        var title = descriptor.Metadata.TryGetValue("title", out var value) ? value : null;
        var titleText = string.IsNullOrWhiteSpace(title) ? string.Empty : $"Title: {title}. ";
        var readOnlyText = descriptor.ReadOnlyHint
            ? "This tool is marked read-only."
            : "This tool may modify external state.";
        var openWorldText = descriptor.OpenWorldHint
            ? "It may interact with open-world systems."
            : "It is expected to stay within the configured server boundary.";

        return $"{titleText}Remote MCP tool {RemoteToolName} from server {ServerId}. {descriptor.Description} {readOnlyText} {openWorldText}".Trim();
    }
}

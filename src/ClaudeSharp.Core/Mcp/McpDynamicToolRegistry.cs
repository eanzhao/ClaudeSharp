namespace ClaudeSharp.Core.Mcp;

/// <summary>
/// Represents a tool registered from an MCP server.
/// </summary>
public sealed record McpRegisteredTool(
    string ServerId,
    McpToolDescriptor Descriptor)
{
    public string QualifiedName => $"{ServerId}:{Descriptor.Name}";
}

/// <summary>
/// Tracks MCP tools that are discovered at runtime.
/// </summary>
public sealed class McpDynamicToolRegistry
{
    private readonly Dictionary<string, Dictionary<string, McpToolDescriptor>> _toolsByServer =
        new(StringComparer.OrdinalIgnoreCase);

    public void ReplaceServerTools(
        string serverId,
        IEnumerable<McpToolDescriptor> tools)
    {
        _toolsByServer[serverId] = tools
            .ToDictionary(tool => tool.Name, tool => tool, StringComparer.OrdinalIgnoreCase);
    }

    public bool RemoveServer(string serverId) =>
        _toolsByServer.Remove(serverId);

    public bool TryGet(
        string serverId,
        string toolName,
        out McpRegisteredTool? tool)
    {
        tool = null;
        if (!_toolsByServer.TryGetValue(serverId, out var serverTools) ||
            !serverTools.TryGetValue(toolName, out var descriptor))
        {
            return false;
        }

        tool = new McpRegisteredTool(serverId, descriptor);
        return true;
    }

    public bool TryGetQualified(
        string qualifiedName,
        out McpRegisteredTool? tool)
    {
        tool = null;
        var separatorIndex = qualifiedName.IndexOf(':');
        if (separatorIndex <= 0 || separatorIndex >= qualifiedName.Length - 1)
            return false;

        var serverId = qualifiedName[..separatorIndex];
        var toolName = qualifiedName[(separatorIndex + 1)..];
        return TryGet(serverId, toolName, out tool);
    }

    public IReadOnlyList<McpRegisteredTool> GetAll() =>
        _toolsByServer
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .SelectMany(pair => pair.Value.Values.Select(descriptor =>
                new McpRegisteredTool(pair.Key, descriptor)))
            .OrderBy(tool => tool.QualifiedName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
}

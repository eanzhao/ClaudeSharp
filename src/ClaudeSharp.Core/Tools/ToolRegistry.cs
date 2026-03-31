using System.Text.Json;

namespace ClaudeSharp.Core.Tools;

/// <summary>
/// 工具注册表 — 对应 Claude Code 的 tools.ts (getAllBaseTools, getTools, assembleToolPool)
///
/// Claude Code 中工具注册表的设计要点：
/// 1. getAllBaseTools(): 返回所有可能的工具（受 feature flag 控制）
/// 2. getTools(): 根据权限过滤掉 deny 的工具
/// 3. assembleToolPool(): 合并内置工具和 MCP 工具，去重
/// 4. 工具按名称排序以保证 prompt cache 稳定性
/// </summary>
public class ToolRegistry
{
    private readonly Dictionary<string, ITool> _tools = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ITool> _aliases = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>注册一个工具</summary>
    public void Register(ITool tool)
    {
        _tools[tool.Name] = tool;
        foreach (var alias in tool.Aliases)
            _aliases[alias] = tool;
    }

    /// <summary>按名称或别名获取工具</summary>
    public ITool? Get(string name)
    {
        if (_tools.TryGetValue(name, out var tool))
            return tool;
        if (_aliases.TryGetValue(name, out tool))
            return tool;
        return null;
    }

    /// <summary>获取所有已启用的工具 — 对应 getTools()</summary>
    public IReadOnlyList<ITool> GetEnabledTools()
    {
        return _tools.Values
            .Where(t => t.IsEnabled())
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>获取所有工具 — 对应 getAllBaseTools()</summary>
    public IReadOnlyList<ITool> GetAllTools() =>
        _tools.Values.OrderBy(t => t.Name, StringComparer.Ordinal).ToList();

    /// <summary>
    /// 生成 API 工具定义列表 — 对应构建 tool schema 发送给 Anthropic API
    /// </summary>
    public IReadOnlyList<JsonElement> GetToolDefinitions()
    {
        var tools = GetEnabledTools();
        var definitions = new List<JsonElement>();

        foreach (var tool in tools)
        {
            var schema = tool.GetInputSchema();
            var def = JsonSerializer.SerializeToElement(new
            {
                name = tool.Name,
                description = tool.GetDescriptionAsync().GetAwaiter().GetResult(),
                input_schema = schema,
            });
            definitions.Add(def);
        }

        return definitions;
    }

    /// <summary>
    /// 异步生成工具定义（推荐使用）
    /// </summary>
    public async Task<IReadOnlyList<JsonElement>> GetToolDefinitionsAsync()
    {
        var tools = GetEnabledTools();
        var definitions = new List<JsonElement>();

        foreach (var tool in tools)
        {
            var description = await tool.GetDescriptionAsync();
            var schema = tool.GetInputSchema();
            var def = JsonSerializer.SerializeToElement(new
            {
                name = tool.Name,
                description,
                input_schema = schema,
            });
            definitions.Add(def);
        }

        return definitions;
    }
}

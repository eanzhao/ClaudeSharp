using System.Text.Json;

namespace Aexon.Core.Tools;

/// <summary>
/// Provides tool registry.
/// </summary>
public class ToolRegistry
{
    private readonly Dictionary<string, ITool> _tools = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ITool> _aliases = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Handles register.
    /// </summary>
    public void Register(ITool tool)
    {
        _tools[tool.Name] = tool;
        foreach (var alias in tool.Aliases)
            _aliases[alias] = tool;
    }

    /// <summary>
    /// Handles get.
    /// </summary>
    public ITool? Get(string name)
    {
        if (_tools.TryGetValue(name, out var tool))
            return tool;
        if (_aliases.TryGetValue(name, out tool))
            return tool;
        return null;
    }

    /// <summary>
    /// Gets enabled tools.
    /// </summary>
    public IReadOnlyList<ITool> GetEnabledTools(Func<ITool, bool>? predicate = null)
    {
        return _tools.Values
            .Where(t => t.IsEnabled() && (predicate == null || predicate(t)))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Gets all tools.
    /// </summary>
    public IReadOnlyList<ITool> GetAllTools() =>
        _tools.Values.OrderBy(t => t.Name, StringComparer.Ordinal).ToList();

    /// <summary>
    /// Gets tool definitions.
    /// </summary>
    public IReadOnlyList<JsonElement> GetToolDefinitions(Func<ITool, bool>? predicate = null)
    {
        var tools = GetEnabledTools(predicate);
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
    /// Gets tool definitions.
    /// </summary>
    public async Task<IReadOnlyList<JsonElement>> GetToolDefinitionsAsync(
        Func<ITool, bool>? predicate = null)
    {
        var tools = GetEnabledTools(predicate);
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

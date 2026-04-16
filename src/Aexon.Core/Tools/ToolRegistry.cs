using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aexon.Core.Tools;

public enum ToolRegistrationState
{
    Loaded,
    Deferred,
}

public sealed record DeferredToolRegistration(
    string Name,
    Func<ITool> Factory,
    IReadOnlyList<string>? Aliases = null,
    IReadOnlyList<string>? Keywords = null);

public sealed record ToolRegistryEntryInfo(
    string Name,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<string> Keywords,
    ToolRegistrationState State);

public sealed record ToolSchemaDefinition(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("input_schema")] JsonElement InputSchema)
{
    public JsonElement ToJsonElement() => JsonSerializer.SerializeToElement(this);
}

/// <summary>
/// Provides tool registry.
/// </summary>
public class ToolRegistry
{
    private readonly Dictionary<string, ToolRegistration> _tools = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ToolRegistration> _aliases = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Handles register.
    /// </summary>
    public void Register(ITool tool) =>
        AddOrReplace(ToolRegistration.CreateLoaded(tool));

    /// <summary>
    /// Registers a deferred tool that can be loaded on demand.
    /// </summary>
    public void RegisterDeferred(DeferredToolRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        AddOrReplace(ToolRegistration.CreateDeferred(registration));
    }

    /// <summary>
    /// Handles get.
    /// </summary>
    public ITool? Get(string name)
    {
        return TryGetRegistration(name, out var registration) &&
               registration.State == ToolRegistrationState.Loaded
            ? registration.Tool
            : null;
    }

    /// <summary>
    /// Loads a deferred tool and returns the active tool instance.
    /// </summary>
    public ITool? Load(string name)
    {
        if (!TryGetRegistration(name, out var registration))
            return null;

        var tool = registration.Load();
        RegisterAliases(registration);
        return tool;
    }

    /// <summary>
    /// Gets a tool instance for inspection without changing its loaded state.
    /// </summary>
    public ITool? Peek(string name)
    {
        if (!TryGetRegistration(name, out var registration))
            return null;

        return registration.CreatePreviewTool();
    }

    /// <summary>
    /// Gets a value indicating whether the tool is already loaded.
    /// </summary>
    public bool IsLoaded(string name) =>
        TryGetRegistration(name, out var registration) &&
        registration.State == ToolRegistrationState.Loaded;

    /// <summary>
    /// Gets all registered tools, including deferred entries.
    /// </summary>
    public IReadOnlyList<ToolRegistryEntryInfo> GetRegisteredTools() =>
        _tools.Values
            .OrderBy(registration => registration.Name, StringComparer.Ordinal)
            .Select(registration => new ToolRegistryEntryInfo(
                registration.Name,
                registration.LookupAliases.ToArray(),
                registration.Keywords.ToArray(),
                registration.State))
            .ToList();

    /// <summary>
    /// Gets a tool schema definition without necessarily loading the tool into the active session.
    /// </summary>
    public async Task<ToolSchemaDefinition?> DescribeAsync(
        string name,
        bool load = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryGetRegistration(name, out var registration))
            return null;

        var tool = load
            ? registration.Load()
            : registration.CreatePreviewTool();

        if (load)
            RegisterAliases(registration);

        return await CreateDefinitionAsync(tool);
    }

    /// <summary>
    /// Gets enabled tools.
    /// </summary>
    public IReadOnlyList<ITool> GetEnabledTools(Func<ITool, bool>? predicate = null)
    {
        return _tools.Values
            .Where(registration => registration.State == ToolRegistrationState.Loaded)
            .Select(registration => registration.Tool!)
            .Where(tool => tool.IsEnabled() && (predicate == null || predicate(tool)))
            .OrderBy(tool => tool.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Gets all tools.
    /// </summary>
    public IReadOnlyList<ITool> GetAllTools() =>
        _tools.Values
            .Where(registration => registration.State == ToolRegistrationState.Loaded)
            .Select(registration => registration.Tool!)
            .OrderBy(tool => tool.Name, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Gets tool definitions.
    /// </summary>
    public IReadOnlyList<JsonElement> GetToolDefinitions(Func<ITool, bool>? predicate = null)
    {
        var tools = GetEnabledTools(predicate);
        var definitions = new List<JsonElement>();

        foreach (var tool in tools)
        {
            definitions.Add(CreateDefinition(tool).ToJsonElement());
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
            definitions.Add((await CreateDefinitionAsync(tool)).ToJsonElement());
        }

        return definitions;
    }

    private static ToolSchemaDefinition CreateDefinition(ITool tool) =>
        new(
            tool.Name,
            tool.GetDescriptionAsync().GetAwaiter().GetResult(),
            tool.GetInputSchema());

    private static async Task<ToolSchemaDefinition> CreateDefinitionAsync(ITool tool) =>
        new(
            tool.Name,
            await tool.GetDescriptionAsync(),
            tool.GetInputSchema());

    private void AddOrReplace(ToolRegistration registration)
    {
        if (_tools.TryGetValue(registration.Name, out var existing))
            UnregisterAliases(existing);

        _tools[registration.Name] = registration;
        RegisterAliases(registration);
    }

    private bool TryGetRegistration(string name, out ToolRegistration registration)
    {
        if (_tools.TryGetValue(name, out registration!))
            return true;

        return _aliases.TryGetValue(name, out registration!);
    }

    private void RegisterAliases(ToolRegistration registration)
    {
        foreach (var alias in registration.LookupAliases)
            _aliases[alias] = registration;
    }

    private void UnregisterAliases(ToolRegistration registration)
    {
        foreach (var alias in registration.LookupAliases)
        {
            if (_aliases.TryGetValue(alias, out var existing) && ReferenceEquals(existing, registration))
                _aliases.Remove(alias);
        }
    }

    private sealed class ToolRegistration
    {
        private readonly Func<ITool>? _factory;
        private readonly string[] _registeredAliases;
        private readonly string[] _keywords;

        private ToolRegistration(
            string name,
            string[] aliases,
            string[] keywords,
            ITool? tool,
            Func<ITool>? factory,
            ToolRegistrationState state)
        {
            Name = name;
            _registeredAliases = aliases;
            _keywords = keywords;
            Tool = tool;
            _factory = factory;
            State = state;
        }

        public string Name { get; }

        public IReadOnlyList<string> LookupAliases =>
            BuildLookupAliases();

        public IReadOnlyList<string> Keywords => _keywords;

        public ToolRegistrationState State { get; private set; }

        public ITool? Tool { get; private set; }

        public static ToolRegistration CreateLoaded(ITool tool) =>
            new(
                tool.Name,
                NormalizeValues(tool.Aliases),
                [],
                tool,
                factory: null,
                ToolRegistrationState.Loaded);

        public static ToolRegistration CreateDeferred(DeferredToolRegistration registration) =>
            new(
                registration.Name,
                NormalizeValues(registration.Aliases),
                NormalizeValues(registration.Keywords),
                tool: null,
                registration.Factory,
                ToolRegistrationState.Deferred);

        public ITool Load()
        {
            if (Tool != null)
            {
                State = ToolRegistrationState.Loaded;
                return Tool;
            }

            if (_factory == null)
                throw new InvalidOperationException($"Tool {Name} is not loadable.");

            var tool = _factory();
            ValidateLoadedTool(tool);
            Tool = tool;
            State = ToolRegistrationState.Loaded;
            return tool;
        }

        public ITool CreatePreviewTool()
        {
            if (Tool != null)
                return Tool;

            if (_factory == null)
                throw new InvalidOperationException($"Tool {Name} is not loadable.");

            var tool = _factory();
            ValidateLoadedTool(tool);
            return tool;
        }

        private IReadOnlyList<string> BuildLookupAliases()
        {
            return _registeredAliases
                .Concat(Tool?.Aliases ?? [])
                .Where(alias => !string.Equals(alias, Name, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private void ValidateLoadedTool(ITool tool)
        {
            if (!string.Equals(tool.Name, Name, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Deferred tool registration expected {Name}, but factory returned {tool.Name}.");
            }
        }

        private static string[] NormalizeValues(IReadOnlyList<string>? values)
        {
            return values?
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
                ?? [];
        }
    }
}

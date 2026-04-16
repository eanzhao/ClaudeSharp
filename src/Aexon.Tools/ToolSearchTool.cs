using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Aexon.Core.Permissions;
using Aexon.Core.Tools;

namespace Aexon.Tools;

public sealed class ToolSearchToolInput
{
    [JsonPropertyName("query")]
    public string Query { get; set; } = "";

    [JsonPropertyName("max_results")]
    public int? MaxResults { get; set; }
}

/// <summary>
/// Searches registered tools and loads deferred tools on demand.
/// </summary>
public sealed partial class ToolSearchTool : ITool
{
    private const int DefaultMaxResults = 5;
    private const int MaxMaxResults = 20;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ToolRegistry _registry;

    public ToolSearchTool(ToolRegistry registry)
    {
        _registry = registry;
    }

    public string Name => "ToolSearch";

    public string[] Aliases => ["ToolSearchTool"];

    public Task<string> GetDescriptionAsync() =>
        Task.FromResult(
            "Search registered tools by name or keyword, inspect their schemas, and load deferred tools with query=\"select:ToolA,ToolB\".");

    public JsonElement GetInputSchema()
    {
        return JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "query": {
              "type": "string",
              "description": "Search text, or select:ToolA,ToolB to load specific tools"
            },
            "max_results": {
              "type": "integer",
              "description": "Maximum number of matches to return for normal search"
            }
          },
          "required": ["query"],
          "additionalProperties": false
        }
        """).RootElement.Clone();
    }

    public Task<string> GetPromptAsync(ToolPromptContext context)
    {
        return Task.FromResult("""
            Search for tools that are not currently loaded into the active tool set.

            Usage:
            - Use a normal query like "mailbox status" or "cron" to inspect matching tools and their schemas
            - Use query="select:ToolA,ToolB" to load one or more deferred tools into the current session
            - After a successful select call, the newly loaded tools become available on the next model turn
            - Prefer exact tool names inside select:
            """);
    }

    public Task<ValidationResult> ValidateInputAsync(JsonElement input, ToolExecutionContext context)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<ToolSearchToolInput>(input, JsonOptions);
            if (string.IsNullOrWhiteSpace(parsed?.Query))
                return Task.FromResult(ValidationResult.Invalid("query is required."));

            if (parsed.MaxResults is <= 0)
                return Task.FromResult(ValidationResult.Invalid("max_results must be greater than 0."));

            return Task.FromResult(ValidationResult.Valid());
        }
        catch (JsonException ex)
        {
            return Task.FromResult(ValidationResult.Invalid(ex.Message));
        }
    }

    public Task<PermissionResult> CheckPermissionsAsync(JsonElement input, ToolExecutionContext context) =>
        Task.FromResult(PermissionResult.Allow());

    public bool IsReadOnly(JsonElement input) =>
        !TryGetQuery(input, out var query) || !IsSelectQuery(query);

    public bool IsConcurrencySafe(JsonElement input) => IsReadOnly(input);

    public string? GetActivityDescription(JsonElement? input)
    {
        if (!TryGetQuery(input, out var query))
            return "Searching tools";

        return IsSelectQuery(query)
            ? "Loading tools"
            : $"Searching tools for \"{query.Trim()}\"";
    }

    public int MaxResultSizeChars => 200_000;

    public async Task<ToolResult> ExecuteAsync(
        JsonElement input,
        ToolExecutionContext context,
        IProgress<ToolProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var parsed = JsonSerializer.Deserialize<ToolSearchToolInput>(input, JsonOptions);
        if (parsed == null || string.IsNullOrWhiteSpace(parsed.Query))
            return ToolResult.Error("query is required.");

        var query = parsed.Query.Trim();

        if (IsSelectQuery(query))
            return await ExecuteSelectAsync(query, cancellationToken);

        var maxResults = Math.Clamp(parsed.MaxResults ?? DefaultMaxResults, 1, MaxMaxResults);
        return await ExecuteSearchAsync(query, maxResults, cancellationToken);
    }

    private async Task<ToolResult> ExecuteSearchAsync(
        string query,
        int maxResults,
        CancellationToken cancellationToken)
    {
        var candidates = new List<SearchCandidate>();

        foreach (var entry in _registry.GetRegisteredTools())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var tool = _registry.Peek(entry.Name);
            if (tool == null || !tool.IsEnabled())
                continue;

            var score = GetMatchScore(entry, query);
            if (score <= 0)
                continue;

            var definition = await _registry.DescribeAsync(entry.Name, cancellationToken: cancellationToken);
            if (definition == null)
                continue;

            candidates.Add(new SearchCandidate(entry, definition, score));
        }

        var matches = candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Definition.Name, StringComparer.Ordinal)
            .Take(maxResults)
            .Select(candidate => new
            {
                name = candidate.Definition.Name,
                aliases = candidate.Entry.Aliases,
                keywords = candidate.Entry.Keywords,
                state = candidate.Entry.State.ToString().ToLowerInvariant(),
                definition = candidate.Definition,
            })
            .ToArray();

        var payload = new
        {
            mode = "search",
            query,
            match_count = matches.Length,
            matches,
        };

        return ToolResult.Success(JsonSerializer.Serialize(payload, JsonOptions));
    }

    private async Task<ToolResult> ExecuteSelectAsync(
        string query,
        CancellationToken cancellationToken)
    {
        var requested = ParseSelectedToolNames(query);
        if (requested.Count == 0)
            return ToolResult.Error("select: requires at least one tool name.");

        var loadedDefinitions = new List<ToolSchemaDefinition>();
        var unavailable = new List<string>();

        foreach (var name in requested)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var tool = _registry.Load(name);
            if (tool == null || !tool.IsEnabled())
            {
                unavailable.Add(name);
                continue;
            }

            var definition = await _registry.DescribeAsync(tool.Name, load: true, cancellationToken: cancellationToken);
            if (definition != null)
                loadedDefinitions.Add(definition);
        }

        if (loadedDefinitions.Count == 0)
        {
            return ToolResult.Error(
                unavailable.Count == 0
                    ? "No tools were loaded."
                    : $"No matching tools were loaded. Unknown or unavailable: {string.Join(", ", unavailable)}");
        }

        var payload = new
        {
            mode = "select",
            query,
            loaded = loadedDefinitions.Select(definition => definition.Name).ToArray(),
            unavailable,
            definitions = loadedDefinitions,
        };

        return ToolResult.Success(JsonSerializer.Serialize(payload, JsonOptions));
    }

    private static bool TryGetQuery(JsonElement? input, out string query)
    {
        query = string.Empty;

        if (input?.TryGetProperty("query", out var property) != true)
            return false;

        query = property.GetString() ?? string.Empty;
        return true;
    }

    private static bool IsSelectQuery(string query) =>
        query.TrimStart().StartsWith("select:", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> ParseSelectedToolNames(string query)
    {
        var list = query.Trim();
        var separatorIndex = list.IndexOf(':');
        if (separatorIndex < 0 || separatorIndex == list.Length - 1)
            return [];

        return list[(separatorIndex + 1)..]
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static int GetMatchScore(ToolRegistryEntryInfo entry, string query)
    {
        var normalizedQuery = Normalize(query);
        if (string.IsNullOrEmpty(normalizedQuery))
            return 0;

        var primaryName = Normalize(entry.Name);
        var aliases = entry.Aliases.Select(Normalize).Where(value => value.Length > 0).ToArray();
        var searchTerms = BuildSearchTerms(entry);
        var combined = string.Join(' ', searchTerms);

        var score = 0;

        if (string.Equals(primaryName, normalizedQuery, StringComparison.Ordinal))
            score = 1_000;
        else if (aliases.Contains(normalizedQuery, StringComparer.Ordinal))
            score = 950;
        else if (primaryName.StartsWith(normalizedQuery, StringComparison.Ordinal))
            score = 880;
        else if (aliases.Any(alias => alias.StartsWith(normalizedQuery, StringComparison.Ordinal)))
            score = 840;
        else if (primaryName.Contains(normalizedQuery, StringComparison.Ordinal))
            score = 760;
        else if (aliases.Any(alias => alias.Contains(normalizedQuery, StringComparison.Ordinal)))
            score = 720;
        else if (combined.Contains(normalizedQuery, StringComparison.Ordinal))
            score = 680;

        var tokens = Tokenize(normalizedQuery);
        if (tokens.Length == 0)
            return score;

        var matchedTokenCount = tokens.Count(token =>
            searchTerms.Any(term => term.Contains(token, StringComparison.Ordinal)));

        if (matchedTokenCount == tokens.Length)
            score = Math.Max(score, 520 + (matchedTokenCount * 30));
        else if (matchedTokenCount > 0)
            score = Math.Max(score, 320 + (matchedTokenCount * 25));

        return score;
    }

    private static string[] BuildSearchTerms(ToolRegistryEntryInfo entry)
    {
        return entry.Aliases
            .Prepend(entry.Name)
            .Concat(entry.Keywords)
            .Concat(SplitIdentifier(entry.Name))
            .Concat(entry.Aliases.SelectMany(SplitIdentifier))
            .Select(Normalize)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<string> SplitIdentifier(string value)
    {
        foreach (Match match in IdentifierTokenRegex().Matches(value))
            yield return match.Value;
    }

    private static string[] Tokenize(string value)
    {
        return SearchTokenRegex()
            .Matches(value)
            .Select(match => Normalize(match.Value))
            .Where(token => token.Length >= 2)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static string Normalize(string value) =>
        value.Trim().ToLowerInvariant();

    [GeneratedRegex(@"[A-Z]+(?![a-z])|[A-Z]?[a-z]+|\d+")]
    private static partial Regex IdentifierTokenRegex();

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex SearchTokenRegex();

    private sealed record SearchCandidate(
        ToolRegistryEntryInfo Entry,
        ToolSchemaDefinition Definition,
        int Score);
}

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClaudeSharp.Core.Providers;
using ClaudeSharp.Core.Permissions;
using ClaudeSharp.Core.Query;
using ClaudeSharp.Core.Tools;

namespace ClaudeSharp.Tools;

/// <summary>
/// Represents web search hit.
/// </summary>
public sealed record WebSearchHit(
    string Title,
    string Url,
    string? Snippet = null);

/// <summary>
/// Defines the contract for web search backend.
/// </summary>
public interface IWebSearchBackend
{
    Task<IReadOnlyList<WebSearchHit>> SearchAsync(
        string query,
        int maxResults,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Provides duck duck go web search backend.
/// </summary>
public sealed class DuckDuckGoWebSearchBackend : IWebSearchBackend
{
    private static readonly HttpClient SharedHttpClient = new();

    private readonly HttpClient _httpClient;

    public DuckDuckGoWebSearchBackend(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? SharedHttpClient;
    }

    public async Task<IReadOnlyList<WebSearchHit>> SearchAsync(
        string query,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var requestUri = $"https://html.duckduckgo.com/html/?q={Uri.EscapeDataString(query)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.UserAgent.ParseAdd("ClaudeSharp/1.0");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return [];

        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseResults(html, maxResults);
    }

    private static IReadOnlyList<WebSearchHit> ParseResults(string html, int maxResults)
    {
        var matches = Regex.Matches(
            html,
            @"<a[^>]*class=""result__a""[^>]*href=""(?<url>[^""]+)""[^>]*>(?<title>.*?)</a>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        var results = new List<WebSearchHit>();
        foreach (Match match in matches)
        {
            if (results.Count >= maxResults)
                break;

            var title = WebUtility.HtmlDecode(match.Groups["title"].Value)
                .Trim();
            var url = NormalizeUrl(WebUtility.HtmlDecode(match.Groups["url"].Value).Trim());

            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(url))
                continue;

            results.Add(new WebSearchHit(title, url));
        }

        return results;
    }

    private static string NormalizeUrl(string url)
    {
        if (!url.Contains("uddg=", StringComparison.OrdinalIgnoreCase))
            return url;

        var match = Regex.Match(url, @"uddg=([^&]+)", RegexOptions.IgnoreCase);
        if (!match.Success)
            return url;

        return WebUtility.UrlDecode(match.Groups[1].Value) ?? url;
    }
}

/// <summary>
/// Provides web search tool.
/// </summary>
public sealed class WebSearchTool : ITool
{
    private readonly IProviderCapabilityRouter _capabilityRouter;
    private readonly Func<string?> _currentModelAccessor;
    private readonly IWebSearchBackend _backend;

    public WebSearchTool(
        IProviderCapabilityRouter? capabilityRouter = null,
        Func<string?>? currentModelAccessor = null,
        IWebSearchBackend? backend = null)
    {
        _capabilityRouter = capabilityRouter ?? new DefaultProviderCapabilityRouter();
        _currentModelAccessor = currentModelAccessor ?? (() => ClaudeModels.DefaultMainModel);
        _backend = backend ?? new DuckDuckGoWebSearchBackend();
    }

    public string Name => "WebSearch";

    public Task<string> GetDescriptionAsync() =>
        Task.FromResult("Search the web for relevant sources and return a source list.");

    public JsonElement GetInputSchema()
    {
        return JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "query": {
              "type": "string",
              "description": "Search query"
            },
            "max_results": {
              "type": "integer",
              "description": "Maximum number of results to return"
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
            Search the web for a query and return trustworthy sources.

            Rules:
            - Use this tool for discovery, not for fetching a known URL
            - Prefer authoritative sources when possible
            - Include sources as markdown links in your final answer
            - Keep the result concise and source-oriented
            """);
    }

    public Task<PermissionResult> CheckPermissionsAsync(
        JsonElement input,
        ToolExecutionContext context)
    {
        return Task.FromResult(PermissionResult.Allow());
    }

    public bool IsEnabled() =>
        _capabilityRouter.Supports(_currentModelAccessor(), ModelCapability.WebSearch);

    public bool IsReadOnly(JsonElement input) => true;

    public bool IsConcurrencySafe(JsonElement input) => true;

    public string? GetActivityDescription(JsonElement? input)
    {
        if (input?.TryGetProperty("query", out var queryProperty) == true)
            return $"Searching the web for \"{queryProperty.GetString()}\"";

        return "Searching the web";
    }

    public async Task<ToolResult> ExecuteAsync(
        JsonElement input,
        ToolExecutionContext context,
        IProgress<ToolProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled())
            return ToolResult.Error("WebSearch is not supported by the current model/provider.");

        if (!input.TryGetProperty("query", out var queryProperty))
            return ToolResult.Error("query is required.");

        var query = queryProperty.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(query))
            return ToolResult.Error("query is required.");

        var maxResults = 5;
        if (input.TryGetProperty("max_results", out var maxResultsProperty) &&
            maxResultsProperty.ValueKind == JsonValueKind.Number &&
            maxResultsProperty.TryGetInt32(out var parsedMaxResults) &&
            parsedMaxResults > 0)
        {
            maxResults = Math.Min(parsedMaxResults, 10);
        }

        var results = await _backend.SearchAsync(query, maxResults, cancellationToken);
        var text = FormatResults(query, results);
        return ToolResult.Success(text);
    }

    private static string FormatResults(string query, IReadOnlyList<WebSearchHit> hits)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Web search results for: {query}");

        if (hits.Count == 0)
        {
            builder.AppendLine("No search results found.");
            return builder.ToString().TrimEnd();
        }

        for (var index = 0; index < hits.Count; index++)
        {
            var hit = hits[index];
            builder.AppendLine($"{index + 1}. [{hit.Title}]({hit.Url})");
            if (!string.IsNullOrWhiteSpace(hit.Snippet))
                builder.AppendLine($"   {hit.Snippet.Trim()}");
        }

        builder.AppendLine();
        builder.AppendLine("Sources:");
        foreach (var hit in hits)
            builder.AppendLine($"- [{hit.Title}]({hit.Url})");

        return builder.ToString().TrimEnd();
    }
}

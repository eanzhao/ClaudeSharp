using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClaudeSharp.Core.Permissions;
using ClaudeSharp.Core.Tools;

namespace ClaudeSharp.Tools;

/// <summary>
/// Defines web fetch policy decision values.
/// </summary>
public enum WebFetchPolicyDecision
{
    Allow,
    Ask,
    Deny,
}

/// <summary>
/// Represents web fetch policy result.
/// </summary>
public sealed record WebFetchPolicyResult(
    WebFetchPolicyDecision Decision,
    string Host,
    string Message);

/// <summary>
/// Defines the contract for web fetch policy.
/// </summary>
public interface IWebFetchPolicy
{
    WebFetchPolicyResult Evaluate(Uri uri);
}

/// <summary>
/// Provides default web fetch policy.
/// </summary>
public sealed class DefaultWebFetchPolicy : IWebFetchPolicy
{
    private static readonly HashSet<string> PreapprovedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "anthropic.com",
        "docs.anthropic.com",
        "www.anthropic.com",
        "github.com",
        "raw.githubusercontent.com",
        "developer.mozilla.org",
        "learn.microsoft.com",
        "docs.microsoft.com",
        "cloud.google.com",
        "docs.aws.amazon.com",
        "nodejs.org",
        "react.dev",
        "pkg.go.dev",
        "kubernetes.io",
    };

    private static readonly HashSet<string> BlockedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "localhost",
        "127.0.0.1",
        "::1",
    };

    public WebFetchPolicyResult Evaluate(Uri uri)
    {
        var host = NormalizeHost(uri.Host);

        if (string.IsNullOrWhiteSpace(host))
        {
            return new WebFetchPolicyResult(
                WebFetchPolicyDecision.Deny,
                host,
                "WebFetch requires a valid host.");
        }

        if (BlockedHosts.Contains(host))
        {
            return new WebFetchPolicyResult(
                WebFetchPolicyDecision.Deny,
                host,
                $"WebFetch is blocked for host {host}.");
        }

        if (PreapprovedHosts.Contains(host))
        {
            return new WebFetchPolicyResult(
                WebFetchPolicyDecision.Allow,
                host,
                $"Host {host} is preapproved for web fetch.");
        }

        return new WebFetchPolicyResult(
            WebFetchPolicyDecision.Ask,
            host,
            $"Allow web fetch from domain {host}?");
    }

    private static string NormalizeHost(string host) =>
        host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
            ? host[4..].ToLowerInvariant()
            : host.ToLowerInvariant();
}

/// <summary>
/// Provides web fetch tool.
/// </summary>
public sealed class WebFetchTool : ITool
{
    private static readonly HttpClient SharedHttpClient = new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
    });

    private const int MaxRedirects = 5;

    private readonly HttpClient _httpClient;
    private readonly IWebFetchPolicy _policy;

    public WebFetchTool(
        HttpClient? httpClient = null,
        IWebFetchPolicy? policy = null)
    {
        _httpClient = httpClient ?? SharedHttpClient;
        _policy = policy ?? new DefaultWebFetchPolicy();
    }

    public string Name => "WebFetch";

    public Task<string> GetDescriptionAsync() =>
        Task.FromResult("Fetch a web page by URL and return a concise text extraction.");

    public JsonElement GetInputSchema()
    {
        return JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "url": {
              "type": "string",
              "description": "The URL to fetch"
            },
            "prompt": {
              "type": "string",
              "description": "What to extract from the page"
            }
          },
          "required": ["url"],
          "additionalProperties": false
        }
        """).RootElement.Clone();
    }

    public Task<string> GetPromptAsync(ToolPromptContext context)
    {
        return Task.FromResult("""
            Fetch a specific URL and return a concise extraction.

            Rules:
            - Only use this when you already know the URL
            - Prefer a dedicated source-specific tool when one exists
            - The tool may upgrade http to https automatically
            - Same-host redirects may be followed; cross-host redirects are blocked
            - External content may be truncated for safety and size limits
            - When the domain is unfamiliar, approval may be required
            """);
    }

    public Task<PermissionResult> CheckPermissionsAsync(
        JsonElement input,
        ToolExecutionContext context)
    {
        var uri = TryParseUrl(input);
        if (uri == null)
            return Task.FromResult(PermissionResult.Deny("A valid url is required."));

        var decision = _policy.Evaluate(uri);
        return Task.FromResult(decision.Decision switch
        {
            WebFetchPolicyDecision.Allow => PermissionResult.Allow(),
            WebFetchPolicyDecision.Deny => PermissionResult.Deny(decision.Message),
            _ => PermissionResult.Ask(decision.Message),
        });
    }

    public bool IsReadOnly(JsonElement input) => true;

    public bool IsConcurrencySafe(JsonElement input) => true;

    public int MaxResultSizeChars => 100_000;

    public string GetUserFacingName(JsonElement? input = null)
    {
        var uri = input is not null ? TryParseUrl(input.Value) : null;
        return uri == null ? "WebFetch" : $"WebFetch: {uri.Host}";
    }

    public string? GetActivityDescription(JsonElement? input)
    {
        var uri = input is not null ? TryParseUrl(input.Value) : null;
        return uri == null ? "Fetching URL" : $"Fetching {uri.Host}";
    }

    public async Task<ToolResult> ExecuteAsync(
        JsonElement input,
        ToolExecutionContext context,
        IProgress<ToolProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var uri = TryParseUrl(input);
        if (uri == null)
            return ToolResult.Error("url is required.");

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return ToolResult.Error("WebFetch only supports http and https URLs.");
        }

        uri = UpgradeToHttps(uri);

        var prompt = input.TryGetProperty("prompt", out var promptProperty)
            ? promptProperty.GetString()?.Trim()
            : null;

        var fetch = await FetchAsync(uri, cancellationToken);
        if (fetch.Error != null)
            return ToolResult.Error(fetch.Error);

        var extracted = ExtractContent(fetch);
        var result = BuildResultText(
            fetch.FinalUri,
            fetch.ContentType,
            fetch.Body,
            extracted,
            prompt);
        return ToolResult.Success(result);
    }

    private async Task<WebFetchResponse> FetchAsync(Uri uri, CancellationToken cancellationToken)
    {
        var current = uri;

        for (var redirectCount = 0; redirectCount <= MaxRedirects; redirectCount++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.UserAgent.ParseAdd("ClaudeSharp/1.0");

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (IsRedirect(response.StatusCode) &&
                response.Headers.Location != null)
            {
                var next = ResolveRedirect(current, response.Headers.Location);
                if (next == null)
                {
                    return new WebFetchResponse(
                        current,
                        response.StatusCode,
                        null,
                        string.Empty,
                        "Cross-host redirects are not allowed.");
                }

                current = next;
                continue;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return new WebFetchResponse(
                current,
                response.StatusCode,
                response.Content.Headers.ContentType?.MediaType,
                body,
                null);
        }

        return new WebFetchResponse(
            current,
            HttpStatusCode.TooManyRequests,
            null,
            string.Empty,
            "Too many redirects.");
    }

    private static bool IsRedirect(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.Moved
            or HttpStatusCode.Redirect
            or HttpStatusCode.RedirectMethod
            or HttpStatusCode.RedirectKeepVerb
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;

    private static Uri? ResolveRedirect(Uri current, Uri location)
    {
        var next = location.IsAbsoluteUri ? location : new Uri(current, location);
        if (IsSafeRedirect(current, next))
            return next;

        return null;
    }

    private static bool IsSafeRedirect(Uri current, Uri next)
    {
        if (!string.Equals(current.Scheme, next.Scheme, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(next.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return NormalizeHost(current.Host) == NormalizeHost(next.Host);
    }

    private static Uri UpgradeToHttps(Uri uri)
    {
        if (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return uri;

        var builder = new UriBuilder(uri)
        {
            Scheme = Uri.UriSchemeHttps,
            Port = 443,
        };

        return builder.Uri;
    }

    private static string ExtractContent(WebFetchResponse response)
    {
        if (string.IsNullOrWhiteSpace(response.Body))
            return string.Empty;

        var contentType = response.ContentType ?? string.Empty;
        if (contentType.Contains("html", StringComparison.OrdinalIgnoreCase))
            return StripHtml(response.Body);

        return response.Body.Trim();
    }

    private static string BuildResultText(
        Uri uri,
        string? contentType,
        string rawBody,
        string content,
        string? prompt)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"URL: {uri}");
        if (!string.IsNullOrWhiteSpace(contentType))
            builder.AppendLine($"Content-Type: {contentType}");

        if (!string.IsNullOrWhiteSpace(prompt))
            builder.AppendLine($"Requested focus: {prompt}");

        var title = TryExtractTitle(rawBody);
        if (!string.IsNullOrWhiteSpace(title))
            builder.AppendLine($"Title: {title}");

        if (!string.IsNullOrWhiteSpace(content))
        {
            builder.AppendLine("Content:");
            builder.AppendLine(TrimToLimit(content, 20_000));
        }
        else
        {
            builder.AppendLine("Content: (empty)");
        }

        return builder.ToString().TrimEnd();
    }

    private static string StripHtml(string html)
    {
        var withoutScripts = Regex.Replace(
            html,
            "<script\\b.*?</script>",
            " ",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        var withoutStyles = Regex.Replace(
            withoutScripts,
            "<style\\b.*?</style>",
            " ",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        var text = Regex.Replace(withoutStyles, "<[^>]+>", " ");
        text = WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, "\\s+", " ");
        return text.Trim();
    }

    private static string? TryExtractTitle(string content)
    {
        var match = Regex.Match(
            content,
            @"(?is)<title[^>]*>(?<title>.*?)</title>");

        return match.Success
            ? WebUtility.HtmlDecode(match.Groups["title"].Value).Trim()
            : null;
    }

    private static string TrimToLimit(string value, int maxCharacters)
    {
        if (value.Length <= maxCharacters)
            return value;

        return $"{value[..(maxCharacters - 3)]}...";
    }

    private static Uri? TryParseUrl(JsonElement input)
    {
        if (!input.TryGetProperty("url", out var urlProperty))
            return null;

        var raw = urlProperty.GetString();
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        return Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var uri)
            ? uri
            : null;
    }

    private static string NormalizeHost(string host) =>
        host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
            ? host[4..].ToLowerInvariant()
            : host.ToLowerInvariant();

    private sealed record WebFetchResponse(
        Uri FinalUri,
        HttpStatusCode StatusCode,
        string? ContentType,
        string Body,
        string? Error);
}

using System.Net;
using System.Text;
using System.Text.Json;
using ClaudeSharp.Core.Permissions;
using ClaudeSharp.Core.Query;
using ClaudeSharp.Core.Tools;
using ClaudeSharp.Tools;

namespace ClaudeSharp.Core.Tests.Web;

/// <summary>
/// Contains tests for web Fetch Tool.
/// </summary>
public sealed class WebFetchToolTests
{
    [Fact]
    public void DefaultWebFetchPolicy_NormalizesHostsAndAppliesAllowAskDeny()
    {
        var policy = new DefaultWebFetchPolicy();

        Assert.Equal(
            WebFetchPolicyDecision.Allow,
            policy.Evaluate(new Uri("https://www.anthropic.com")).Decision);
        Assert.Equal(
            WebFetchPolicyDecision.Ask,
            policy.Evaluate(new Uri("https://example.com")).Decision);
        Assert.Equal(
            WebFetchPolicyDecision.Deny,
            policy.Evaluate(new Uri("https://localhost")).Decision);
    }

    [Fact]
    public async Task CheckPermissionsAsync_UsesPolicyDecision()
    {
        var tool = new WebFetchTool(
            httpClient: new HttpClient(new ScriptedHandler()),
            policy: new FixedPolicy(WebFetchPolicyDecision.Ask, "example.com", "ask first"));

        var result = await tool.CheckPermissionsAsync(
            JsonSerializer.SerializeToElement(new { url = "https://example.com/page" }),
            BuildContext());

        Assert.Equal(PermissionBehavior.Ask, result.Behavior);
        Assert.Equal("ask first", result.Message);
    }

    [Fact]
    public async Task ExecuteAsync_FollowsSafeRedirectsAndStripsHtml()
    {
        var handler = new ScriptedHandler();
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.Redirect)
        {
            Headers =
            {
                Location = new Uri("/next", UriKind.Relative),
            },
        });
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                <html>
                  <head><title>Hello Page</title></head>
                  <body><h1>World</h1><p>some text</p></body>
                </html>
                """,
                Encoding.UTF8,
                "text/html"),
        });

        var tool = new WebFetchTool(
            httpClient: new HttpClient(handler),
            policy: new FixedPolicy(WebFetchPolicyDecision.Allow, "example.com", "allow"));

        var result = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new { url = "http://example.com/start", prompt = "focus" }),
            BuildContext());

        Assert.False(result.IsError);
        Assert.Contains("URL: https://example.com/next", result.Data);
        Assert.Contains("Title: Hello Page", result.Data);
        Assert.Contains("World some text", result.Data);
        Assert.Contains("Requested focus: focus", result.Data);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("https", handler.Requests[0].RequestUri?.Scheme);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsCrossHostRedirects()
    {
        var handler = new ScriptedHandler();
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.Redirect)
        {
            Headers =
            {
                Location = new Uri("https://evil.example/next"),
            },
        });

        var tool = new WebFetchTool(
            httpClient: new HttpClient(handler),
            policy: new FixedPolicy(WebFetchPolicyDecision.Allow, "example.com", "allow"));

        var result = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new { url = "https://example.com/start" }),
            BuildContext());

        Assert.True(result.IsError);
        Assert.Contains("Cross-host redirects are not allowed", result.Data);
    }

    private static ToolExecutionContext BuildContext() =>
        new()
        {
            WorkingDirectory = "/tmp",
            PermissionContext = new PermissionContext(),
            Tools = [],
            Messages = [],
            CancellationToken = CancellationToken.None,
            MainLoopModel = ClaudeModels.DefaultMainModel,
        };

    private sealed class FixedPolicy : IWebFetchPolicy
    {
        private readonly WebFetchPolicyDecision _decision;
        private readonly string _host;
        private readonly string _message;

        public FixedPolicy(WebFetchPolicyDecision decision, string host, string message)
        {
            _decision = decision;
            _host = host;
            _message = message;
        }

        public WebFetchPolicyResult Evaluate(Uri uri) =>
            new(_decision, _host, _message);
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new();

        public List<HttpRequestMessage> Requests { get; } = [];

        public void Enqueue(HttpResponseMessage response) => _responses.Enqueue(response);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_responses.Count > 0
                ? _responses.Dequeue()
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("", Encoding.UTF8, "text/plain"),
                });
        }
    }
}

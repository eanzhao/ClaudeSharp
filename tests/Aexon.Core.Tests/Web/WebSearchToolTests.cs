using System.Text.Json;
using Aexon.Core.Permissions;
using Aexon.Core.Providers;
using Aexon.Core.Query;
using Aexon.Core.Tools;
using Aexon.Tools;

namespace Aexon.Core.Tests.Web;

/// <summary>
/// Contains tests for web Search Tool.
/// </summary>
public sealed class WebSearchToolTests
{
    [Fact]
    public void IsEnabled_UsesProviderCapabilityRouting()
    {
        var router = new DefaultProviderCapabilityRouter();
        var supportedModel = ClaudeModelCatalog.All.First(model => model.StableId == "claude-opus-4-6").ProviderIds.FirstParty;
        var unsupportedModel = ClaudeModelCatalog.All.First(model => model.StableId == "claude-sonnet-4-6").ProviderIds.Bedrock;

        var supportedTool = new WebSearchTool(router, () => supportedModel, new FakeSearchBackend());
        var unsupportedTool = new WebSearchTool(router, () => unsupportedModel, new FakeSearchBackend());

        Assert.True(supportedTool.IsEnabled());
        Assert.False(unsupportedTool.IsEnabled());
    }

    [Fact]
    public async Task ExecuteAsync_FormatsSearchResultsAndSources()
    {
        var router = new DefaultProviderCapabilityRouter();
        var backend = new FakeSearchBackend(
            new WebSearchHit("Aexon", "https://example.com/claudesharp", "first hit"),
            new WebSearchHit("Claude Code", "https://example.com/claude-code"));

        var tool = new WebSearchTool(router, () => ClaudeModels.DefaultMainModel, backend);

        var result = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new { query = "claude sharp", max_results = 2 }),
            BuildContext());

        Assert.False(result.IsError);
        Assert.Contains("Web search results for: claude sharp", result.Data);
        Assert.Contains("1. [Aexon](https://example.com/claudesharp)", result.Data);
        Assert.Contains("first hit", result.Data);
        Assert.Contains("Sources:", result.Data);
        Assert.Contains("- [Claude Code](https://example.com/claude-code)", result.Data);
        Assert.Equal("claude sharp", backend.LastQuery);
        Assert.Equal(2, backend.LastMaxResults);
    }

    [Fact]
    public async Task CheckPermissionsAsync_AlwaysAllowsPassthrough()
    {
        var tool = new WebSearchTool(new DefaultProviderCapabilityRouter(), () => ClaudeModels.DefaultMainModel, new FakeSearchBackend());

        var result = await tool.CheckPermissionsAsync(
            JsonSerializer.SerializeToElement(new { query = "test" }),
            BuildContext());

        Assert.Equal(PermissionBehavior.Allow, result.Behavior);
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

    private sealed class FakeSearchBackend : IWebSearchBackend
    {
        private readonly IReadOnlyList<WebSearchHit> _hits;

        public FakeSearchBackend(params WebSearchHit[] hits)
        {
            _hits = hits;
        }

        public string? LastQuery { get; private set; }
        public int LastMaxResults { get; private set; }

        public Task<IReadOnlyList<WebSearchHit>> SearchAsync(
            string query,
            int maxResults,
            CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            LastMaxResults = maxResults;
            return Task.FromResult(_hits.Take(maxResults).ToArray() as IReadOnlyList<WebSearchHit>);
        }
    }
}

using System.Text.Json;
using Aexon.Core.Permissions;
using Aexon.Core.Query;
using Aexon.Core.Tools;
using Aexon.Tools;

namespace Aexon.Core.Tests.Runtime;

public sealed class ToolSearchToolTests
{
    [Fact]
    public async Task ExecuteAsync_SearchesByExactNameAndKeyword()
    {
        var registry = new ToolRegistry();
        var toolSearch = new ToolSearchTool(registry);
        registry.Register(toolSearch);
        registry.RegisterDeferred(new DeferredToolRegistration(
            "MailboxStatus",
            () => new FakeTool
            {
                Name = "MailboxStatus",
                Description = "Inspect mailbox status.",
                InputSchema = TestSupport.Json(new
                {
                    type = "object",
                    properties = new
                    {
                        request = new { type = "string" },
                    },
                }),
            },
            Keywords: ["mailbox", "inbox", "messages"]));

        var exact = await toolSearch.ExecuteAsync(
            TestSupport.Json(new { query = "MailboxStatus" }),
            CreateContext(registry));
        var exactJson = JsonDocument.Parse(exact.Data).RootElement;
        var exactMatch = Assert.Single(exactJson.GetProperty("matches").EnumerateArray());

        Assert.False(exact.IsError);
        Assert.Equal("MailboxStatus", exactMatch.GetProperty("name").GetString());
        Assert.Equal("deferred", exactMatch.GetProperty("state").GetString());
        Assert.True(exactMatch.GetProperty("definition").GetProperty("input_schema")
            .TryGetProperty("properties", out _));

        var keyword = await toolSearch.ExecuteAsync(
            TestSupport.Json(new { query = "inbox" }),
            CreateContext(registry));
        var keywordJson = JsonDocument.Parse(keyword.Data).RootElement;
        var keywordMatch = Assert.Single(keywordJson.GetProperty("matches").EnumerateArray());

        Assert.False(keyword.IsError);
        Assert.Equal("MailboxStatus", keywordMatch.GetProperty("name").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_SelectLoadsMultipleDeferredTools()
    {
        var registry = new ToolRegistry();
        var toolSearch = new ToolSearchTool(registry);
        registry.Register(toolSearch);
        registry.RegisterDeferred(new DeferredToolRegistration(
            "AlphaTool",
            () => new FakeTool { Name = "AlphaTool", Description = "alpha" },
            Keywords: ["alpha"]));
        registry.RegisterDeferred(new DeferredToolRegistration(
            "BetaTool",
            () => new FakeTool { Name = "BetaTool", Description = "beta" },
            Keywords: ["beta"]));

        var result = await toolSearch.ExecuteAsync(
            TestSupport.Json(new { query = "select:AlphaTool,BetaTool" }),
            CreateContext(registry));
        var json = JsonDocument.Parse(result.Data).RootElement;

        Assert.False(result.IsError);
        Assert.True(registry.IsLoaded("AlphaTool"));
        Assert.True(registry.IsLoaded("BetaTool"));
        Assert.Equal(
            ["AlphaTool", "BetaTool"],
            json.GetProperty("loaded")
                .EnumerateArray()
                .Select(item => item.GetString()!)
                .ToArray());
        Assert.Equal(2, json.GetProperty("definitions").GetArrayLength());
    }

    private static ToolExecutionContext CreateContext(ToolRegistry registry) => new()
    {
        WorkingDirectory = Environment.CurrentDirectory,
        PermissionContext = new PermissionContext(),
        Tools = registry.GetAllTools(),
        Messages = [],
        CancellationToken = CancellationToken.None,
        MainLoopModel = ClaudeModels.DefaultMainModel,
        MainLoopProvider = AiProvider.Anthropic,
    };
}

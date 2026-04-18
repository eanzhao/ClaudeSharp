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

    [Fact]
    public async Task ExecuteAsync_SelectDeduplicatesAndReportsUnavailable()
    {
        var registry = new ToolRegistry();
        var toolSearch = new ToolSearchTool(registry);
        registry.Register(toolSearch);
        registry.RegisterDeferred(new DeferredToolRegistration(
            "AlphaTool",
            () => new FakeTool { Name = "AlphaTool", Description = "alpha" }));

        var result = await toolSearch.ExecuteAsync(
            TestSupport.Json(new { query = "select:AlphaTool, AlphaTool , DoesNotExist" }),
            CreateContext(registry));
        var json = JsonDocument.Parse(result.Data).RootElement;

        Assert.False(result.IsError);
        Assert.Equal(1, json.GetProperty("definitions").GetArrayLength());
        Assert.Equal(
            ["AlphaTool"],
            json.GetProperty("loaded")
                .EnumerateArray()
                .Select(item => item.GetString()!)
                .ToArray());
        Assert.Equal(
            ["DoesNotExist"],
            json.GetProperty("unavailable")
                .EnumerateArray()
                .Select(item => item.GetString()!)
                .ToArray());
    }

    [Fact]
    public async Task ExecuteAsync_SelectReturnsErrorWhenNoToolsResolve()
    {
        var registry = new ToolRegistry();
        var toolSearch = new ToolSearchTool(registry);
        registry.Register(toolSearch);

        var missing = await toolSearch.ExecuteAsync(
            TestSupport.Json(new { query = "select:DoesNotExist,AlsoMissing" }),
            CreateContext(registry));
        var empty = await toolSearch.ExecuteAsync(
            TestSupport.Json(new { query = "select:" }),
            CreateContext(registry));

        Assert.True(missing.IsError);
        Assert.Contains("DoesNotExist", missing.Data, StringComparison.Ordinal);
        Assert.Contains("AlsoMissing", missing.Data, StringComparison.Ordinal);
        Assert.True(empty.IsError);
        Assert.Contains("select: requires at least one tool name.", empty.Data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_SearchRespectsMaxResultsAndOrdersByScore()
    {
        var registry = new ToolRegistry();
        var toolSearch = new ToolSearchTool(registry);
        registry.Register(toolSearch);

        registry.RegisterDeferred(new DeferredToolRegistration(
            "MailboxStatus",
            () => new FakeTool { Name = "MailboxStatus", Description = "mailbox status" },
            Keywords: ["mailbox"]));
        registry.RegisterDeferred(new DeferredToolRegistration(
            "MailboxSend",
            () => new FakeTool { Name = "MailboxSend", Description = "send mail" },
            Keywords: ["mail"]));
        registry.RegisterDeferred(new DeferredToolRegistration(
            "InboxPeek",
            () => new FakeTool { Name = "InboxPeek", Description = "check inbox" },
            Keywords: ["mailbox", "inbox"]));

        var limited = await toolSearch.ExecuteAsync(
            TestSupport.Json(new { query = "mailbox", max_results = 2 }),
            CreateContext(registry));
        var json = JsonDocument.Parse(limited.Data).RootElement;
        var matches = json.GetProperty("matches").EnumerateArray().ToArray();

        Assert.False(limited.IsError);
        Assert.Equal("search", json.GetProperty("mode").GetString());
        Assert.Equal(2, matches.Length);
        // Exact/alias-ish starts-with beats keyword-only hits.
        Assert.Equal("MailboxSend", matches[0].GetProperty("name").GetString());
        Assert.Equal("MailboxStatus", matches[1].GetProperty("name").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_SearchReturnsEmptyMatchesWhenNothingMatches()
    {
        var registry = new ToolRegistry();
        var toolSearch = new ToolSearchTool(registry);
        registry.Register(toolSearch);
        registry.RegisterDeferred(new DeferredToolRegistration(
            "MailboxStatus",
            () => new FakeTool { Name = "MailboxStatus", Description = "mailbox status" },
            Keywords: ["mailbox"]));

        var result = await toolSearch.ExecuteAsync(
            TestSupport.Json(new { query = "nonexistent-keyword-xyz" }),
            CreateContext(registry));
        var json = JsonDocument.Parse(result.Data).RootElement;

        Assert.False(result.IsError);
        Assert.Equal(0, json.GetProperty("match_count").GetInt32());
        Assert.Empty(json.GetProperty("matches").EnumerateArray());
    }

    [Fact]
    public async Task ExecuteAsync_SearchSkipsDisabledTools()
    {
        var registry = new ToolRegistry();
        var toolSearch = new ToolSearchTool(registry);
        registry.Register(toolSearch);
        registry.RegisterDeferred(new DeferredToolRegistration(
            "MailboxStatus",
            () => new FakeTool
            {
                Name = "MailboxStatus",
                Description = "mailbox status",
                Enabled = false,
            }));

        var result = await toolSearch.ExecuteAsync(
            TestSupport.Json(new { query = "MailboxStatus" }),
            CreateContext(registry));
        var json = JsonDocument.Parse(result.Data).RootElement;

        Assert.False(result.IsError);
        Assert.Empty(json.GetProperty("matches").EnumerateArray());
    }

    [Fact]
    public async Task ValidateInputAsync_RejectsBlankQueryAndNonPositiveMaxResults()
    {
        var registry = new ToolRegistry();
        var toolSearch = new ToolSearchTool(registry);
        var context = CreateContext(registry);

        var blank = await toolSearch.ValidateInputAsync(
            TestSupport.Json(new { query = "   " }),
            context);
        var zero = await toolSearch.ValidateInputAsync(
            TestSupport.Json(new { query = "anything", max_results = 0 }),
            context);
        var ok = await toolSearch.ValidateInputAsync(
            TestSupport.Json(new { query = "anything", max_results = 3 }),
            context);

        Assert.False(blank.IsValid);
        Assert.Contains("query is required", blank.Message, StringComparison.Ordinal);
        Assert.False(zero.IsValid);
        Assert.Contains("max_results must be greater than 0", zero.Message, StringComparison.Ordinal);
        Assert.True(ok.IsValid);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyStringQueryReturnsError()
    {
        var registry = new ToolRegistry();
        var toolSearch = new ToolSearchTool(registry);

        var result = await toolSearch.ExecuteAsync(
            TestSupport.Json(new { query = "" }),
            CreateContext(registry));

        Assert.True(result.IsError);
        Assert.Contains("query is required", result.Data, StringComparison.Ordinal);
    }

    [Fact]
    public void IsReadOnly_IsTrueForSearchButFalseForSelect()
    {
        var registry = new ToolRegistry();
        var toolSearch = new ToolSearchTool(registry);

        var searchInput = TestSupport.Json(new { query = "mailbox" });
        var selectInput = TestSupport.Json(new { query = "select:MailboxStatus" });
        var selectWithPaddingInput = TestSupport.Json(new { query = "  SELECT:Foo " });

        Assert.True(toolSearch.IsReadOnly(searchInput));
        Assert.True(toolSearch.IsConcurrencySafe(searchInput));
        Assert.False(toolSearch.IsReadOnly(selectInput));
        Assert.False(toolSearch.IsConcurrencySafe(selectInput));
        Assert.False(toolSearch.IsReadOnly(selectWithPaddingInput));
    }

    [Fact]
    public void GetActivityDescription_ReflectsQueryShape()
    {
        var registry = new ToolRegistry();
        var toolSearch = new ToolSearchTool(registry);

        var searching = toolSearch.GetActivityDescription(TestSupport.Json(new { query = "mailbox" }));
        var loading = toolSearch.GetActivityDescription(TestSupport.Json(new { query = "select:Foo,Bar" }));
        var fallback = toolSearch.GetActivityDescription(null);

        Assert.Equal("Searching tools for \"mailbox\"", searching);
        Assert.Equal("Loading tools", loading);
        Assert.Equal("Searching tools", fallback);
    }

    [Fact]
    public async Task ExecuteAsync_SelectReturnsLoadedToolDefinition()
    {
        var registry = new ToolRegistry();
        var toolSearch = new ToolSearchTool(registry);
        registry.Register(toolSearch);
        registry.RegisterDeferred(new DeferredToolRegistration(
            "GammaTool",
            () => new FakeTool
            {
                Name = "GammaTool",
                Description = "gamma action",
                InputSchema = TestSupport.Json(new { type = "object", properties = new { value = new { type = "string" } } }),
            }));

        Assert.False(registry.IsLoaded("GammaTool"));

        var result = await toolSearch.ExecuteAsync(
            TestSupport.Json(new { query = "select:GammaTool" }),
            CreateContext(registry));
        var json = JsonDocument.Parse(result.Data).RootElement;
        var definition = json.GetProperty("definitions").EnumerateArray().Single();

        Assert.False(result.IsError);
        Assert.True(registry.IsLoaded("GammaTool"));
        Assert.Equal("select", json.GetProperty("mode").GetString());
        Assert.Equal("GammaTool", definition.GetProperty("name").GetString());
        Assert.Equal("gamma action", definition.GetProperty("description").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_MaxResultsIsClampedAndDefaultsToFive()
    {
        var registry = new ToolRegistry();
        var toolSearch = new ToolSearchTool(registry);
        registry.Register(toolSearch);

        for (var i = 0; i < 8; i++)
        {
            var name = $"MailboxTool{i:D2}";
            registry.RegisterDeferred(new DeferredToolRegistration(
                name,
                () => new FakeTool { Name = name, Description = "mailbox related" },
                Keywords: ["mailbox"]));
        }

        var defaultResult = await toolSearch.ExecuteAsync(
            TestSupport.Json(new { query = "mailbox" }),
            CreateContext(registry));
        var defaultJson = JsonDocument.Parse(defaultResult.Data).RootElement;

        var bigResult = await toolSearch.ExecuteAsync(
            TestSupport.Json(new { query = "mailbox", max_results = 999 }),
            CreateContext(registry));
        var bigJson = JsonDocument.Parse(bigResult.Data).RootElement;

        Assert.Equal(5, defaultJson.GetProperty("match_count").GetInt32());
        // MaxMaxResults caps at 20 even when the caller asks for more than that.
        Assert.Equal(8, bigJson.GetProperty("match_count").GetInt32());
    }

    [Fact]
    public async Task GetDescriptionAsync_MentionsSelectSyntax()
    {
        var registry = new ToolRegistry();
        var toolSearch = new ToolSearchTool(registry);

        var description = await toolSearch.GetDescriptionAsync();

        Assert.Contains("select:ToolA,ToolB", description, StringComparison.Ordinal);
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

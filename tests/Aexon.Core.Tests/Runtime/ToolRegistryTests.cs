using System.Text.Json;
using Aexon.Core.Agents;
using Aexon.Core.Tools;
using Aexon.Tools;

namespace Aexon.Core.Tests.Runtime;

/// <summary>
/// Contains tests for tool Registry.
/// </summary>
public class ToolRegistryTests
{
    [Fact]
    public void Get_ReturnsToolByNameOrAlias_AndIsCaseInsensitive()
    {
        var registry = new ToolRegistry();
        var tool = new FakeTool
        {
            Name = "Alpha",
            Aliases = ["a", "first"],
        };

        registry.Register(tool);

        Assert.Same(tool, registry.Get("Alpha"));
        Assert.Same(tool, registry.Get("alpha"));
        Assert.Same(tool, registry.Get("a"));
        Assert.Same(tool, registry.Get("FIRST"));
        Assert.Null(registry.Get("missing"));
    }

    [Fact]
    public void GetEnabledTools_FiltersDisabledTools_AndSortsByName()
    {
        var registry = new ToolRegistry();
        registry.Register(new FakeTool { Name = "zeta", Enabled = true });
        registry.Register(new FakeTool { Name = "alpha", Enabled = true });
        registry.Register(new FakeTool { Name = "beta", Enabled = false });

        var enabled = registry.GetEnabledTools();

        Assert.Equal(["alpha", "zeta"], enabled.Select(tool => tool.Name));
    }

    [Fact]
    public async Task GetToolDefinitionsAsync_UsesSortedEnabledToolsAndToolMetadata()
    {
        var registry = new ToolRegistry();
        registry.Register(new FakeTool
        {
            Name = "zeta",
            Description = "zeta desc",
            PromptText = "zeta prompt",
            InputSchema = TestSupport.Json(new { type = "object", properties = new { zeta = new { type = "string" } } }),
        });
        registry.Register(new FakeTool
        {
            Name = "alpha",
            Description = "alpha desc",
            PromptText = "alpha prompt",
            InputSchema = TestSupport.Json(new { type = "object", properties = new { alpha = new { type = "number" } } }),
        });

        var definitions = await registry.GetToolDefinitionsAsync();

        Assert.Equal(2, definitions.Count);
        Assert.Equal("alpha", definitions[0].GetProperty("name").GetString());
        Assert.Equal("alpha desc", definitions[0].GetProperty("description").GetString());
        Assert.Equal("zeta", definitions[1].GetProperty("name").GetString());
        Assert.Equal("zeta desc", definitions[1].GetProperty("description").GetString());
    }

    [Fact]
    public void GetToolDefinitions_SyncPathMatchesAsyncOrdering()
    {
        var registry = new ToolRegistry();
        registry.Register(new FakeTool { Name = "beta", Description = "beta desc" });
        registry.Register(new FakeTool { Name = "alpha", Description = "alpha desc" });

        var definitions = registry.GetToolDefinitions();

        Assert.Equal(["alpha", "beta"], definitions.Select(def => def.GetProperty("name").GetString()));
    }

    [Fact]
    public void GetEnabledTools_DoesNotExposeAgentManagementToolsToModel()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var registry = new ToolRegistry();
        registry.Register(new AgentStatusTool(runtime));
        registry.Register(new AgentStopTool(runtime));
        registry.Register(new AgentWaitTool(runtime));
        registry.Register(new FakeTool { Name = "alpha", Enabled = true });

        var enabled = registry.GetEnabledTools();

        Assert.Equal(["alpha"], enabled.Select(tool => tool.Name));
    }
}

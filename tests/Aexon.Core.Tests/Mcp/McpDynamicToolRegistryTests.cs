using System.Text.Json;
using Aexon.Core.Mcp;

namespace Aexon.Core.Tests.Mcp;

/// <summary>
/// Contains tests for mcp Dynamic Tool Registry.
/// </summary>
public sealed class McpDynamicToolRegistryTests
{
    [Fact]
    public void ReplaceServerTools_ReplacesOldToolsAndKeepsQualifiedLookupWorking()
    {
        var registry = new McpDynamicToolRegistry();

        registry.ReplaceServerTools("server-a", [BuildTool("alpha"), BuildTool("beta")]);

        Assert.Equal(2, registry.GetAll().Count);
        Assert.True(registry.TryGet("server-a", "alpha", out var alpha));
        Assert.NotNull(alpha);
        Assert.Equal("server-a:alpha", alpha!.QualifiedName);
        Assert.True(registry.TryGetQualified("server-a:beta", out var beta));
        Assert.NotNull(beta);
        Assert.False(registry.TryGetQualified("bad", out _));

        registry.ReplaceServerTools("server-a", [BuildTool("gamma")]);

        Assert.False(registry.TryGet("server-a", "alpha", out _));
        var all = registry.GetAll();
        Assert.Single(all);
        Assert.Equal("server-a:gamma", all[0].QualifiedName);

        Assert.True(registry.RemoveServer("server-a"));
        Assert.Empty(registry.GetAll());
    }

    private static McpToolDescriptor BuildTool(string name) =>
        new()
        {
            Name = name,
            Description = $"{name} tool",
            InputSchema = JsonSerializer.SerializeToElement(new { type = "object" }),
        };
}

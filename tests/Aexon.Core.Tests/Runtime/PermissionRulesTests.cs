using System.Text.Json;
using Aexon.Core.Permissions;
using Aexon.Core.Tools;

namespace Aexon.Core.Tests.Runtime;

/// <summary>
/// Contains tests for permission Rules.
/// </summary>
public sealed class PermissionRulesTests
{
    [Fact]
    public void Parse_TrimsToolNameAndRuleContent()
    {
        var rule = PermissionRule.Parse(PermissionBehavior.Allow, " Shell( git status ) ");

        Assert.Equal("Shell", rule.ToolName);
        Assert.Equal("git status", rule.RuleContent);
        Assert.Equal("Shell(git status)", rule.ToExpression());
    }

    [Fact]
    public void MatchesRuleContent_SupportsExactPrefixWildcardAndEscapedStars()
    {
        Assert.True(PermissionRuleMatcher.MatchesRuleContent("git status", "git status"));
        Assert.True(PermissionRuleMatcher.MatchesRuleContent("git:*", "git status"));
        Assert.True(PermissionRuleMatcher.MatchesRuleContent("src/*.cs", "src/Program.cs"));
        Assert.True(PermissionRuleMatcher.MatchWildcardPattern(@"literal\*star", "literal*star"));
        Assert.False(PermissionRuleMatcher.MatchesRuleContent("git:*", "gits"));
    }

    [Fact]
    public void ExtractRuleTarget_UsesCommandFilePathAndPathFields()
    {
        Assert.Equal("git status", PermissionRuleMatcher.ExtractRuleTarget(Json(new { command = "git status" })));
        Assert.Equal("README.md", PermissionRuleMatcher.ExtractRuleTarget(Json(new { file_path = "README.md" })));
        Assert.Equal("src/Program.cs", PermissionRuleMatcher.ExtractRuleTarget(Json(new { path = "src/Program.cs" })));
    }

    [Fact]
    public void FindFirstMatch_ResolvesToolAliases()
    {
        var tool = new FakeTool
        {
            Name = "Shell",
            Aliases = ["sh"],
        };

        var rule = PermissionRule.Create(PermissionBehavior.Deny, "sh", "README.md");
        var match = PermissionRuleMatcher.FindFirstMatch(
            [rule],
            tool,
            Json(new { file_path = "README.md" }));

        Assert.Same(rule, match);
    }

    private static JsonElement Json(object value) =>
        JsonSerializer.SerializeToElement(value);
}

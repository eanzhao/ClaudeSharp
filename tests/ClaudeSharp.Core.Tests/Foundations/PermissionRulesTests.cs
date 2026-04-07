using System.Text.Json;
using ClaudeSharp.Core.Permissions;

namespace ClaudeSharp.Core.Tests.Foundations;

public sealed class PermissionRulesTests
{
    [Fact]
    public void Create_TrimsInput_AndRejectsBlankToolName()
    {
        var rule = PermissionRule.Create(PermissionBehavior.Allow, " Shell ", " git status ");

        Assert.Equal(PermissionBehavior.Allow, rule.Behavior);
        Assert.Equal("Shell", rule.ToolName);
        Assert.Equal("git status", rule.RuleContent);
        Assert.Equal("Shell(git status)", rule.ToExpression());
        Assert.Throws<ArgumentException>(() => PermissionRule.Create(PermissionBehavior.Allow, "   "));
    }

    [Theory]
    [InlineData(PermissionBehavior.Allow, "Shell", "Shell")]
    [InlineData(PermissionBehavior.Ask, "Shell(git status)", "Shell(git status)")]
    public void Parse_ReturnsRuleForSimpleAndParenExpressions(PermissionBehavior behavior, string expression, string expected)
    {
        var rule = PermissionRule.Parse(behavior, expression);
        Assert.Equal(expected, rule.ToExpression());
    }

    [Fact]
    public void Parse_ReusesToolNameWhenExpressionHasNoClosingParen()
    {
        var rule = PermissionRule.Parse(PermissionBehavior.Deny, "Shell(git status");
        Assert.Equal("Shell(git status", rule.ToolName);
        Assert.Null(rule.RuleContent);
    }

    [Fact]
    public void Parse_RejectsBlankExpressions()
    {
        Assert.Throws<ArgumentException>(() => PermissionRule.Parse(PermissionBehavior.Allow, " "));
    }

    [Theory]
    [InlineData("git:*", "git")]
    [InlineData("git status", null)]
    public void ExtractPrefix_DetectsSuffixStarPatterns(string input, string? expected)
    {
        Assert.Equal(expected, PermissionRuleMatcher.ExtractPrefix(input));
    }

    [Theory]
    [InlineData("git*status", true)]
    [InlineData("git\\*status", false)]
    [InlineData("git:*", false)]
    public void HasWildcards_RespectsEscapesAndColonStarPrefix(string input, bool expected)
    {
        Assert.Equal(expected, PermissionRuleMatcher.HasWildcards(input));
    }

    [Theory]
    [InlineData("git status", "git status", true)]
    [InlineData("git:*", "git status", true)]
    [InlineData("git:*", "git", true)]
    [InlineData("git:*", "gits", false)]
    [InlineData("*.md", "readme.md", true)]
    [InlineData("*.md", "readme.txt", false)]
    public void MatchWildcardAndPrefixRules_WorkAsExpected(string ruleContent, string target, bool expected)
    {
        Assert.Equal(expected, PermissionRuleMatcher.MatchesRuleContent(ruleContent, target));
    }

    [Fact]
    public void FindFirstMatch_RespectsToolAliasesAndRuleOrder()
    {
        var tool = FoundationTestHelpers.Tool("Bash", ["Shell"]);
        var input = JsonSerializer.SerializeToElement(new { command = "git status" });
        var rules = new[]
        {
            PermissionRule.Create(PermissionBehavior.Deny, "Other", "git status"),
            PermissionRule.Create(PermissionBehavior.Ask, "Shell", "git status"),
            PermissionRule.Create(PermissionBehavior.Allow, "Bash", "git status"),
        };

        var match = PermissionRuleMatcher.FindFirstMatch(rules, tool, input);

        Assert.NotNull(match);
        Assert.Equal(PermissionBehavior.Ask, match!.Behavior);
    }

    [Fact]
    public void ExtractRuleTarget_LooksAtCommandAndPathFields()
    {
        var command = PermissionRuleMatcher.ExtractRuleTarget(JsonSerializer.SerializeToElement(new { command = "git status" }));
        var path = PermissionRuleMatcher.ExtractRuleTarget(JsonSerializer.SerializeToElement(new { path = "/tmp/file.txt" }));
        var filePath = PermissionRuleMatcher.ExtractRuleTarget(JsonSerializer.SerializeToElement(new { file_path = "/tmp/other.txt" }));

        Assert.Equal("git status", command);
        Assert.Equal("/tmp/file.txt", path);
        Assert.Equal("/tmp/other.txt", filePath);
    }

    [Fact]
    public void Matches_TreatsWhitespaceRuleContentAsMatchWhenToolMatches()
    {
        var tool = FoundationTestHelpers.Tool("Bash", ["Shell"]);
        var rule = new PermissionRule
        {
            Behavior = PermissionBehavior.Allow,
            ToolName = "shell",
            RuleContent = "   ",
        };

        Assert.True(PermissionRuleMatcher.Matches(rule, tool, null));
    }

    [Fact]
    public void MatchesRuleContent_RespectsCaseSensitivityWhenRequested()
    {
        Assert.True(PermissionRuleMatcher.MatchesRuleContent("Git Status", "git status"));
        Assert.False(PermissionRuleMatcher.MatchesRuleContent("Git Status", "git status", caseInsensitive: false));
    }

    [Fact]
    public void MatchWildcardPattern_CanBeCaseInsensitive()
    {
        Assert.True(PermissionRuleMatcher.MatchWildcardPattern("FOO*", "foo/bar", caseInsensitive: true));
    }
}

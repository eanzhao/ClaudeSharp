using System.Text.Json;
using ClaudeSharp.Core.Permissions;

namespace ClaudeSharp.Core.Tests.Foundations;

/// <summary>
/// Contains tests for permission Rules Deep.
/// </summary>
public sealed class PermissionRulesDeepTests
{
    [Fact]
    public void Matches_ReturnsTrueForExactToolNameAndRuleWithoutContent()
    {
        var tool = FoundationTestHelpers.Tool("Bash", ["Shell"]);
        var exactRule = PermissionRule.Create(PermissionBehavior.Allow, "bash", "git status");
        var noContentRule = PermissionRule.Create(PermissionBehavior.Allow, "Bash");

        Assert.True(PermissionRuleMatcher.Matches(exactRule, tool, "git status"));
        Assert.True(PermissionRuleMatcher.Matches(noContentRule, tool, null));
    }

    [Fact]
    public void Matches_ReturnsFalseWhenContentIsMissingOrToolDoesNotMatch()
    {
        var tool = FoundationTestHelpers.Tool("Bash");
        var rule = PermissionRule.Create(PermissionBehavior.Allow, "Other", "git status");

        Assert.False(PermissionRuleMatcher.Matches(rule, tool, null));
        Assert.False(PermissionRuleMatcher.Matches(rule, tool, "git status"));
    }

    [Fact]
    public void FindFirstMatch_ReturnsNullWhenNoRuleMatches()
    {
        var tool = FoundationTestHelpers.Tool("Bash");
        var rules = new[]
        {
            PermissionRule.Create(PermissionBehavior.Allow, "Other", "git status"),
            PermissionRule.Create(PermissionBehavior.Ask, "Shell", "git push"),
        };

        Assert.Null(PermissionRuleMatcher.FindFirstMatch(rules, tool, JsonSerializer.SerializeToElement(new { command = "git status" })));
    }

    [Fact]
    public void ExtractRuleTarget_IgnoresEmptyValues_AndReturnsNullWhenNoTargetFieldsExist()
    {
        Assert.Null(PermissionRuleMatcher.ExtractRuleTarget(JsonSerializer.SerializeToElement(new { command = "" })));
        Assert.Null(PermissionRuleMatcher.ExtractRuleTarget(JsonSerializer.SerializeToElement(new { other = "value" })));
    }

    [Fact]
    public void MatchWildcardPattern_SupportsEscapedStarAndBackslash()
    {
        Assert.True(PermissionRuleMatcher.MatchWildcardPattern(@"foo\*bar\\baz", @"foo*bar\baz", caseInsensitive: false));
        Assert.False(PermissionRuleMatcher.MatchWildcardPattern(@"foo\*bar\\baz", @"fooXbar\baz", caseInsensitive: false));
    }

    [Theory]
    [InlineData("git:*", "git/status", true)]
    [InlineData("git:*", "git\\status", true)]
    [InlineData("git:*", "gxt status", false)]
    public void PrefixMatching_RespectsWhitespaceSlashAndNonMatches(string ruleContent, string target, bool expected)
    {
        Assert.Equal(expected, PermissionRuleMatcher.MatchesRuleContent(ruleContent, target));
    }
}

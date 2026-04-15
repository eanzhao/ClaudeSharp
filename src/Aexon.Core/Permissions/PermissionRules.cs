using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aexon.Core.Tools;

namespace Aexon.Core.Permissions;

/// <summary>
/// Represents permission rule.
/// </summary>
public sealed record PermissionRule
{
    public required PermissionBehavior Behavior { get; init; }
    public required string ToolName { get; init; }
    public string? RuleContent { get; init; }

    public static PermissionRule Create(
        PermissionBehavior behavior,
        string toolName,
        string? ruleContent = null)
    {
        if (string.IsNullOrWhiteSpace(toolName))
            throw new ArgumentException("Tool name is required.", nameof(toolName));

        return new PermissionRule
        {
            Behavior = behavior,
            ToolName = toolName.Trim(),
            RuleContent = string.IsNullOrWhiteSpace(ruleContent) ? null : ruleContent.Trim(),
        };
    }

    /// <summary>
    /// Handles parse.
    /// </summary>
    public static PermissionRule Parse(PermissionBehavior behavior, string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            throw new ArgumentException("Rule expression is required.", nameof(expression));

        var trimmed = expression.Trim();
        var openIndex = trimmed.IndexOf('(');

        if (openIndex <= 0 || !trimmed.EndsWith(")", StringComparison.Ordinal))
            return Create(behavior, trimmed);

        var toolName = trimmed[..openIndex].Trim();
        var content = trimmed[(openIndex + 1)..^1].Trim();
        return Create(behavior, toolName, content);
    }

    public string ToExpression() =>
        string.IsNullOrWhiteSpace(RuleContent)
            ? ToolName
            : $"{ToolName}({RuleContent})";
}

/// <summary>
/// Defines permission rule match kind values.
/// </summary>
internal enum PermissionRuleMatchKind
{
    Exact,
    Prefix,
    Wildcard,
}

/// <summary>
/// Represents permission rule matcher.
/// </summary>
public static class PermissionRuleMatcher
{
    private const string EscapedStarPlaceholder = "\0ESCAPED_STAR\0";
    private const string EscapedBackslashPlaceholder = "\0ESCAPED_BACKSLASH\0";

    public static PermissionRule? FindFirstMatch(
        IEnumerable<PermissionRule> rules,
        ITool tool,
        JsonElement input)
    {
        var target = ExtractRuleTarget(input);

        foreach (var rule in rules)
        {
            if (Matches(rule, tool, target))
                return rule;
        }

        return null;
    }

    public static bool Matches(PermissionRule rule, ITool tool, string? target)
    {
        if (!MatchesTool(rule, tool))
            return false;

        if (string.IsNullOrWhiteSpace(rule.RuleContent))
            return true;

        if (string.IsNullOrWhiteSpace(target))
            return false;

        return MatchesRuleContent(rule.RuleContent, target);
    }

    public static string? ExtractRuleTarget(JsonElement input)
    {
        foreach (var propertyName in new[] { "command", "file_path", "path" })
        {
            if (input.TryGetProperty(propertyName, out var property) &&
                property.ValueKind == JsonValueKind.String)
            {
                var value = property.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }
        }

        return null;
    }

    public static bool MatchesRuleContent(
        string ruleContent,
        string target,
        bool caseInsensitive = true)
    {
        var comparison = caseInsensitive
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        var parsed = ParseRuleContent(ruleContent);
        var candidate = target.Trim();

        return parsed.Kind switch
        {
            PermissionRuleMatchKind.Exact =>
                string.Equals(parsed.Value, candidate, comparison),
            PermissionRuleMatchKind.Prefix =>
                MatchesPrefix(parsed.Value, candidate, comparison),
            PermissionRuleMatchKind.Wildcard =>
                MatchWildcardPattern(parsed.Value, candidate, caseInsensitive),
            _ => false,
        };
    }

    public static string? ExtractPrefix(string permissionRule)
    {
        var match = Regex.Match(permissionRule, @"^(.+):\*$");
        return match.Success ? match.Groups[1].Value : null;
    }

    public static bool HasWildcards(string pattern)
    {
        if (pattern.EndsWith(":*", StringComparison.Ordinal))
            return false;

        for (var i = 0; i < pattern.Length; i++)
        {
            if (pattern[i] != '*')
                continue;

            var backslashCount = 0;
            for (var j = i - 1; j >= 0 && pattern[j] == '\\'; j--)
                backslashCount++;

            if (backslashCount % 2 == 0)
                return true;
        }

        return false;
    }

    public static bool MatchWildcardPattern(
        string pattern,
        string command,
        bool caseInsensitive = false)
    {
        var trimmedPattern = pattern.Trim();
        var processed = new StringBuilder();

        for (var i = 0; i < trimmedPattern.Length; i++)
        {
            var current = trimmedPattern[i];

            if (current == '\\' && i + 1 < trimmedPattern.Length)
            {
                var next = trimmedPattern[i + 1];
                if (next == '*')
                {
                    processed.Append(EscapedStarPlaceholder);
                    i++;
                    continue;
                }

                if (next == '\\')
                {
                    processed.Append(EscapedBackslashPlaceholder);
                    i++;
                    continue;
                }
            }

            processed.Append(current);
        }

        var processedString = processed.ToString();
        var regexPattern = EscapeRegexExceptStar(processedString)
            .Replace("*", ".*", StringComparison.Ordinal)
            .Replace(EscapedStarPlaceholder, "\\*", StringComparison.Ordinal)
            .Replace(EscapedBackslashPlaceholder, "\\\\", StringComparison.Ordinal);

        var options = RegexOptions.Singleline;
        if (caseInsensitive)
            options |= RegexOptions.IgnoreCase;

        return Regex.IsMatch(command, $"^{regexPattern}$", options);
    }

    private static bool MatchesTool(PermissionRule rule, ITool tool)
    {
        if (string.Equals(rule.ToolName, tool.Name, StringComparison.OrdinalIgnoreCase))
            return true;

        return tool.Aliases.Any(alias =>
            string.Equals(rule.ToolName, alias, StringComparison.OrdinalIgnoreCase));
    }

    private static (PermissionRuleMatchKind Kind, string Value) ParseRuleContent(string ruleContent)
    {
        var prefix = ExtractPrefix(ruleContent);
        if (prefix != null)
            return (PermissionRuleMatchKind.Prefix, prefix.Trim());

        if (HasWildcards(ruleContent))
            return (PermissionRuleMatchKind.Wildcard, ruleContent);

        return (PermissionRuleMatchKind.Exact, ruleContent.Trim());
    }

    private static bool MatchesPrefix(
        string prefix,
        string candidate,
        StringComparison comparison)
    {
        if (string.Equals(prefix, candidate, comparison))
            return true;

        if (!candidate.StartsWith(prefix, comparison) || candidate.Length <= prefix.Length)
            return false;

        var next = candidate[prefix.Length];
        return char.IsWhiteSpace(next) || next is '/' or '\\';
    }

    private static string EscapeRegexExceptStar(string value) =>
        Regex.Escape(value).Replace(@"\*", "*", StringComparison.Ordinal);
}

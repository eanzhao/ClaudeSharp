using System.Collections;
using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;

namespace Aexon.Core.Markdown;

/// <summary>
/// Parses Markdown frontmatter and normalizes the extracted values.
/// </summary>
public static partial class FrontmatterParser
{
    private static readonly Regex FrontmatterRegex = FrontmatterRegexFactory();
    private static readonly Regex YamlSpecialCharsRegex = YamlSpecialCharsRegexFactory();
    private static readonly Regex SimpleKeyValueRegex = SimpleKeyValueRegexFactory();
    private static readonly IDeserializer Deserializer = new DeserializerBuilder().Build();

    public static ParsedMarkdown Parse(string markdown)
    {
        var match = FrontmatterRegex.Match(markdown);
        if (!match.Success)
        {
            return new ParsedMarkdown(
                new Dictionary<string, object?>(),
                markdown);
        }

        var frontmatterText = match.Groups[1].Value;
        var content = markdown[match.Length..];

        if (TryDeserialize(frontmatterText, out var frontmatter))
            return new ParsedMarkdown(frontmatter, content);

        var quoted = QuoteProblematicValues(frontmatterText);
        return TryDeserialize(quoted, out frontmatter)
            ? new ParsedMarkdown(frontmatter, content)
            : new ParsedMarkdown(new Dictionary<string, object?>(), content);
    }

    public static IReadOnlyList<string> SplitPathValue(object? input)
    {
        if (input == null)
            return Array.Empty<string>();

        if (input is string text)
            return SplitCommaSeparatedWithBraceExpansion(text);

        if (input is IEnumerable enumerable)
        {
            var values = new List<string>();
            foreach (var item in enumerable)
                values.AddRange(SplitPathValue(item));

            return values;
        }

        return Array.Empty<string>();
    }

    public static int? ParsePositiveInt(object? value)
    {
        if (value == null)
            return null;

        if (value is int intValue && intValue > 0)
            return intValue;

        if (int.TryParse(Convert.ToString(value), out var parsed) && parsed > 0)
            return parsed;

        return null;
    }

    public static bool ParseBoolean(object? value) =>
        value is true || string.Equals(Convert.ToString(value), "true", StringComparison.OrdinalIgnoreCase);

    private static bool TryDeserialize(
        string yaml,
        out Dictionary<string, object?> frontmatter)
    {
        try
        {
            var parsed = Deserializer.Deserialize<object?>(yaml);
            if (NormalizeYamlNode(parsed) is Dictionary<string, object?> dict)
            {
                frontmatter = dict;
                return true;
            }
        }
        catch
        {
            // Ignore and fall back.
        }

        frontmatter = new Dictionary<string, object?>();
        return false;
    }

    private static object? NormalizeYamlNode(object? node)
    {
        switch (node)
        {
            case null:
                return null;
            case string or bool or byte or sbyte or short or ushort or int or uint or long or ulong
                or float or double or decimal:
                return node;
            case IDictionary dictionary:
                {
                    var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    foreach (DictionaryEntry entry in dictionary)
                    {
                        var key = Convert.ToString(entry.Key);
                        if (string.IsNullOrWhiteSpace(key))
                            continue;

                        result[key] = NormalizeYamlNode(entry.Value);
                    }

                    return result;
                }
            case IEnumerable enumerable:
                {
                    var result = new List<object?>();
                    foreach (var item in enumerable)
                        result.Add(NormalizeYamlNode(item));

                    return result;
                }
            default:
                return Convert.ToString(node);
        }
    }

    private static string QuoteProblematicValues(string frontmatterText)
    {
        var lines = frontmatterText.Split('\n');
        var result = new List<string>(lines.Length);

        foreach (var line in lines)
        {
            var match = SimpleKeyValueRegex.Match(line);
            if (!match.Success)
            {
                result.Add(line);
                continue;
            }

            var key = match.Groups[1].Value;
            var value = match.Groups[2].Value;

            var isQuoted = value.Length >= 2 &&
                           ((value.StartsWith("\"", StringComparison.Ordinal) &&
                             value.EndsWith("\"", StringComparison.Ordinal)) ||
                            (value.StartsWith("'", StringComparison.Ordinal) &&
                             value.EndsWith("'", StringComparison.Ordinal)));

            if (isQuoted || !YamlSpecialCharsRegex.IsMatch(value))
            {
                result.Add(line);
                continue;
            }

            var escaped = value.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal);
            result.Add($"{key}: \"{escaped}\"");
        }

        return string.Join('\n', result);
    }

    private static IReadOnlyList<string> SplitCommaSeparatedWithBraceExpansion(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return Array.Empty<string>();

        var parts = new List<string>();
        var current = new StringBuilder();
        var braceDepth = 0;

        foreach (var ch in input)
        {
            switch (ch)
            {
                case '{':
                    braceDepth++;
                    current.Append(ch);
                    break;
                case '}':
                    braceDepth--;
                    current.Append(ch);
                    break;
                case ',' when braceDepth == 0:
                    AddIfNotBlank(parts, current.ToString());
                    current.Clear();
                    break;
                default:
                    current.Append(ch);
                    break;
            }
        }

        AddIfNotBlank(parts, current.ToString());
        return parts.SelectMany(ExpandBraces).ToList();
    }

    private static IReadOnlyList<string> ExpandBraces(string pattern)
    {
        var match = Regex.Match(pattern, @"^([^{]*)\{([^}]+)\}(.*)$");
        if (!match.Success)
            return new[] { pattern };

        var prefix = match.Groups[1].Value;
        var alternatives = match.Groups[2].Value
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var suffix = match.Groups[3].Value;

        var expanded = new List<string>();
        foreach (var alternative in alternatives)
        {
            var combined = prefix + alternative + suffix;
            expanded.AddRange(ExpandBraces(combined));
        }

        return expanded;
    }

    private static void AddIfNotBlank(List<string> values, string value)
    {
        var trimmed = value.Trim();
        if (!string.IsNullOrEmpty(trimmed))
            values.Add(trimmed);
    }

    [GeneratedRegex(@"^---\s*\n([\s\S]*?)---\s*\n?", RegexOptions.Compiled)]
    private static partial Regex FrontmatterRegexFactory();

    [GeneratedRegex(@"[{}\[\]*&#!|>%@`]|: ", RegexOptions.Compiled)]
    private static partial Regex YamlSpecialCharsRegexFactory();

    [GeneratedRegex(@"^([a-zA-Z_-]+):\s+(.+)$", RegexOptions.Compiled)]
    private static partial Regex SimpleKeyValueRegexFactory();
}

/// <summary>
/// Represents parsed markdown.
/// </summary>
public sealed record ParsedMarkdown(
    IReadOnlyDictionary<string, object?> Frontmatter,
    string Content);

using System.Collections;
using System.Reflection;
using ClaudeSharp.Core.Markdown;

namespace ClaudeSharp.Core.Tests.Foundations;

/// <summary>
/// Contains tests for frontmatter Parser Deep.
/// </summary>
public sealed class FrontmatterParserDeepTests
{
    [Fact]
    public void Parse_RetriesQuotedFrontmatterValues_AndNormalizesNestedCollections()
    {
        var parsed = FrontmatterParser.Parse("""
        ---
        title: hello
        summary: value: with colon
        tags:
          - one
          - two
        metadata:
          answer: 42
        ---
        body
        """);

        Assert.Equal("body", parsed.Content);
        Assert.Equal("hello", parsed.Frontmatter["title"]);
        Assert.Equal("value: with colon", parsed.Frontmatter["summary"]);

        var tags = Assert.IsType<List<object?>>(parsed.Frontmatter["tags"]);
        Assert.Equal(["one", "two"], tags);

        var metadata = Assert.IsType<Dictionary<string, object?>>(parsed.Frontmatter["metadata"]);
        Assert.Equal(42, Convert.ToInt32(metadata["answer"]));
    }

    [Fact]
    public void SplitPathValue_ReturnsEmptyForNullBlankAndUnsupportedValues()
    {
        Assert.Empty(FrontmatterParser.SplitPathValue(null));
        Assert.Empty(FrontmatterParser.SplitPathValue("   "));
        Assert.Empty(FrontmatterParser.SplitPathValue(new object()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-2)]
    public void ParsePositiveInt_ReturnsNullForMissingOrNonPositiveValues(object? input)
    {
        Assert.Null(FrontmatterParser.ParsePositiveInt(input));
    }

    [Fact]
    public void NormalizeYamlNode_HandlesNullBlankKeysEnumerablesAndFallbackValues()
    {
        Assert.Null(InvokeNormalize(null));

        var dictionary = new Hashtable
        {
            [""] = "skip",
            ["name"] = 1,
        };

        var normalizedDictionary = Assert.IsType<Dictionary<string, object?>>(InvokeNormalize(dictionary));
        Assert.False(normalizedDictionary.ContainsKey(string.Empty));
        Assert.Equal(1, normalizedDictionary["name"]);

        var normalizedEnumerable = Assert.IsType<List<object?>>(InvokeNormalize(new ArrayList { "one", 2 }));
        Assert.Equal(["one", 2], normalizedEnumerable);

        Assert.Equal("custom-value", InvokeNormalize(new CustomValue()));
    }

    private static object? InvokeNormalize(object? value)
    {
        var method = typeof(FrontmatterParser).GetMethod(
            "NormalizeYamlNode",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        return method!.Invoke(null, [value]);
    }

    private sealed class CustomValue
    {
        public override string ToString() => "custom-value";
    }
}

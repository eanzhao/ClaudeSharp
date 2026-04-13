using Aexon.Core.Markdown;

namespace Aexon.Core.Tests.Foundations;

/// <summary>
/// Contains tests for frontmatter Parser.
/// </summary>
public sealed class FrontmatterParserTests
{
    [Fact]
    public void Parse_ReturnsFrontmatterAndContentWhenYamlIsValid()
    {
        var parsed = FrontmatterParser.Parse("""
        ---
        title: hello
        paths: src/**,docs/{a,b}.md
        enabled: true
        count: 3
        ---
        body text
        """);

        Assert.Equal("body text", parsed.Content);
        Assert.Equal("hello", parsed.Frontmatter["title"]);
        Assert.True(FrontmatterParser.ParseBoolean(parsed.Frontmatter["enabled"]));
        Assert.Equal(3, FrontmatterParser.ParsePositiveInt(parsed.Frontmatter["count"]));

        var paths = FrontmatterParser.SplitPathValue(parsed.Frontmatter["paths"]);
        Assert.Equal(["src/**", "docs/a.md", "docs/b.md"], paths);
    }

    [Fact]
    public void Parse_FallsBackToContentWhenFrontmatterIsMissingOrScalar()
    {
        var withoutFrontmatter = FrontmatterParser.Parse("plain body");
        var scalarFrontmatter = FrontmatterParser.Parse("""
        ---
        hello
        ---
        body
        """);

        Assert.Empty(withoutFrontmatter.Frontmatter);
        Assert.Equal("plain body", withoutFrontmatter.Content);
        Assert.Empty(scalarFrontmatter.Frontmatter);
        Assert.Equal("body", scalarFrontmatter.Content);
    }

    [Fact]
    public void SplitPathValue_FlattensEnumerablesAndExpandsBraces()
    {
        var values = FrontmatterParser.SplitPathValue(new object?[]
        {
            "src/{a,b}.cs,docs/*.md",
            new[] { "test/{x,y}.txt", "readme.md" },
        });

        Assert.Equal(["src/a.cs", "src/b.cs", "docs/*.md", "test/x.txt", "test/y.txt", "readme.md"], values);
    }

    [Theory]
    [InlineData(5, 5)]
    [InlineData("7", 7)]
    [InlineData("-1", null)]
    [InlineData("abc", null)]
    public void ParsePositiveInt_HandlesMultipleInputShapes(object? input, int? expected)
    {
        Assert.Equal(expected, FrontmatterParser.ParsePositiveInt(input));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData("true", true)]
    [InlineData("False", false)]
    [InlineData("anything-else", false)]
    public void ParseBoolean_HandlesBoolAndStringValues(object? input, bool expected)
    {
        Assert.Equal(expected, FrontmatterParser.ParseBoolean(input));
    }
}

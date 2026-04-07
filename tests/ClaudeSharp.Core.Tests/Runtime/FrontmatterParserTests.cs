using ClaudeSharp.Core.Markdown;

namespace ClaudeSharp.Core.Tests.Runtime;

/// <summary>
/// Contains tests for frontmatter Parser.
/// </summary>
public sealed class FrontmatterParserTests
{
    [Fact]
    public void Parse_ReturnsContentWhenFrontmatterIsMissing()
    {
        var parsed = FrontmatterParser.Parse("plain body\n");

        Assert.Empty(parsed.Frontmatter);
        Assert.Contains("plain body", parsed.Content);
    }

    [Fact]
    public void Parse_RetriesWithQuotedValuesWhenYamlNeedsIt()
    {
        var parsed = FrontmatterParser.Parse("""
---
paths: src/**
title: hello:world
---
body
""");

        Assert.Equal("src/**", parsed.Frontmatter["paths"]);
        Assert.Equal("hello:world", parsed.Frontmatter["title"]);
        Assert.Contains("body", parsed.Content);
    }

    [Fact]
    public void Parse_FallsBackToContentWhenFrontmatterIsScalar()
    {
        var parsed = FrontmatterParser.Parse("""
---
scalar
---
body
""");

        Assert.Empty(parsed.Frontmatter);
        Assert.Contains("body", parsed.Content);
    }

    [Fact]
    public void SplitPathValue_ExpandsBracePatternsAndNestedEnumerables()
    {
        var values = FrontmatterParser.SplitPathValue(new object?[]
        {
            "{src,lib}/**",
            new[] { "docs/**" },
        });

        Assert.Equal(["src/**", "lib/**", "docs/**"], values);
    }

    [Fact]
    public void ParsePositiveInt_AndParseBoolean_HandleCommonInputs()
    {
        Assert.Equal(5, FrontmatterParser.ParsePositiveInt(5));
        Assert.Equal(12, FrontmatterParser.ParsePositiveInt("12"));
        Assert.Null(FrontmatterParser.ParsePositiveInt(0));
        Assert.Null(FrontmatterParser.ParsePositiveInt("nope"));

        Assert.True(FrontmatterParser.ParseBoolean(true));
        Assert.True(FrontmatterParser.ParseBoolean("TRUE"));
        Assert.False(FrontmatterParser.ParseBoolean(false));
        Assert.False(FrontmatterParser.ParseBoolean("false"));
    }
}

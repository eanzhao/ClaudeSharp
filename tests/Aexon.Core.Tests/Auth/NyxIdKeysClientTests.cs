using Aexon.Core.Auth;

namespace Aexon.Core.Tests.Auth;

public class NyxIdKeysClientTests
{
    [Fact]
    public void ParseOpenAiModelList_PicksDataIds()
    {
        // Standard OpenAI /v1/models shape: { "data": [ { "id": "..." } ] }
        const string json = """
            {
              "object": "list",
              "data": [
                { "id": "gpt-4o", "object": "model" },
                { "id": "gpt-4o-mini", "object": "model" }
              ]
            }
            """;

        var result = NyxIdKeysClient.ParseOpenAiModelList(json);

        Assert.NotNull(result);
        Assert.Equal(new[] { "gpt-4o", "gpt-4o-mini" }, result!);
    }

    [Fact]
    public void ParseOpenAiModelList_AcceptsModelsArrayOfStrings()
    {
        // Some NyxID-proxied services (e.g. aelf llm.aelf.dev) return
        // `{ "models": ["claude-sonnet", "claude-haiku"] }` instead.
        const string json = """
            { "models": ["claude-sonnet-4-6", "claude-haiku-4-5"] }
            """;

        var result = NyxIdKeysClient.ParseOpenAiModelList(json);

        Assert.NotNull(result);
        Assert.Equal(new[] { "claude-sonnet-4-6", "claude-haiku-4-5" }, result!);
    }

    [Fact]
    public void ParseOpenAiModelList_AcceptsModelsArrayOfObjects()
    {
        const string json = """
            {
              "models": [
                { "id": "qwen3-coder", "family": "qwen" },
                { "id": "kimi-2" }
              ]
            }
            """;

        var result = NyxIdKeysClient.ParseOpenAiModelList(json);

        Assert.NotNull(result);
        Assert.Equal(new[] { "qwen3-coder", "kimi-2" }, result!);
    }

    [Fact]
    public void ParseOpenAiModelList_ReturnsNullForNonLlmShape()
    {
        // Arbitrary service body that isn't OpenAI-shaped — picker should
        // skip this service rather than surface it as an LLM candidate.
        const string json = """
            { "status": "ok", "version": "1.2.3" }
            """;

        Assert.Null(NyxIdKeysClient.ParseOpenAiModelList(json));
    }

    [Fact]
    public void ParseOpenAiModelList_ReturnsNullForMalformedJson()
    {
        Assert.Null(NyxIdKeysClient.ParseOpenAiModelList("not even json"));
    }

    [Fact]
    public void ParseOpenAiModelList_ReturnsNullForEmptyBody()
    {
        Assert.Null(NyxIdKeysClient.ParseOpenAiModelList(string.Empty));
        Assert.Null(NyxIdKeysClient.ParseOpenAiModelList("   "));
    }

    [Fact]
    public void ParseOpenAiModelList_SkipsDataEntriesWithoutStringId()
    {
        const string json = """
            {
              "data": [
                { "id": "good-model" },
                { "id": null },
                { "other": "no-id-here" },
                { "id": "" },
                { "id": "another-good-model" }
              ]
            }
            """;

        var result = NyxIdKeysClient.ParseOpenAiModelList(json);

        Assert.NotNull(result);
        Assert.Equal(new[] { "good-model", "another-good-model" }, result!);
    }
}

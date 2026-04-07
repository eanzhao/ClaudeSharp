namespace ClaudeSharp.Core.Query;

/// <summary>
/// Claude 模型目录。
/// 参考 Claude Code 的 utils/model/configs.ts / aliases.ts / model.ts，
/// 把模型别名、稳定 ID、provider-specific ID 整理成一个可复用表。
/// </summary>
public static class ClaudeModelCatalog
{
    private static readonly IReadOnlyList<ClaudeModelDescriptor> Models =
    [
        new(
            StableId: "claude-3-5-haiku",
            SourceCanonicalId: "claude-3-5-haiku-20241022",
            Family: ClaudeModelFamily.Haiku,
            ProviderIds: new ClaudeModelProviderIds(
                FirstParty: "claude-3-5-haiku-20241022",
                Bedrock: "us.anthropic.claude-3-5-haiku-20241022-v1:0",
                Vertex: "claude-3-5-haiku@20241022",
                Foundry: "claude-3-5-haiku"),
            Aliases: ["claude-3-5-haiku"]),
        new(
            StableId: "claude-3-5-sonnet",
            SourceCanonicalId: "claude-3-5-sonnet-20241022",
            Family: ClaudeModelFamily.Sonnet,
            ProviderIds: new ClaudeModelProviderIds(
                FirstParty: "claude-3-5-sonnet-20241022",
                Bedrock: "anthropic.claude-3-5-sonnet-20241022-v2:0",
                Vertex: "claude-3-5-sonnet-v2@20241022",
                Foundry: "claude-3-5-sonnet"),
            Aliases: ["claude-3-5-sonnet"]),
        new(
            StableId: "claude-3-7-sonnet",
            SourceCanonicalId: "claude-3-7-sonnet-20250219",
            Family: ClaudeModelFamily.Sonnet,
            ProviderIds: new ClaudeModelProviderIds(
                FirstParty: "claude-3-7-sonnet-20250219",
                Bedrock: "us.anthropic.claude-3-7-sonnet-20250219-v1:0",
                Vertex: "claude-3-7-sonnet@20250219",
                Foundry: "claude-3-7-sonnet"),
            Aliases: ["claude-3-7-sonnet"]),
        new(
            StableId: "claude-haiku-4-5",
            SourceCanonicalId: "claude-haiku-4-5-20251001",
            Family: ClaudeModelFamily.Haiku,
            ProviderIds: new ClaudeModelProviderIds(
                FirstParty: "claude-haiku-4-5-20251001",
                Bedrock: "us.anthropic.claude-haiku-4-5-20251001-v1:0",
                Vertex: "claude-haiku-4-5@20251001",
                Foundry: "claude-haiku-4-5"),
            Aliases: ["haiku", "claude-haiku", "haiku-4-5"]),
        new(
            StableId: "claude-sonnet-4",
            SourceCanonicalId: "claude-sonnet-4-20250514",
            Family: ClaudeModelFamily.Sonnet,
            ProviderIds: new ClaudeModelProviderIds(
                FirstParty: "claude-sonnet-4-20250514",
                Bedrock: "us.anthropic.claude-sonnet-4-20250514-v1:0",
                Vertex: "claude-sonnet-4@20250514",
                Foundry: "claude-sonnet-4"),
            Aliases: ["claude-sonnet-4", "sonnet-4"]),
        new(
            StableId: "claude-sonnet-4-5",
            SourceCanonicalId: "claude-sonnet-4-5-20250929",
            Family: ClaudeModelFamily.Sonnet,
            ProviderIds: new ClaudeModelProviderIds(
                FirstParty: "claude-sonnet-4-5-20250929",
                Bedrock: "us.anthropic.claude-sonnet-4-5-20250929-v1:0",
                Vertex: "claude-sonnet-4-5@20250929",
                Foundry: "claude-sonnet-4-5"),
            Aliases: ["claude-sonnet-4-5", "sonnet-4-5"]),
        new(
            StableId: "claude-sonnet-4-6",
            SourceCanonicalId: "claude-sonnet-4-6",
            Family: ClaudeModelFamily.Sonnet,
            ProviderIds: new ClaudeModelProviderIds(
                FirstParty: "claude-sonnet-4-6",
                Bedrock: "us.anthropic.claude-sonnet-4-6",
                Vertex: "claude-sonnet-4-6",
                Foundry: "claude-sonnet-4-6"),
            Aliases: ["default", "sonnet", "claude-sonnet", "sonnet-4-6", "claude-sonnet-4-6"]),
        new(
            StableId: "claude-opus-4",
            SourceCanonicalId: "claude-opus-4-20250514",
            Family: ClaudeModelFamily.Opus,
            ProviderIds: new ClaudeModelProviderIds(
                FirstParty: "claude-opus-4-20250514",
                Bedrock: "us.anthropic.claude-opus-4-20250514-v1:0",
                Vertex: "claude-opus-4@20250514",
                Foundry: "claude-opus-4"),
            Aliases: ["claude-opus-4", "opus-4"]),
        new(
            StableId: "claude-opus-4-1",
            SourceCanonicalId: "claude-opus-4-1-20250805",
            Family: ClaudeModelFamily.Opus,
            ProviderIds: new ClaudeModelProviderIds(
                FirstParty: "claude-opus-4-1-20250805",
                Bedrock: "us.anthropic.claude-opus-4-1-20250805-v1:0",
                Vertex: "claude-opus-4-1@20250805",
                Foundry: "claude-opus-4-1"),
            Aliases: ["claude-opus-4-1", "opus-4-1"]),
        new(
            StableId: "claude-opus-4-5",
            SourceCanonicalId: "claude-opus-4-5-20251101",
            Family: ClaudeModelFamily.Opus,
            ProviderIds: new ClaudeModelProviderIds(
                FirstParty: "claude-opus-4-5-20251101",
                Bedrock: "us.anthropic.claude-opus-4-5-20251101-v1:0",
                Vertex: "claude-opus-4-5@20251101",
                Foundry: "claude-opus-4-5"),
            Aliases: ["claude-opus-4-5", "opus-4-5"]),
        new(
            StableId: "claude-opus-4-6",
            SourceCanonicalId: "claude-opus-4-6",
            Family: ClaudeModelFamily.Opus,
            ProviderIds: new ClaudeModelProviderIds(
                FirstParty: "claude-opus-4-6",
                Bedrock: "us.anthropic.claude-opus-4-6-v1",
                Vertex: "claude-opus-4-6",
                Foundry: "claude-opus-4-6"),
            Aliases: ["best", "opus", "claude-opus", "opus-4-6", "claude-opus-4-6"]),
    ];

    private static readonly IReadOnlyDictionary<string, ClaudeModelDescriptor> AliasMap =
        BuildAliasMap();

    private static readonly IReadOnlyList<(string MatchText, string StableId)> Matchers =
        Models.SelectMany(model => model.GetMatchers())
            .OrderByDescending(item => item.MatchText.Length)
            .ToList();

    public static string DefaultMainLoopModel => "claude-sonnet-4-6";

    public static IReadOnlyList<string> CommonAliases => ["sonnet", "opus", "haiku"];

    public static IReadOnlyList<ClaudeModelDescriptor> All => Models;

    public static string ResolveModelOrAlias(string? modelOrAlias)
    {
        if (string.IsNullOrWhiteSpace(modelOrAlias))
            return DefaultMainLoopModel;

        var input = modelOrAlias.Trim();
        if (AliasMap.TryGetValue(input, out var directMatch))
            return directMatch.StableId;

        var normalized = Canonicalize(input);
        return normalized ?? input;
    }

    public static ClaudeModelDescriptor? TryResolve(string? modelOrAlias)
    {
        var resolved = ResolveModelOrAlias(modelOrAlias);
        return Models.FirstOrDefault(model =>
            string.Equals(model.StableId, resolved, StringComparison.OrdinalIgnoreCase));
    }

    public static string? Canonicalize(string modelId)
    {
        foreach (var matcher in Matchers)
        {
            if (modelId.Contains(matcher.MatchText, StringComparison.OrdinalIgnoreCase))
                return matcher.StableId;
        }

        return null;
    }

    private static IReadOnlyDictionary<string, ClaudeModelDescriptor> BuildAliasMap()
    {
        var map = new Dictionary<string, ClaudeModelDescriptor>(StringComparer.OrdinalIgnoreCase);

        foreach (var model in Models)
        {
            map[model.StableId] = model;
            map[model.SourceCanonicalId] = model;

            foreach (var alias in model.Aliases)
                map[alias] = model;
        }

        return map;
    }
}

public enum ClaudeModelFamily
{
    Sonnet,
    Opus,
    Haiku,
}

public sealed record ClaudeModelProviderIds(
    string FirstParty,
    string Bedrock,
    string Vertex,
    string Foundry);

public sealed record ClaudeModelDescriptor(
    string StableId,
    string SourceCanonicalId,
    ClaudeModelFamily Family,
    ClaudeModelProviderIds ProviderIds,
    IReadOnlyList<string> Aliases)
{
    public IEnumerable<(string MatchText, string StableId)> GetMatchers()
    {
        yield return (StableId, StableId);
        yield return (SourceCanonicalId, StableId);
        yield return (ProviderIds.FirstParty, StableId);
        yield return (ProviderIds.Bedrock, StableId);
        yield return (ProviderIds.Vertex, StableId);
        yield return (ProviderIds.Foundry, StableId);
    }
}

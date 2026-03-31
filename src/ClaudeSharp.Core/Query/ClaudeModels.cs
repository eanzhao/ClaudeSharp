namespace ClaudeSharp.Core.Query;

/// <summary>
/// Claude 模型别名与默认值。
/// 对齐当前 Claude Code 快照里默认主模型为 claude-sonnet-4-6。
/// </summary>
public static class ClaudeModels
{
    public const string DefaultMainModel = "claude-sonnet-4-6";

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["default"] = DefaultMainModel,
        ["sonnet"] = DefaultMainModel,
        ["claude-sonnet"] = DefaultMainModel,
        ["sonnet-4-6"] = DefaultMainModel,
        ["claude-sonnet-4-6"] = DefaultMainModel,
        ["sonnet-4-5"] = "claude-sonnet-4-5",
        ["claude-sonnet-4-5"] = "claude-sonnet-4-5",
        ["sonnet-4"] = "claude-sonnet-4",
        ["claude-sonnet-4"] = "claude-sonnet-4",
        ["opus"] = "claude-opus-4-6",
        ["claude-opus"] = "claude-opus-4-6",
        ["opus-4-6"] = "claude-opus-4-6",
        ["claude-opus-4-6"] = "claude-opus-4-6",
        ["haiku"] = "claude-haiku-4-5",
        ["claude-haiku"] = "claude-haiku-4-5",
        ["haiku-4-5"] = "claude-haiku-4-5",
        ["claude-haiku-4-5"] = "claude-haiku-4-5",
    };

    public static string Resolve(string? modelOrAlias)
    {
        if (string.IsNullOrWhiteSpace(modelOrAlias))
            return DefaultMainModel;

        var normalized = modelOrAlias.Trim();
        return Aliases.TryGetValue(normalized, out var resolved)
            ? resolved
            : normalized;
    }

    public static IReadOnlyList<string> CommonAliases => ["sonnet", "opus", "haiku"];
}

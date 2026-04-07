namespace ClaudeSharp.Core.Query;

/// <summary>
/// Claude 模型解析入口。
/// 内部改成委托给 ClaudeModelCatalog，这样模型元数据就能单独复用。
/// </summary>
public static class ClaudeModels
{
    public const string DefaultMainModel = "claude-sonnet-4-6";

    public static string Resolve(string? modelOrAlias)
        => ClaudeModelCatalog.ResolveModelOrAlias(modelOrAlias);

    public static IReadOnlyList<string> CommonAliases => ClaudeModelCatalog.CommonAliases;
}

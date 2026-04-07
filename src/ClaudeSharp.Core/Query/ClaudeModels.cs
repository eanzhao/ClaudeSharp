namespace ClaudeSharp.Core.Query;

/// <summary>
/// Exposes common model constants and alias resolution helpers.
/// </summary>
public static class ClaudeModels
{
    public const string DefaultMainModel = "claude-sonnet-4-6";

    public static string Resolve(string? modelOrAlias)
        => ClaudeModelCatalog.ResolveModelOrAlias(modelOrAlias);

    public static IReadOnlyList<string> CommonAliases => ClaudeModelCatalog.CommonAliases;
}

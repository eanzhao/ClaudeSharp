using Microsoft.Extensions.AI;

namespace Aexon.Core.Query;

/// <summary>
/// Applies shared chat-request defaults from the active query-engine configuration.
/// </summary>
public static class ChatClientOptionDefaults
{
    public static void Apply(ChatOptions options, QueryEngineConfig config)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(config);

        if (string.IsNullOrWhiteSpace(options.ModelId))
            options.ModelId = config.Model;

        var profile = QueryExecutionProfileResolver.Resolve(config);

        options.MaxOutputTokens ??= profile.MaxOutputTokens;
        options.AdditionalProperties ??= new AdditionalPropertiesDictionary();
        options.AdditionalProperties.TryAdd(ChatClientPropertyKeys.Effort, profile.Effort.ToString());
        options.AdditionalProperties.TryAdd(ChatClientPropertyKeys.ThinkingMode, profile.ThinkingMode.ToString());
        if (profile.ThinkingBudgetTokens is { } budget)
            options.AdditionalProperties.TryAdd(ChatClientPropertyKeys.ThinkingBudgetTokens, budget);
    }
}

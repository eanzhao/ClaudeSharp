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

        options.MaxOutputTokens ??= config.MaxTokens;
        options.AdditionalProperties ??= new AdditionalPropertiesDictionary();
        options.AdditionalProperties.TryAdd("ThinkingMode", config.ThinkingMode.ToString());
        options.AdditionalProperties.TryAdd("ThinkingBudgetTokens", config.ThinkingBudgetTokens);
    }
}

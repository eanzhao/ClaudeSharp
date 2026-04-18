namespace Aexon.Core.Providers;

/// <summary>
/// Per-million-token pricing for a model. Values are USD.
/// </summary>
public sealed record ModelPricing(
    decimal InputPer1M,
    decimal OutputPer1M,
    decimal CacheReadPer1M,
    decimal CacheWritePer1M)
{
    public static ModelPricing Unknown { get; } = new(0m, 0m, 0m, 0m);

    public decimal EstimateCostUsd(
        long inputTokens,
        long outputTokens,
        long cacheReadTokens = 0,
        long cacheWriteTokens = 0)
    {
        return
            inputTokens * InputPer1M / 1_000_000m +
            outputTokens * OutputPer1M / 1_000_000m +
            cacheReadTokens * CacheReadPer1M / 1_000_000m +
            cacheWriteTokens * CacheWritePer1M / 1_000_000m;
    }
}

namespace Aexon.Core.Query;

/// <summary>
/// Shared additional-property keys exchanged between QueryEngine and chat middleware.
/// </summary>
public static class ChatClientPropertyKeys
{
    public const string Effort = nameof(Effort);
    public const string ThinkingMode = nameof(ThinkingMode);
    public const string ThinkingBudgetTokens = nameof(ThinkingBudgetTokens);
    public const string ApiRequestTimeout = nameof(ApiRequestTimeout);
    public const string ApiMaxRetryCount = nameof(ApiMaxRetryCount);
    public const string ApiRetryBaseDelay = nameof(ApiRetryBaseDelay);
    public const string ApiRetryMaxDelay = nameof(ApiRetryMaxDelay);
    public const string ApiRetryBackoffMultiplier = nameof(ApiRetryBackoffMultiplier);
    public const string ApiQuotaStatus = nameof(ApiQuotaStatus);
}

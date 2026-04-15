using System.Net;

namespace Aexon.Core.Query;

/// <summary>
/// Captures the latest known API quota headers from Anthropic responses.
/// </summary>
public sealed record ApiQuotaStatus(
    HttpStatusCode StatusCode,
    int? RequestsLimit,
    int? RequestsRemaining,
    DateTimeOffset? RequestsResetAt,
    int? TokensLimit,
    int? TokensRemaining,
    DateTimeOffset? TokensResetAt,
    TimeSpan? RetryAfter)
{
    public static ApiQuotaStatus? FromHeaders(
        HttpStatusCode statusCode,
        IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers)
    {
        var map = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in headers)
            map[header.Key] = header.Value.ToArray();

        return FromSnapshot(new ApiQuotaSnapshot(statusCode, map));
    }

    public static ApiQuotaStatus? FromSnapshot(ApiQuotaSnapshot? snapshot)
    {
        if (snapshot == null)
            return null;

        var requestsLimit = TryGetInt(snapshot, "anthropic-ratelimit-requests-limit");
        var requestsRemaining = TryGetInt(snapshot, "anthropic-ratelimit-requests-remaining");
        var requestsResetAt = TryGetDateTimeOffset(snapshot, "anthropic-ratelimit-requests-reset");
        var tokensLimit = TryGetInt(snapshot, "anthropic-ratelimit-tokens-limit");
        var tokensRemaining = TryGetInt(snapshot, "anthropic-ratelimit-tokens-remaining");
        var tokensResetAt = TryGetDateTimeOffset(snapshot, "anthropic-ratelimit-tokens-reset");
        var retryAfter = TryGetRetryAfter(snapshot);

        var hasSignal =
            requestsLimit.HasValue ||
            requestsRemaining.HasValue ||
            requestsResetAt.HasValue ||
            tokensLimit.HasValue ||
            tokensRemaining.HasValue ||
            tokensResetAt.HasValue ||
            retryAfter.HasValue;

        return !hasSignal
            ? null
            : new ApiQuotaStatus(
                snapshot.StatusCode,
                requestsLimit,
                requestsRemaining,
                requestsResetAt,
                tokensLimit,
                tokensRemaining,
                tokensResetAt,
                retryAfter);
    }

    private static int? TryGetInt(ApiQuotaSnapshot snapshot, string headerName)
    {
        var raw = snapshot.GetHeaderValue(headerName);
        return int.TryParse(raw, out var value) ? value : null;
    }

    private static DateTimeOffset? TryGetDateTimeOffset(ApiQuotaSnapshot snapshot, string headerName)
    {
        var raw = snapshot.GetHeaderValue(headerName);
        return DateTimeOffset.TryParse(raw, out var value) ? value : null;
    }

    private static TimeSpan? TryGetRetryAfter(ApiQuotaSnapshot snapshot)
    {
        var raw = snapshot.GetHeaderValue("retry-after");
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        if (double.TryParse(raw, out var seconds) && seconds >= 0)
            return TimeSpan.FromSeconds(seconds);

        if (DateTimeOffset.TryParse(raw, out var retryAt))
        {
            var delay = retryAt - DateTimeOffset.UtcNow;
            return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
        }

        return null;
    }
}

/// <summary>
/// Normalized response snapshot used by retry logic and quota extraction.
/// </summary>
public sealed class ApiQuotaSnapshot(
    HttpStatusCode statusCode,
    IReadOnlyDictionary<string, IReadOnlyList<string>> headers)
{
    public HttpStatusCode StatusCode { get; } = statusCode;

    public IReadOnlyDictionary<string, IReadOnlyList<string>> Headers { get; } = headers;

    public string? GetHeaderValue(string name)
    {
        return Headers.TryGetValue(name, out var values) ? values.FirstOrDefault() : null;
    }
}

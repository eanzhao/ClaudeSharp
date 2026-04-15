using System.Collections.Concurrent;
using Aexon.Core.Query;

namespace Aexon.Cli;

/// <summary>
/// Captures raw HTTP response headers for the current async request scope.
/// </summary>
internal sealed class ApiResponseObserver
{
    private static readonly AsyncLocal<string?> CurrentRequestId = new();
    private readonly ConcurrentDictionary<string, ApiQuotaSnapshot> _snapshots = new(StringComparer.Ordinal);

    public ApiRequestScope BeginRequest()
    {
        var previousRequestId = CurrentRequestId.Value;
        var requestId = Guid.NewGuid().ToString("N");
        CurrentRequestId.Value = requestId;
        return new ApiRequestScope(this, requestId, previousRequestId);
    }

    public HttpClient CreateHttpClient(HttpMessageHandler? innerHandler = null)
    {
        return new HttpClient(
            new ApiResponseTrackingHandler(this)
            {
                InnerHandler = innerHandler ?? new HttpClientHandler(),
            },
            disposeHandler: true);
    }

    public bool TryTakeSnapshot(string requestId, out ApiQuotaSnapshot? snapshot)
    {
        if (_snapshots.TryRemove(requestId, out var captured))
        {
            snapshot = captured;
            return true;
        }

        snapshot = null;
        return false;
    }

    internal void Record(HttpResponseMessage response)
    {
        var requestId = CurrentRequestId.Value;
        if (string.IsNullOrWhiteSpace(requestId))
            return;

        var headers = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var header in response.Headers)
            headers[header.Key] = header.Value.ToArray();

        if (response.Content != null)
        {
            foreach (var header in response.Content.Headers)
                headers[header.Key] = header.Value.ToArray();
        }

        _snapshots[requestId] = new ApiQuotaSnapshot(response.StatusCode, headers);
    }

    internal void Restore(string? previousRequestId) => CurrentRequestId.Value = previousRequestId;
}

internal sealed class ApiRequestScope : IDisposable
{
    private readonly ApiResponseObserver _observer;
    private readonly string? _previousRequestId;

    internal ApiRequestScope(
        ApiResponseObserver observer,
        string requestId,
        string? previousRequestId)
    {
        _observer = observer;
        RequestId = requestId;
        _previousRequestId = previousRequestId;
    }

    public string RequestId { get; }

    public void Dispose() => _observer.Restore(_previousRequestId);
}

internal sealed class ApiResponseTrackingHandler(ApiResponseObserver observer) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        observer.Record(response);
        return response;
    }
}

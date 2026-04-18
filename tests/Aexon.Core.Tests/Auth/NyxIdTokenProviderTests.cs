using System.Net;
using System.Text;
using System.Text.Json;
using Aexon.Core.Auth;
using Aexon.Core.Tests.Runtime;

namespace Aexon.Core.Tests.Auth;

public sealed class NyxIdTokenProviderTests
{
    private static readonly DateTimeOffset FixedNow = DateTimeOffset.Parse("2026-04-17T12:00:00Z");

    [Fact]
    public async Task GetValidAccessTokenAsync_RefreshesWhenTokenIsNearExpiry()
    {
        using var temp = new TempDirectory();
        var store = new NyxIdCredentialStore(temp.FullPath(".nyxid"), temp.FullPath("preferences.json"));
        var oldJwt = BuildJwt($$"""{"exp":{{FixedNow.AddSeconds(30).ToUnixTimeSeconds()}}}""");
        store.Save(new NyxIdCredentials
        {
            BaseUrl = "https://nyx.example",
            AccessToken = oldJwt,
            RefreshToken = "old-refresh",
            ExpiresAt = FixedNow.AddSeconds(30),
            ClientId = NyxIdAuthService.SyntheticClientId,
        });

        var newExp = FixedNow.AddSeconds(900).ToUnixTimeSeconds();
        var newJwt = BuildJwt($$"""{"exp":{{newExp}}}""");
        var handler = new RoutedHandler(
            refreshResponse: $$"""
                {
                  "access_token": "{{newJwt}}",
                  "refresh_token": "new-refresh"
                }
                """);
        var authService = new NyxIdAuthService(new HttpClient(handler), store, () => FixedNow);
        var provider = new NyxIdTokenProvider(store, authService, () => FixedNow);

        var accessToken = await provider.GetValidAccessTokenAsync();

        Assert.Equal(newJwt, accessToken);
        var refreshRequest = Assert.Single(handler.Requests);
        Assert.Equal(
            "https://nyx.example/api/v1/auth/refresh",
            refreshRequest.RequestUri?.ToString());

        var body = await refreshRequest.Content!.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("old-refresh", doc.RootElement.GetProperty("refresh_token").GetString());

        var persisted = store.Load()!;
        Assert.Equal(newJwt, persisted.AccessToken);
        Assert.Equal("new-refresh", persisted.RefreshToken);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(newExp), persisted.ExpiresAt);
    }

    [Fact]
    public async Task GetValidAccessTokenAsync_DoesNotRefreshWhenTokenIsStillFresh()
    {
        using var temp = new TempDirectory();
        var store = new NyxIdCredentialStore(temp.FullPath(".nyxid"), temp.FullPath("preferences.json"));
        var freshJwt = BuildJwt($$"""{"exp":{{FixedNow.AddMinutes(5).ToUnixTimeSeconds()}}}""");
        store.Save(new NyxIdCredentials
        {
            BaseUrl = "https://nyx.example",
            AccessToken = freshJwt,
            RefreshToken = "fresh-refresh",
            ExpiresAt = FixedNow.AddMinutes(5),
            ClientId = NyxIdAuthService.SyntheticClientId,
        });

        var handler = new CountingHandler();
        var authService = new NyxIdAuthService(new HttpClient(handler), store, () => FixedNow);
        var provider = new NyxIdTokenProvider(store, authService, () => FixedNow);

        var accessToken = await provider.GetValidAccessTokenAsync();

        Assert.Equal(freshJwt, accessToken);
        Assert.Equal(0, handler.CallCount);
    }

    private static string BuildJwt(string payloadJson)
    {
        var header = Base64UrlEncode(Encoding.UTF8.GetBytes("""{"alg":"none"}"""));
        var payload = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));
        return $"{header}.{payload}.";
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private sealed class RoutedHandler(string refreshResponse) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var path = request.RequestUri?.AbsolutePath;
            if (path != "/api/v1/auth/refresh")
                throw new InvalidOperationException($"Unexpected request: {request.RequestUri}");

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(refreshResponse, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            throw new InvalidOperationException("Refresh should not be called.");
        }
    }
}

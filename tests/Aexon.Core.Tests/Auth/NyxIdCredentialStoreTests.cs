using System.Text;
using Aexon.Core.Auth;
using Aexon.Core.Tests.Runtime;

namespace Aexon.Core.Tests.Auth;

public sealed class NyxIdCredentialStoreTests
{
    [Fact]
    public void SaveAndLoad_RoundTripsTokensAndPreferences()
    {
        using var temp = new TempDirectory();
        var nyxDir = temp.FullPath(".nyxid");
        var prefsPath = temp.FullPath("preferences.json");
        var store = new NyxIdCredentialStore(nyxDir, prefsPath);

        var expSeconds = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
        var accessToken = BuildJwt($$"""{"exp":{{expSeconds}}}""");

        var credentials = new NyxIdCredentials
        {
            BaseUrl = "https://nyx.example",
            ClientId = NyxIdAuthService.SyntheticClientId,
            AccessToken = accessToken,
            RefreshToken = "refresh-token",
            IdToken = null,
            ExpiresAt = DateTimeOffset.FromUnixTimeSeconds(expSeconds),
            DefaultProvider = "anthropic",
            DefaultModel = "claude-sonnet-4-5",
        };

        store.Save(credentials);

        Assert.True(File.Exists(Path.Combine(nyxDir, "access_token")));
        Assert.True(File.Exists(Path.Combine(nyxDir, "refresh_token")));
        Assert.True(File.Exists(Path.Combine(nyxDir, "base_url")));
        Assert.True(File.Exists(prefsPath));

        // Files must be readable by the nyxid Rust CLI (plain text, no framing).
        Assert.Equal(accessToken, File.ReadAllText(Path.Combine(nyxDir, "access_token")));
        Assert.Equal("refresh-token", File.ReadAllText(Path.Combine(nyxDir, "refresh_token")));
        Assert.Equal("https://nyx.example", File.ReadAllText(Path.Combine(nyxDir, "base_url")));

        var loaded = store.Load();
        Assert.NotNull(loaded);
        Assert.Equal("https://nyx.example", loaded!.BaseUrl);
        Assert.Equal(accessToken, loaded.AccessToken);
        Assert.Equal("refresh-token", loaded.RefreshToken);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(expSeconds), loaded.ExpiresAt);
        Assert.Equal("anthropic", loaded.DefaultProvider);
        Assert.Equal("claude-sonnet-4-5", loaded.DefaultModel);
        Assert.Equal(NyxIdAuthService.SyntheticClientId, loaded.ClientId);
        Assert.Null(loaded.IdToken);
    }

    [Fact]
    public void Save_UsesProtectedUnixPermissions()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var temp = new TempDirectory();
        var nyxDir = temp.FullPath(".nyxid");
        var store = new NyxIdCredentialStore(nyxDir, temp.FullPath("preferences.json"));

        store.Save(new NyxIdCredentials
        {
            BaseUrl = "https://nyx.example",
            ClientId = NyxIdAuthService.SyntheticClientId,
            AccessToken = BuildJwt("""{"sub":"u"}"""),
            RefreshToken = "refresh",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30),
        });

        foreach (var file in new[] { "access_token", "refresh_token", "base_url" })
        {
            var mode = File.GetUnixFileMode(Path.Combine(nyxDir, file));
            Assert.True(mode.HasFlag(UnixFileMode.UserRead), $"{file} user read");
            Assert.True(mode.HasFlag(UnixFileMode.UserWrite), $"{file} user write");
            Assert.False(mode.HasFlag(UnixFileMode.GroupRead), $"{file} group read");
            Assert.False(mode.HasFlag(UnixFileMode.OtherRead), $"{file} other read");
        }
    }

    [Fact]
    public void Load_ReturnsNullWhenAccessTokenMissing()
    {
        using var temp = new TempDirectory();
        var store = new NyxIdCredentialStore(temp.FullPath(".nyxid"), temp.FullPath("preferences.json"));

        Assert.Null(store.Load());
    }

    [Fact]
    public void Load_ReturnsNullWhenBaseUrlMissing()
    {
        using var temp = new TempDirectory();
        var nyxDir = temp.FullPath(".nyxid");
        Directory.CreateDirectory(nyxDir);
        File.WriteAllText(Path.Combine(nyxDir, "access_token"), BuildJwt("""{"sub":"u"}"""));

        var store = new NyxIdCredentialStore(nyxDir, temp.FullPath("preferences.json"));

        Assert.Null(store.Load());
    }

    [Fact]
    public void Clear_RemovesTokenFilesButKeepsPreferences()
    {
        using var temp = new TempDirectory();
        var nyxDir = temp.FullPath(".nyxid");
        var prefsPath = temp.FullPath("preferences.json");
        var store = new NyxIdCredentialStore(nyxDir, prefsPath);

        store.Save(new NyxIdCredentials
        {
            BaseUrl = "https://nyx.example",
            ClientId = NyxIdAuthService.SyntheticClientId,
            AccessToken = BuildJwt("""{"sub":"u"}"""),
            RefreshToken = "refresh",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30),
            DefaultProvider = "anthropic",
        });

        store.Clear();

        Assert.False(File.Exists(Path.Combine(nyxDir, "access_token")));
        Assert.False(File.Exists(Path.Combine(nyxDir, "refresh_token")));
        Assert.False(File.Exists(Path.Combine(nyxDir, "base_url")));
        Assert.True(File.Exists(prefsPath));

        var prefs = store.LoadPreferences();
        Assert.Equal("anthropic", prefs.DefaultProvider);
    }

    [Fact]
    public void Save_DropsRefreshTokenFileWhenCredentialsHaveNone()
    {
        using var temp = new TempDirectory();
        var nyxDir = temp.FullPath(".nyxid");
        var store = new NyxIdCredentialStore(nyxDir, temp.FullPath("preferences.json"));

        store.Save(new NyxIdCredentials
        {
            BaseUrl = "https://nyx.example",
            ClientId = NyxIdAuthService.SyntheticClientId,
            AccessToken = BuildJwt("""{"sub":"u"}"""),
            RefreshToken = "refresh",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30),
        });
        Assert.True(File.Exists(Path.Combine(nyxDir, "refresh_token")));

        store.Save(new NyxIdCredentials
        {
            BaseUrl = "https://nyx.example",
            ClientId = NyxIdAuthService.SyntheticClientId,
            AccessToken = BuildJwt("""{"sub":"u"}"""),
            RefreshToken = null,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30),
        });
        Assert.False(File.Exists(Path.Combine(nyxDir, "refresh_token")));
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
}

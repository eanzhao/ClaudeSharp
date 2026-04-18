using Aexon.Core.Aevatar;
using Aexon.Core.Tests.Runtime;

namespace Aexon.Core.Tests.Aevatar;

public sealed class AevatarChatSettingsStoreTests
{
    [Fact]
    public void LoadReturnsEmptySettingsWhenFileMissing()
    {
        using var temp = new TempDirectory();
        var store = new AevatarChatSettingsStore(temp.FullPath("aevatar.json"));

        var loaded = store.Load();

        Assert.Null(loaded.BaseUrl);
        Assert.Null(loaded.ScopeId);
        Assert.Null(loaded.LastActorId);
    }

    [Fact]
    public void SaveAndLoadRoundTripsAllFields()
    {
        using var temp = new TempDirectory();
        var store = new AevatarChatSettingsStore(temp.FullPath("aevatar.json"));

        store.Save(new AevatarChatSettings
        {
            BaseUrl = "https://custom.aevatar.example/",
            ScopeId = "team-42",
            LastActorId = "nyxid-chat-abc123",
        });

        var loaded = store.Load();

        Assert.Equal("https://custom.aevatar.example", loaded.BaseUrl);
        Assert.Equal("team-42", loaded.ScopeId);
        Assert.Equal("nyxid-chat-abc123", loaded.LastActorId);
    }

    [Fact]
    public void SaveTrimsAndNormalizesValues()
    {
        using var temp = new TempDirectory();
        var store = new AevatarChatSettingsStore(temp.FullPath("aevatar.json"));

        store.Save(new AevatarChatSettings
        {
            BaseUrl = "  https://Example.com:8080/  ",
            ScopeId = "  scope  ",
            LastActorId = "  actor  ",
        });

        var loaded = store.Load();

        Assert.StartsWith("https://example.com:8080", loaded.BaseUrl);
        Assert.Equal("scope", loaded.ScopeId);
        Assert.Equal("actor", loaded.LastActorId);
    }

    [Fact]
    public void SaveRejectsInvalidBaseUrlSilently()
    {
        using var temp = new TempDirectory();
        var store = new AevatarChatSettingsStore(temp.FullPath("aevatar.json"));

        store.Save(new AevatarChatSettings { BaseUrl = "not a url" });

        var loaded = store.Load();
        Assert.Null(loaded.BaseUrl);
    }

    [Fact]
    public void SaveTreatsBlankFieldsAsNull()
    {
        using var temp = new TempDirectory();
        var store = new AevatarChatSettingsStore(temp.FullPath("aevatar.json"));

        store.Save(new AevatarChatSettings
        {
            BaseUrl = "  ",
            ScopeId = "   ",
            LastActorId = "  ",
        });

        var loaded = store.Load();
        Assert.Null(loaded.BaseUrl);
        Assert.Null(loaded.ScopeId);
        Assert.Null(loaded.LastActorId);
    }

    [Fact]
    public void ResolveScopeIdFallsBackToDefault()
    {
        var empty = new AevatarChatSettings();
        Assert.Equal(
            AevatarChatSettingsStore.DefaultScopeId,
            AevatarChatSettingsStore.ResolveScopeId(empty, @override: null));

        var saved = new AevatarChatSettings { ScopeId = "stored" };
        Assert.Equal("stored", AevatarChatSettingsStore.ResolveScopeId(saved, @override: null));

        Assert.Equal(
            "explicit",
            AevatarChatSettingsStore.ResolveScopeId(saved, @override: "explicit"));
    }

    [Fact]
    public void ResolveBaseUrlFallsBackToMainnet()
    {
        var empty = new AevatarChatSettings();
        Assert.Equal(
            AevatarChatSettingsStore.MainnetBaseUrl,
            AevatarChatSettingsStore.ResolveBaseUrl(empty, @override: null));

        var saved = new AevatarChatSettings { BaseUrl = "https://local.example" };
        Assert.Equal(
            "https://local.example",
            AevatarChatSettingsStore.ResolveBaseUrl(saved, @override: null));

        Assert.Equal(
            "https://explicit.example",
            AevatarChatSettingsStore.ResolveBaseUrl(saved, @override: "https://explicit.example"));
    }

    [Fact]
    public void LoadReturnsEmptyWhenFileIsCorrupt()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteFile("aevatar.json", "{ not valid json");
        var store = new AevatarChatSettingsStore(path);

        var loaded = store.Load();

        Assert.Null(loaded.BaseUrl);
        Assert.Null(loaded.ScopeId);
    }
}

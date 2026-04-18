using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aexon.Core.Auth;

/// <summary>
/// Persists NyxID tokens using the same filesystem layout as the upstream
/// <c>nyxid</c> Rust CLI (<c>~/.nyxid/{access_token,refresh_token,base_url}</c>)
/// so one login works for both CLIs. Aexon-specific preferences
/// (default LLM provider/model) live separately in
/// <c>~/.aexon/preferences.json</c> so they don't pollute the nyxid directory.
/// </summary>
public sealed class NyxIdCredentialStore
{
    internal const string AccessTokenFile = "access_token";
    internal const string RefreshTokenFile = "refresh_token";
    internal const string BaseUrlFile = "base_url";

    private static readonly JsonSerializerOptions PreferencesJsonOptions = new()
    {
        WriteIndented = true,
    };

    public NyxIdCredentialStore(string? nyxIdDirectory = null, string? preferencesFilePath = null)
    {
        NyxIdDirectory = string.IsNullOrWhiteSpace(nyxIdDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nyxid")
            : Path.GetFullPath(nyxIdDirectory);

        PreferencesFilePath = string.IsNullOrWhiteSpace(preferencesFilePath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".aexon",
                "preferences.json")
            : Path.GetFullPath(preferencesFilePath);
    }

    public string NyxIdDirectory { get; }

    public string PreferencesFilePath { get; }

    internal string AccessTokenPath => Path.Combine(NyxIdDirectory, AccessTokenFile);

    internal string RefreshTokenPath => Path.Combine(NyxIdDirectory, RefreshTokenFile);

    internal string BaseUrlPath => Path.Combine(NyxIdDirectory, BaseUrlFile);

    public NyxIdCredentials? Load()
    {
        var accessToken = TryReadLine(AccessTokenPath);
        var baseUrl = TryReadLine(BaseUrlPath);
        if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(baseUrl))
            return null;

        var refreshToken = TryReadLine(RefreshTokenPath);
        var expiresAt = ResolveExpiresAt(accessToken);
        var prefs = LoadPreferences();

        return new NyxIdCredentials
        {
            BaseUrl = baseUrl!.TrimEnd('/'),
            AccessToken = accessToken!,
            RefreshToken = string.IsNullOrWhiteSpace(refreshToken) ? null : refreshToken,
            IdToken = null,
            ExpiresAt = expiresAt,
            ClientId = NyxIdAuthService.SyntheticClientId,
            DefaultProvider = prefs.DefaultProvider,
            DefaultModel = prefs.DefaultModel,
        };
    }

    public void Save(NyxIdCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentException.ThrowIfNullOrWhiteSpace(credentials.BaseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(credentials.AccessToken);

        Directory.CreateDirectory(NyxIdDirectory);

        WriteProtectedFile(BaseUrlPath, credentials.BaseUrl.Trim().TrimEnd('/'));
        WriteProtectedFile(AccessTokenPath, credentials.AccessToken.Trim());

        var refreshToken = credentials.RefreshToken?.Trim();
        if (!string.IsNullOrWhiteSpace(refreshToken))
            WriteProtectedFile(RefreshTokenPath, refreshToken);
        else
            DeleteIfExists(RefreshTokenPath);

        SavePreferences(new Preferences(
            credentials.DefaultProvider?.Trim().ToLowerInvariant(),
            credentials.DefaultModel?.Trim()));
    }

    /// <summary>
    /// Clears the <c>~/.nyxid/</c> token files (parity with <c>nyxid logout</c>),
    /// preserving aexon-only preferences so a subsequent login re-uses the last
    /// selected default LLM.
    /// </summary>
    public void Clear()
    {
        DeleteIfExists(AccessTokenPath);
        DeleteIfExists(RefreshTokenPath);
        DeleteIfExists(BaseUrlPath);
    }

    // ── Preferences (aexon-only) ──

    internal Preferences LoadPreferences()
    {
        if (!File.Exists(PreferencesFilePath))
            return Preferences.Empty;

        try
        {
            var json = File.ReadAllText(PreferencesFilePath);
            return JsonSerializer.Deserialize<Preferences>(json, PreferencesJsonOptions) ?? Preferences.Empty;
        }
        catch (IOException)
        {
            return Preferences.Empty;
        }
        catch (JsonException)
        {
            return Preferences.Empty;
        }
    }

    internal void SavePreferences(Preferences prefs)
    {
        if (prefs.IsEmpty && !File.Exists(PreferencesFilePath))
            return;

        var directory = Path.GetDirectoryName(PreferencesFilePath)
                        ?? throw new InvalidOperationException("aexon preferences path has no parent directory.");
        Directory.CreateDirectory(directory);

        var tempPath = $"{PreferencesFilePath}.{Guid.NewGuid():N}.tmp";
        var json = JsonSerializer.Serialize(prefs, PreferencesJsonOptions);
        File.WriteAllText(tempPath, json);
        ApplyUnixPermissions(tempPath);
        File.Move(tempPath, PreferencesFilePath, overwrite: true);
        ApplyUnixPermissions(PreferencesFilePath);
    }

    // ── Helpers ──

    private static string? TryReadLine(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            var content = File.ReadAllText(path).Trim();
            return string.IsNullOrWhiteSpace(content) ? null : content;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static DateTimeOffset ResolveExpiresAt(string accessToken) =>
        NyxIdAuthService.TryReadJwtExpiry(accessToken, out var expiresAt)
            ? expiresAt
            : DateTimeOffset.UtcNow.AddMinutes(30);

    private static void WriteProtectedFile(string path, string content)
    {
        var directory = Path.GetDirectoryName(path)
                        ?? throw new InvalidOperationException("Credential file path has no parent directory.");
        Directory.CreateDirectory(directory);

        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(tempPath, content);
        ApplyUnixPermissions(tempPath);
        File.Move(tempPath, path, overwrite: true);
        ApplyUnixPermissions(path);
    }

    private static void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // best-effort
        }
        catch (UnauthorizedAccessException)
        {
            // best-effort
        }
    }

    private static void ApplyUnixPermissions(string path)
    {
        if (OperatingSystem.IsWindows())
            return;

        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (PlatformNotSupportedException)
        {
        }
    }

    internal sealed record Preferences(
        [property: JsonPropertyName("default_provider")] string? DefaultProvider,
        [property: JsonPropertyName("default_model")] string? DefaultModel)
    {
        public static Preferences Empty { get; } = new(null, null);

        public bool IsEmpty =>
            string.IsNullOrWhiteSpace(DefaultProvider) && string.IsNullOrWhiteSpace(DefaultModel);
    }
}

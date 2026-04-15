namespace Aexon.Cli;

internal sealed record OllamaClientSettings(
    string BaseUrl,
    string? SourceVariable)
{
    public string? StartupSummary =>
        string.IsNullOrWhiteSpace(SourceVariable)
            ? $"Ollama config: using default host {BaseUrl}."
            : $"Ollama config: using host from {SourceVariable}.";
}

internal static class OllamaClientSettingsLoader
{
    private const string DefaultBaseUrl = "http://127.0.0.1:11434";

    public static OllamaClientSettings Load(Func<string, string?>? getEnvironmentVariable = null)
    {
        getEnvironmentVariable ??= Environment.GetEnvironmentVariable;

        var host = Normalize(getEnvironmentVariable("OLLAMA_HOST"));
        if (TryResolveAbsoluteUrl(host, out var ollamaHost))
            return new OllamaClientSettings(ollamaHost, "OLLAMA_HOST");

        var baseUrl = Normalize(getEnvironmentVariable("OLLAMA_BASE_URL"));
        if (TryResolveAbsoluteUrl(baseUrl, out var ollamaBaseUrl))
            return new OllamaClientSettings(ollamaBaseUrl, "OLLAMA_BASE_URL");

        return new OllamaClientSettings(DefaultBaseUrl, null);
    }

    private static bool TryResolveAbsoluteUrl(string? value, out string resolved)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            resolved = uri.ToString().TrimEnd('/');
            return true;
        }

        resolved = DefaultBaseUrl;
        return false;
    }

    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}

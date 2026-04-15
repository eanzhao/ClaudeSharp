namespace Aexon.Cli;

internal sealed record OpenAIClientSettings(
    string? ApiKey,
    string? BaseUrl)
{
    public bool HasUsableConfiguration =>
        !string.IsNullOrWhiteSpace(ApiKey) || !string.IsNullOrWhiteSpace(BaseUrl);

    public string? StartupSummary
    {
        get
        {
            var messages = new List<string>();
            if (!string.IsNullOrWhiteSpace(ApiKey))
                messages.Add("OpenAI config: using API key from OPENAI_API_KEY.");
            if (!string.IsNullOrWhiteSpace(BaseUrl))
                messages.Add("OpenAI config: using base URL from OPENAI_BASE_URL.");

            return messages.Count == 0
                ? null
                : string.Join(Environment.NewLine, messages);
        }
    }
}

internal static class OpenAIClientSettingsLoader
{
    public static OpenAIClientSettings Load(Func<string, string?>? getEnvironmentVariable = null)
    {
        getEnvironmentVariable ??= Environment.GetEnvironmentVariable;

        var apiKey = Normalize(getEnvironmentVariable("OPENAI_API_KEY"));
        var baseUrl = Normalize(getEnvironmentVariable("OPENAI_BASE_URL"));
        if (!string.IsNullOrWhiteSpace(baseUrl) &&
            !Uri.TryCreate(baseUrl, UriKind.Absolute, out _))
        {
            baseUrl = null;
        }

        return new OpenAIClientSettings(apiKey, baseUrl);
    }

    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}

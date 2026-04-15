namespace Aexon.Core.Configuration;

/// <summary>
/// Resolves candidate settings.json files for the current session.
/// </summary>
public static class SettingsFileLocator
{
    public static IReadOnlyList<string> GetCandidatePaths(
        string workingDirectory,
        string? explicitConfigPath = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitConfigPath))
            return [explicitConfigPath];

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return
        [
            Path.Combine(home, ".aexon", "settings.json"),
            Path.Combine(home, ".claude", "settings.json"),
            Path.Combine(workingDirectory, ".aexon", "settings.json"),
            Path.Combine(workingDirectory, ".claude", "settings.json"),
        ];
    }
}

namespace Aexon.Core.Agents;

/// <summary>
/// Parses user-facing auto-resume mode values.
/// </summary>
public static class AgentAutoResumeModeParser
{
    public const string Usage = "queue|latest|disabled";

    public static bool TryParse(string? value, out AgentAutoResumeMode mode)
    {
        mode = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        switch (value.Trim().ToLowerInvariant())
        {
            case "queue":
            case "serial":
            case "oldest":
            case "oldest-first":
                mode = AgentAutoResumeMode.Queue;
                return true;

            case "latest":
            case "newest":
            case "recent":
                mode = AgentAutoResumeMode.Latest;
                return true;

            case "disabled":
            case "off":
            case "manual":
                mode = AgentAutoResumeMode.Disabled;
                return true;

            default:
                return false;
        }
    }
}

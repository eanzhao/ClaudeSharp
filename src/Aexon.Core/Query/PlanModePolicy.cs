using Aexon.Core.Permissions;

namespace Aexon.Core.Query;

/// <summary>
/// Controls plan-mode transitions for the current session.
/// </summary>
public interface IPlanModeController
{
    bool IsPlanModeActive { get; }

    PermissionMode PlanModeResumeMode { get; }

    Task<bool> EnterPlanModeAsync(CancellationToken ct = default);

    Task<PermissionMode> ExitPlanModeAsync(CancellationToken ct = default);
}

/// <summary>
/// Defines the tool policy used while plan mode is active.
/// </summary>
public static class PlanModeToolPolicy
{
    public const string EnterPlanModeToolName = "EnterPlanMode";
    public const string ExitPlanModeToolName = "ExitPlanMode";

    private static readonly string[] ReadToolNamesInternal =
    [
        "Read",
        "Glob",
        "Grep",
        "WebFetch",
    ];

    private static readonly string[] AllowedToolNamesInPlanModeInternal =
    [
        ExitPlanModeToolName,
        .. ReadToolNamesInternal,
    ];

    private static readonly HashSet<string> AllowedToolNamesInPlanModeLookup = new(
        AllowedToolNamesInPlanModeInternal,
        StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> ReadToolNames => ReadToolNamesInternal;

    public static IReadOnlyList<string> AllowedToolNamesInPlanMode =>
        AllowedToolNamesInPlanModeInternal;

    public static bool IsAllowedInPlanMode(string? toolName) =>
        !string.IsNullOrWhiteSpace(toolName) &&
        AllowedToolNamesInPlanModeLookup.Contains(toolName);
}

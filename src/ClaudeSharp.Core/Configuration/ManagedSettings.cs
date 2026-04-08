namespace ClaudeSharp.Core.Configuration;

/// <summary>
/// Describes the source of Anthropic credentials or other runtime auth material.
/// </summary>
public enum AnthropicTokenSourceKind
{
    Unknown = 0,
    EnvironmentVariable,
    AppSettings,
    ManagedSettings,
    Organization,
    Workspace,
    UserLogin,
    ApiKey,
    OAuth,
    Sso,
    Plugin,
}

/// <summary>
/// Represents a single token source in the runtime hierarchy.
/// </summary>
public sealed record AnthropicTokenSourceSnapshot
{
    public required string Id { get; init; }
    public AnthropicTokenSourceKind Kind { get; init; } = AnthropicTokenSourceKind.Unknown;
    public string? DisplayName { get; init; }
    public string? SourcePath { get; init; }
    public string? ParentId { get; init; }
    public bool IsDefault { get; init; }
    public bool IsActive { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Represents the managed org policy that shapes local runtime behavior.
/// </summary>
public sealed record OrganizationPolicySnapshot
{
    public string? OrganizationId { get; init; }
    public string? WorkspaceId { get; init; }
    public bool RequiresManagedAccess { get; init; }
    public bool AllowUserProvidedTokenSources { get; init; } = true;
    public bool AllowWebSearch { get; init; } = true;
    public bool AllowExternalMcpServers { get; init; } = true;
    public bool AllowPlugins { get; init; } = true;
    public IReadOnlyList<string> AllowedProviderKinds { get; init; } = [];

    public static OrganizationPolicySnapshot Empty { get; } = new();
}

/// <summary>
/// Represents the managed settings snapshot loaded from local configuration.
/// </summary>
public sealed record ManagedSettingsSnapshot
{
    public OrganizationPolicySnapshot OrganizationPolicy { get; init; } =
        OrganizationPolicySnapshot.Empty;

    public IReadOnlyList<AnthropicTokenSourceSnapshot> TokenSources { get; init; } = [];
    public string? SourcePath { get; init; }
    public IReadOnlyList<string> Diagnostics { get; init; } = [];

    public static ManagedSettingsSnapshot Empty { get; } = new();
}

/// <summary>
/// Represents the result of loading managed settings.
/// </summary>
public sealed record ManagedSettingsLoadResult(
    ManagedSettingsSnapshot Settings,
    IReadOnlyList<string> Diagnostics,
    IReadOnlyList<string> SourcePaths)
{
    public string? StartupSummary
    {
        get
        {
            var messages = new List<string>(Diagnostics);
            if (SourcePaths.Count > 0)
            {
                messages.Add(
                    $"Managed settings: loaded {Settings.TokenSources.Count} token source(s) from {SourcePaths.Count} file(s).");
            }

            if (Settings.OrganizationPolicy.RequiresManagedAccess)
                messages.Add("Managed settings: organization policy requires managed access.");

            return messages.Count == 0
                ? null
                : string.Join(Environment.NewLine, messages);
        }
    }
}

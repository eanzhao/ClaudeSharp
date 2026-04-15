using System.Text.Json;

namespace Aexon.Core.Configuration;

/// <summary>
/// Loads managed settings from settings.json files.
/// </summary>
public static class ManagedSettingsLoader
{
    public static ManagedSettingsLoadResult Load(
        string workingDirectory,
        string? explicitConfigPath = null) =>
        LoadFromFiles(
            SettingsFileLocator.GetCandidatePaths(workingDirectory, explicitConfigPath),
            workingDirectory);

    public static ManagedSettingsLoadResult LoadFromFiles(
        IEnumerable<string> configPaths,
        string workingDirectory)
    {
        var diagnostics = new List<string>();
        var sources = new List<string>();
        var policy = OrganizationPolicySnapshot.Empty;
        var tokenSources = new Dictionary<string, AnthropicTokenSourceSnapshot>(StringComparer.OrdinalIgnoreCase);
        string? sourcePath = null;

        foreach (var configPath in configPaths
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Select(Path.GetFullPath)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(configPath))
                continue;

            try
            {
                var parsed = ParseFile(configPath, workingDirectory, policy, diagnostics);
                policy = parsed.Policy;

                foreach (var tokenSource in parsed.TokenSources)
                    tokenSources[tokenSource.Id] = tokenSource;

                sourcePath = configPath;
                sources.Add(configPath);
            }
            catch (Exception ex)
            {
                diagnostics.Add($"Managed settings: failed to load {configPath}: {ex.Message}");
            }
        }

        var settings = new ManagedSettingsSnapshot
        {
            OrganizationPolicy = policy,
            TokenSources = tokenSources.Values
                .OrderBy(tokenSource => tokenSource.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            SourcePath = sourcePath,
            Diagnostics = diagnostics.ToArray(),
        };

        return new ManagedSettingsLoadResult(settings, diagnostics, sources);
    }

    private static ParsedManagedSettings ParseFile(
        string configPath,
        string workingDirectory,
        OrganizationPolicySnapshot currentPolicy,
        ICollection<string> diagnostics)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(configPath));
        var root = document.RootElement;

        if (!TryGetManagedSettingsSection(root, out var managedSettings, diagnostics))
            return ParsedManagedSettings.Empty;

        var policy = ParseOrganizationPolicy(
            managedSettings,
            configPath,
            diagnostics,
            currentPolicy);
        var tokenSources = ParseTokenSources(
            managedSettings,
            configPath,
            workingDirectory,
            diagnostics);

        return new ParsedManagedSettings(policy, tokenSources);
    }

    private static bool TryGetManagedSettingsSection(
        JsonElement root,
        out JsonElement managedSettings,
        ICollection<string> diagnostics)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add("Managed settings: settings.json root must be a JSON object.");
            managedSettings = default;
            return false;
        }

        if (TryGetProperty(root, "Aexon", out var claudeSharp) &&
            claudeSharp.ValueKind == JsonValueKind.Object &&
            TryGetProperty(claudeSharp, "managedSettings", out managedSettings))
        {
            if (managedSettings.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add("Managed settings: Aexon.managedSettings must be a JSON object.");
                managedSettings = default;
                return false;
            }

            return true;
        }

        if (TryGetProperty(root, "managedSettings", out managedSettings))
        {
            if (managedSettings.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add("Managed settings: managedSettings must be a JSON object.");
                managedSettings = default;
                return false;
            }

            return true;
        }

        if (TryGetProperty(root, "ManagedSettings", out managedSettings))
        {
            if (managedSettings.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add("Managed settings: ManagedSettings must be a JSON object.");
                managedSettings = default;
                return false;
            }

            return true;
        }

        managedSettings = default;
        return false;
    }

    private static OrganizationPolicySnapshot ParseOrganizationPolicy(
        JsonElement managedSettings,
        string sourcePath,
        ICollection<string> diagnostics,
        OrganizationPolicySnapshot currentPolicy)
    {
        if (!TryGetProperty(managedSettings, "organizationPolicy", out var policyElement) &&
            !TryGetProperty(managedSettings, "organization_policy", out policyElement) &&
            !TryGetProperty(managedSettings, "orgPolicy", out policyElement))
        {
            return currentPolicy;
        }

        if (policyElement.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add($"Managed settings: organization policy in {sourcePath} must be a JSON object.");
            return currentPolicy;
        }

        var policy = currentPolicy;

        policy = policy with
        {
            OrganizationId = ReadStringProperty(policyElement, "organizationId", sourcePath, diagnostics, policy.OrganizationId) ??
                ReadStringProperty(policyElement, "organization_id", sourcePath, diagnostics, policy.OrganizationId) ??
                policy.OrganizationId,
            WorkspaceId = ReadStringProperty(policyElement, "workspaceId", sourcePath, diagnostics, policy.WorkspaceId) ??
                ReadStringProperty(policyElement, "workspace_id", sourcePath, diagnostics, policy.WorkspaceId) ??
                policy.WorkspaceId,
            RequiresManagedAccess = ReadBoolProperty(policyElement, "requiresManagedAccess", sourcePath, diagnostics, policy.RequiresManagedAccess) ??
                ReadBoolProperty(policyElement, "requires_managed_access", sourcePath, diagnostics, policy.RequiresManagedAccess) ??
                policy.RequiresManagedAccess,
            AllowUserProvidedTokenSources = ReadBoolProperty(policyElement, "allowUserProvidedTokenSources", sourcePath, diagnostics, policy.AllowUserProvidedTokenSources) ??
                ReadBoolProperty(policyElement, "allow_user_provided_token_sources", sourcePath, diagnostics, policy.AllowUserProvidedTokenSources) ??
                policy.AllowUserProvidedTokenSources,
            AllowWebSearch = ReadBoolProperty(policyElement, "allowWebSearch", sourcePath, diagnostics, policy.AllowWebSearch) ??
                ReadBoolProperty(policyElement, "allow_web_search", sourcePath, diagnostics, policy.AllowWebSearch) ??
                policy.AllowWebSearch,
            AllowExternalMcpServers = ReadBoolProperty(policyElement, "allowExternalMcpServers", sourcePath, diagnostics, policy.AllowExternalMcpServers) ??
                ReadBoolProperty(policyElement, "allow_external_mcp_servers", sourcePath, diagnostics, policy.AllowExternalMcpServers) ??
                policy.AllowExternalMcpServers,
            AllowPlugins = ReadBoolProperty(policyElement, "allowPlugins", sourcePath, diagnostics, policy.AllowPlugins) ??
                ReadBoolProperty(policyElement, "allow_plugins", sourcePath, diagnostics, policy.AllowPlugins) ??
                policy.AllowPlugins,
            AllowedProviderKinds = ParseStringList(
                policyElement,
                sourcePath,
                diagnostics,
                ["allowedProviderKinds", "allowed_provider_kinds", "allowedProviders", "allowed_providers"],
                "allowed provider kinds",
                policy.AllowedProviderKinds),
        };

        return policy;
    }

    private static IReadOnlyList<AnthropicTokenSourceSnapshot> ParseTokenSources(
        JsonElement managedSettings,
        string sourcePath,
        string workingDirectory,
        ICollection<string> diagnostics)
    {
        if (!TryGetProperty(managedSettings, "tokenSources", out var tokenSourcesElement) &&
            !TryGetProperty(managedSettings, "token_sources", out tokenSourcesElement) &&
            !TryGetProperty(managedSettings, "anthropicTokenSources", out tokenSourcesElement) &&
            !TryGetProperty(managedSettings, "anthropic_token_sources", out tokenSourcesElement))
        {
            return [];
        }

        if (tokenSourcesElement.ValueKind is JsonValueKind.Array)
        {
            var tokenSources = new List<AnthropicTokenSourceSnapshot>();
            foreach (var item in tokenSourcesElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    diagnostics.Add($"Managed settings: ignored non-object token source in {sourcePath}.");
                    continue;
                }

                if (TryParseTokenSource(item, sourcePath, workingDirectory, diagnostics) is { } tokenSource)
                    tokenSources.Add(tokenSource);
            }

            return tokenSources;
        }

        if (tokenSourcesElement.ValueKind is JsonValueKind.Object)
        {
            var tokenSources = new List<AnthropicTokenSourceSnapshot>();
            foreach (var property in tokenSourcesElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Object)
                {
                    diagnostics.Add(
                        $"Managed settings: token source {property.Name} in {sourcePath} must be a JSON object.");
                    continue;
                }

                if (TryParseTokenSource(property.Value, sourcePath, workingDirectory, diagnostics, property.Name) is { } tokenSource)
                    tokenSources.Add(tokenSource);
            }

            return tokenSources;
        }

        diagnostics.Add($"Managed settings: tokenSources in {sourcePath} must be an array or object.");
        return [];
    }

    private static AnthropicTokenSourceSnapshot? TryParseTokenSource(
        JsonElement element,
        string sourcePath,
        string workingDirectory,
        ICollection<string> diagnostics,
        string? fallbackId = null)
    {
        var id = ReadStringValue(element, "id") ??
            ReadStringValue(element, "tokenSourceId") ??
            ReadStringValue(element, "token_source_id") ??
            fallbackId;

        if (string.IsNullOrWhiteSpace(id))
        {
            diagnostics.Add($"Managed settings: ignored token source without an id in {sourcePath}.");
            return null;
        }

        var kind = TryParseTokenSourceKind(
            ReadStringValue(element, "kind") ??
            ReadStringValue(element, "sourceType") ??
            ReadStringValue(element, "source_type"));

        var metadata = ParseMetadata(element);
        var sourceLocation = ReadStringValue(element, "sourcePath") ??
            ReadStringValue(element, "source_path") ??
            ReadStringValue(element, "path");

        if (string.IsNullOrWhiteSpace(sourceLocation))
            sourceLocation = null;
        else if (!Path.IsPathRooted(sourceLocation))
            sourceLocation = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourcePath) ?? workingDirectory, sourceLocation));

        return new AnthropicTokenSourceSnapshot
        {
            Id = id,
            Kind = kind,
            DisplayName = ReadStringValue(element, "displayName") ??
                ReadStringValue(element, "display_name") ??
                ReadStringValue(element, "name"),
            SourcePath = sourceLocation,
            ParentId = ReadStringValue(element, "parentId") ??
                ReadStringValue(element, "parent_id") ??
                ReadStringValue(element, "parent"),
            IsDefault = ReadBoolValue(element, "isDefault") ??
                ReadBoolValue(element, "is_default") ??
                false,
            IsActive = ReadBoolValue(element, "isActive") ??
                ReadBoolValue(element, "is_active") ??
                false,
            Metadata = metadata,
        };
    }

    private static IReadOnlyDictionary<string, string> ParseMetadata(JsonElement element)
    {
        if (!TryGetProperty(element, "metadata", out var metadataElement) ||
            metadataElement.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in metadataElement.EnumerateObject())
        {
            metadata[property.Name] = property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString() ?? string.Empty
                : property.Value.GetRawText();
        }

        return metadata;
    }

    private static IReadOnlyList<string> ParseStringList(
        JsonElement element,
        string sourcePath,
        ICollection<string> diagnostics,
        IReadOnlyList<string> propertyNames,
        string label,
        IReadOnlyList<string> currentValue)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!TryGetProperty(element, propertyName, out var listElement))
                continue;

            if (listElement.ValueKind != JsonValueKind.Array)
            {
                diagnostics.Add($"Managed settings: {label} in {sourcePath} must be an array of strings.");
                return currentValue;
            }

            var values = new List<string>();
            foreach (var item in listElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var value = item.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                        values.Add(value);
                }
            }

            return values;
        }

        return currentValue;
    }

    private static AnthropicTokenSourceKind TryParseTokenSourceKind(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return AnthropicTokenSourceKind.Unknown;

        var normalized = Normalize(value);
        return normalized switch
        {
            "environmentvariable" => AnthropicTokenSourceKind.EnvironmentVariable,
            "appsettings" => AnthropicTokenSourceKind.AppSettings,
            "managedsettings" => AnthropicTokenSourceKind.ManagedSettings,
            "organization" => AnthropicTokenSourceKind.Organization,
            "workspace" => AnthropicTokenSourceKind.Workspace,
            "userlogin" => AnthropicTokenSourceKind.UserLogin,
            "apikey" => AnthropicTokenSourceKind.ApiKey,
            "oauth" => AnthropicTokenSourceKind.OAuth,
            "sso" => AnthropicTokenSourceKind.Sso,
            "plugin" => AnthropicTokenSourceKind.Plugin,
            _ => AnthropicTokenSourceKind.Unknown,
        };
    }

    private static string Normalize(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string? ReadStringProperty(
        JsonElement element,
        string propertyName,
        string sourcePath,
        ICollection<string> diagnostics,
        string? currentValue)
    {
        if (!TryGetProperty(element, propertyName, out var property))
            return currentValue;

        if (property.ValueKind != JsonValueKind.String)
        {
            diagnostics.Add($"Managed settings: {propertyName} in {sourcePath} must be a string.");
            return currentValue;
        }

        var value = property.GetString()?.Trim();
        return string.IsNullOrWhiteSpace(value) ? currentValue : value;
    }

    private static string? ReadStringValue(
        JsonElement element,
        string propertyName) =>
        TryGetProperty(element, propertyName, out var property) &&
        property.ValueKind == JsonValueKind.String
            ? property.GetString()?.Trim()
            : null;

    private static bool? ReadBoolProperty(
        JsonElement element,
        string propertyName,
        string sourcePath,
        ICollection<string> diagnostics,
        bool currentValue)
    {
        if (!TryGetProperty(element, propertyName, out var property))
            return null;

        if (property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            diagnostics.Add($"Managed settings: {propertyName} in {sourcePath} must be a boolean.");
            return currentValue;
        }

        return property.GetBoolean();
    }

    private static bool? ReadBoolValue(JsonElement element, string propertyName) =>
        TryGetProperty(element, propertyName, out var property) &&
        property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean()
            : null;

    private static bool TryGetProperty(
        JsonElement element,
        string propertyName,
        out JsonElement property)
    {
        foreach (var candidate in element.EnumerateObject())
        {
            if (string.Equals(candidate.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                property = candidate.Value;
                return true;
            }
        }

        property = default;
        return false;
    }

    private sealed record ParsedManagedSettings(
        OrganizationPolicySnapshot Policy,
        IReadOnlyList<AnthropicTokenSourceSnapshot> TokenSources)
    {
        public static ParsedManagedSettings Empty { get; } =
            new(OrganizationPolicySnapshot.Empty, []);
    }
}

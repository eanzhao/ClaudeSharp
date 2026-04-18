using System.Diagnostics.CodeAnalysis;

namespace Aexon.Core.Auth;

/// <summary>
/// Shared interactive picker for the default NyxID-brokered LLM. Merges
/// two NyxID sources into a single pick list:
///
///   • <c>GET /api/v1/llm/status</c> — gateway-native providers (anthropic,
///     openai, ...) that route through <c>/api/v1/llm/{slug}/v1/</c>.
///   • <c>GET /api/v1/keys</c> — user-scoped AI Services (Chrono LLM, Mimo,
///     ...) that route through <c>/api/v1/proxy/s/{slug}/v1/</c>. Each is
///     probed with <c>GET …/v1/models</c> so we only surface ones that
///     actually speak OpenAI-compatible chat.
///
/// The <c>/llm</c> command and the CLI's first-run onboarding both drive
/// this helper so the two paths produce identical behavior.
/// </summary>
public static class NyxIdProviderPicker
{
    private static readonly string[] SupportedProviderSlugs = ["anthropic", "openai"];

    public static IReadOnlyList<string> SupportedProviders => SupportedProviderSlugs;

    public static bool IsSupportedProviderSlug(string providerSlug) =>
        SupportedProviderSlugs.Contains(providerSlug, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Fetches both sources of LLM availability from NyxID, prompts the
    /// user to pick a provider + model, and persists the selection.
    /// Requires a TTY — callers gate on <c>Console.IsInputRedirected</c>.
    /// Returns the updated credentials on success, or null if the user
    /// aborted or NyxID has no usable provider.
    /// </summary>
    [ExcludeFromCodeCoverage]
    public static async Task<NyxIdCredentials?> RunAsync(
        NyxIdCredentialStore credentialStore,
        NyxIdLlmStatusClient statusClient,
        NyxIdKeysClient keysClient,
        NyxIdCredentials credentials,
        Action<string> writeLine,
        IProviderPickerUi? ui = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentialStore);
        ArgumentNullException.ThrowIfNull(statusClient);
        ArgumentNullException.ThrowIfNull(keysClient);
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(writeLine);
        ui ??= ConsoleLineProviderPickerUi.Instance;

        var status = await TryFetchStatusAsync(statusClient, credentials.BaseUrl, writeLine, cancellationToken);
        if (status == null)
            return null;

        writeLine("  Discovering NyxID AI Services…");
        var proxyEntries = await DiscoverProxyServicesAsync(
            keysClient,
            credentials.BaseUrl,
            writeLine,
            cancellationToken);

        var gatewayEntries = status.Providers
            .Where(p => IsSupportedProviderSlug(p.ProviderSlug))
            .Select(p => new PickEntry(
                Kind: PickEntryKind.Gateway,
                DisplaySlug: p.ProviderSlug,
                DisplayName: p.ProviderName,
                Status: p.Status,
                IsReady: p.IsReady,
                Gateway: p,
                ProxyInfo: null,
                ProbedModels: []))
            .ToList();

        if (gatewayEntries.Count + proxyEntries.Count == 0)
        {
            writeLine("  NyxID has no LLM-capable providers reachable for this user.");
            writeLine("  Connect a gateway credential (anthropic / openai) or add an AI Service first.");
            return null;
        }

        PrintStatus(status, credentials, writeLine);
        if (proxyEntries.Count > 0)
        {
            writeLine(string.Empty);
            writeLine($"  AI Services on {credentials.BaseUrl} ({proxyEntries.Count} LLM-capable):");
            for (var i = 0; i < proxyEntries.Count; i++)
            {
                var e = proxyEntries[i];
                var marker = IsCurrentDefaultProxy(e.DisplaySlug, credentials) ? " (default)" : string.Empty;
                var modelCount = e.ProbedModels.Count;
                writeLine(
                    $"    P{i + 1,2}. {e.DisplaySlug,-20} [{e.Status}]{marker}  {e.DisplayName} — {modelCount} model(s)");
            }
        }
        writeLine(string.Empty);

        var entries = BuildPickList(gatewayEntries, proxyEntries);
        var currentDefaultSlug = !string.IsNullOrWhiteSpace(credentials.DefaultProxySlug)
            ? credentials.DefaultProxySlug
            : credentials.DefaultProvider;
        var picked = ui.PromptForEntry(entries, currentDefaultSlug, writeLine);
        if (picked == null)
            return null;

        if (!picked.IsReady)
        {
            writeLine($"  '{picked.DisplaySlug}' is '{picked.Status}' on NyxID.");
            writeLine("  Connect / activate the credential in the NyxID UI before selecting it.");
            return null;
        }

        if (picked.Kind == PickEntryKind.Gateway)
        {
            var model = ui.PromptForModel(
                picked.DisplaySlug,
                availableModels: [],
                credentials.DefaultModel,
                writeLine);
            return SaveDefaultGatewayProvider(
                credentialStore,
                credentials,
                picked.DisplaySlug.ToLowerInvariant(),
                model,
                writeLine);
        }

        var pickedModel = ui.PromptForModel(
            picked.DisplayName,
            picked.ProbedModels,
            credentials.DefaultModel,
            writeLine);
        if (pickedModel == null)
            return null;

        return SaveDefaultProxyService(
            credentialStore,
            credentials,
            picked.ProxyInfo!.Slug,
            picked.ProxyInfo.Label,
            pickedModel,
            writeLine);
    }

    [ExcludeFromCodeCoverage]
    public static async Task<NyxIdLlmStatus?> TryFetchStatusAsync(
        NyxIdLlmStatusClient statusClient,
        string baseUrl,
        Action<string> writeLine,
        CancellationToken cancellationToken)
    {
        try
        {
            return await statusClient.GetStatusAsync(baseUrl, cancellationToken);
        }
        catch (NotLoggedInException ex)
        {
            writeLine($"  {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            writeLine($"  Failed to query NyxID /api/v1/llm/status: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Enumerates the caller's AI Services via <c>GET /api/v1/keys</c> and
    /// probes each active HTTP service for an OpenAI-compatible
    /// <c>/v1/models</c> response. Probes run in parallel so the picker
    /// doesn't stall on the slowest service. Services that aren't LLM-
    /// shaped (or are inactive / SSH / 4xx on the probe) are filtered out.
    /// </summary>
    [ExcludeFromCodeCoverage]
    public static async Task<IReadOnlyList<PickEntry>> DiscoverProxyServicesAsync(
        NyxIdKeysClient keysClient,
        string nyxIdBaseUrl,
        Action<string> writeLine,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<NyxIdAiServiceInfo> services;
        try
        {
            services = await keysClient.ListAsync(nyxIdBaseUrl, cancellationToken);
        }
        catch (NotLoggedInException ex)
        {
            writeLine($"  {ex.Message}");
            return [];
        }
        catch (Exception ex)
        {
            writeLine($"  Failed to list NyxID AI Services: {ex.Message}");
            return [];
        }

        var candidates = services
            .Where(s => s.IsReady && s.IsHttpService && !string.IsNullOrWhiteSpace(s.Slug))
            .ToList();

        if (candidates.Count == 0)
            return [];

        var probes = candidates.Select(async info =>
        {
            var models = await keysClient.TryProbeModelsAsync(nyxIdBaseUrl, info.Slug, cancellationToken);
            return new { Info = info, Models = models };
        });

        var results = await Task.WhenAll(probes);
        return results
            .Where(r => r.Models is { Count: > 0 })
            .Select(r => new PickEntry(
                Kind: PickEntryKind.Proxy,
                DisplaySlug: r.Info.Slug,
                DisplayName: string.IsNullOrWhiteSpace(r.Info.Label) ? r.Info.Slug : r.Info.Label,
                Status: r.Info.Status,
                IsReady: r.Info.IsReady,
                Gateway: null,
                ProxyInfo: r.Info,
                ProbedModels: r.Models!))
            .ToList();
    }

    public static void PrintStatus(
        NyxIdLlmStatus status,
        NyxIdCredentials credentials,
        Action<string> writeLine)
    {
        writeLine($"  Gateway: {status.GatewayUrl}");
        writeLine($"  Gateway providers on {credentials.BaseUrl}:");
        var index = 1;
        foreach (var provider in status.Providers)
        {
            var marker = string.Equals(
                provider.ProviderSlug,
                credentials.DefaultProvider,
                StringComparison.OrdinalIgnoreCase)
                ? " (default)"
                : string.Empty;
            writeLine(
                $"    G{index,2}. {provider.ProviderSlug,-14} [{provider.Status}]{marker}  {provider.ProviderName}");
            index++;
        }

        if (!status.Providers.Any(p => p.IsReady))
        {
            writeLine("  No gateway provider is 'ready' — falling back to AI Services (if any).");
        }
    }

    public static NyxIdCredentials SaveDefaultGatewayProvider(
        NyxIdCredentialStore credentialStore,
        NyxIdCredentials credentials,
        string providerSlug,
        string? model,
        Action<string> writeLine)
    {
        ArgumentNullException.ThrowIfNull(credentialStore);
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerSlug);
        ArgumentNullException.ThrowIfNull(writeLine);

        var updated = credentials with
        {
            DefaultProvider = providerSlug,
            DefaultModel = string.IsNullOrWhiteSpace(model) ? null : model.Trim(),
            // Gateway and proxy defaults are mutually exclusive.
            DefaultProxySlug = null,
            DefaultProxyLabel = null,
        };
        credentialStore.Save(updated);

        var suffix = string.IsNullOrWhiteSpace(updated.DefaultModel)
            ? string.Empty
            : $" with default model {updated.DefaultModel}";
        writeLine($"  Default LLM set to gateway provider '{providerSlug}'{suffix}.");
        writeLine("  Restart the session to pick it up.");
        return updated;
    }

    public static NyxIdCredentials SaveDefaultProxyService(
        NyxIdCredentialStore credentialStore,
        NyxIdCredentials credentials,
        string proxySlug,
        string? proxyLabel,
        string model,
        Action<string> writeLine)
    {
        ArgumentNullException.ThrowIfNull(credentialStore);
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentException.ThrowIfNullOrWhiteSpace(proxySlug);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(writeLine);

        var updated = credentials with
        {
            DefaultProvider = null,
            DefaultModel = model.Trim(),
            DefaultProxySlug = proxySlug.Trim(),
            DefaultProxyLabel = string.IsNullOrWhiteSpace(proxyLabel) ? null : proxyLabel.Trim(),
        };
        credentialStore.Save(updated);

        var displayName = string.IsNullOrWhiteSpace(updated.DefaultProxyLabel)
            ? updated.DefaultProxySlug!
            : $"{updated.DefaultProxyLabel} ({updated.DefaultProxySlug})";
        writeLine(
            $"  Default LLM set to AI Service '{displayName}' with model {updated.DefaultModel}.");
        writeLine("  Restart the session to pick it up.");
        return updated;
    }

    private static bool IsCurrentDefaultProxy(string slug, NyxIdCredentials credentials) =>
        !string.IsNullOrWhiteSpace(credentials.DefaultProxySlug) &&
        string.Equals(credentials.DefaultProxySlug, slug, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<PickEntry> BuildPickList(
        IReadOnlyList<PickEntry> gatewayEntries,
        IReadOnlyList<PickEntry> proxyEntries) =>
        [.. gatewayEntries, .. proxyEntries];

    public enum PickEntryKind
    {
        Gateway,
        Proxy,
    }

    public sealed record PickEntry(
        PickEntryKind Kind,
        string DisplaySlug,
        string DisplayName,
        string Status,
        bool IsReady,
        NyxIdLlmProviderStatus? Gateway,
        NyxIdAiServiceInfo? ProxyInfo,
        IReadOnlyList<string> ProbedModels);
}

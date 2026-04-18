using System.Diagnostics.CodeAnalysis;
using Aexon.Core.Auth;
using Spectre.Console;

namespace Aexon.Commands;

/// <summary>
/// Spectre.Console-backed <see cref="IProviderPickerUi"/>: arrow keys to
/// navigate, Enter to select. Used by <c>/llm</c> and the first-run
/// onboarding when the terminal can render rich prompts; callers fall
/// back to <see cref="ConsoleLineProviderPickerUi"/> when input is
/// redirected or the terminal lacks interactive capability.
/// </summary>
/// <remarks>
/// Excluded from coverage — every method drives a Spectre prompt which
/// requires a real TTY. Behavioral correctness is verified by running
/// <c>aexon llm</c> against mainnet, and the underlying selection logic
/// (slug matching, fall-back-to-custom-id) is covered by the shared
/// <c>ConsoleLineProviderPickerUi</c> path.
/// </remarks>
[ExcludeFromCodeCoverage]
public sealed class SpectreProviderPickerUi : IProviderPickerUi
{
    /// <summary>Sentinel returned by the model prompt when the user asks to type a custom id.</summary>
    private const string CustomModelSentinel = "__aexon::custom-model__";

    public NyxIdProviderPicker.PickEntry? PromptForEntry(
        IReadOnlyList<NyxIdProviderPicker.PickEntry> entries,
        string? currentDefaultSlug,
        Action<string> writeLine)
    {
        if (entries.Count == 0)
            return null;

        var ordered = OrderEntriesWithDefaultFirst(entries, currentDefaultSlug);

        var prompt = new SelectionPrompt<NyxIdProviderPicker.PickEntry>()
            .Title(
                string.IsNullOrWhiteSpace(currentDefaultSlug)
                    ? "[bold]Pick a provider[/]  [grey](↑/↓ to move, Enter to select)[/]"
                    : $"[bold]Pick a provider[/]  [grey](↑/↓ to move, Enter to select; current default: [yellow]{Markup.Escape(currentDefaultSlug)}[/])[/]")
            .PageSize(Math.Min(20, Math.Max(5, entries.Count + 2)))
            .MoreChoicesText("[grey](↑/↓ to scroll)[/]")
            .UseConverter(e => FormatEntry(e, currentDefaultSlug))
            .AddChoices(ordered);

        try
        {
            return AnsiConsole.Prompt(prompt);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            // Terminal can't render interactively after all — fall back so
            // the user still has a way through.
            writeLine($"  (interactive picker unavailable: {ex.Message}; falling back to text UI)");
            return ConsoleLineProviderPickerUi.Instance.PromptForEntry(
                entries,
                currentDefaultSlug,
                writeLine);
        }
    }

    public string? PromptForModel(
        string serviceDisplayName,
        IReadOnlyList<string> availableModels,
        string? currentDefault,
        Action<string> writeLine)
    {
        if (availableModels.Count == 0)
            return PromptForCustomModelId(serviceDisplayName, currentDefault);

        // Put the current default first so Enter keeps it, and tack on a
        // sentinel row that lets the user type something off-list (probe
        // output can be stale or incomplete).
        var ordered = new List<string>(availableModels.Count + 1);
        if (!string.IsNullOrWhiteSpace(currentDefault) &&
            availableModels.Contains(currentDefault, StringComparer.OrdinalIgnoreCase))
        {
            ordered.Add(availableModels.First(m =>
                string.Equals(m, currentDefault, StringComparison.OrdinalIgnoreCase)));
            ordered.AddRange(availableModels.Where(m =>
                !string.Equals(m, currentDefault, StringComparison.OrdinalIgnoreCase)));
        }
        else
        {
            ordered.AddRange(availableModels);
        }
        ordered.Add(CustomModelSentinel);

        var prompt = new SelectionPrompt<string>()
            .Title(
                $"[bold]Pick a model for [cyan]{Markup.Escape(serviceDisplayName)}[/][/]  [grey](↑/↓ to move, Enter to select)[/]")
            .PageSize(Math.Min(20, Math.Max(5, ordered.Count + 1)))
            .MoreChoicesText("[grey](↑/↓ to scroll)[/]")
            .UseConverter(m => FormatModel(m, currentDefault))
            .AddChoices(ordered);

        string picked;
        try
        {
            picked = AnsiConsole.Prompt(prompt);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            writeLine($"  (interactive picker unavailable: {ex.Message}; falling back to text UI)");
            return ConsoleLineProviderPickerUi.Instance.PromptForModel(
                serviceDisplayName,
                availableModels,
                currentDefault,
                writeLine);
        }

        return picked == CustomModelSentinel
            ? PromptForCustomModelId(serviceDisplayName, currentDefault)
            : picked;
    }

    private static string? PromptForCustomModelId(string serviceDisplayName, string? currentDefault)
    {
        var label = string.IsNullOrWhiteSpace(currentDefault)
            ? $"Model id for [cyan]{Markup.Escape(serviceDisplayName)}[/]"
            : $"Model id for [cyan]{Markup.Escape(serviceDisplayName)}[/] [grey](Enter to keep [yellow]{Markup.Escape(currentDefault)}[/])[/]";
        var textPrompt = new TextPrompt<string>(label)
            .AllowEmpty();
        if (!string.IsNullOrWhiteSpace(currentDefault))
            textPrompt.DefaultValue(currentDefault);

        var raw = AnsiConsole.Prompt(textPrompt)?.Trim();
        return string.IsNullOrWhiteSpace(raw) ? currentDefault : raw;
    }

    private static List<NyxIdProviderPicker.PickEntry> OrderEntriesWithDefaultFirst(
        IReadOnlyList<NyxIdProviderPicker.PickEntry> entries,
        string? currentDefaultSlug)
    {
        if (string.IsNullOrWhiteSpace(currentDefaultSlug))
            return entries.ToList();

        var match = entries.FirstOrDefault(e =>
            string.Equals(e.DisplaySlug, currentDefaultSlug, StringComparison.OrdinalIgnoreCase));
        if (match == null)
            return entries.ToList();

        var ordered = new List<NyxIdProviderPicker.PickEntry> { match };
        ordered.AddRange(entries.Where(e => !ReferenceEquals(e, match)));
        return ordered;
    }

    private static string FormatEntry(NyxIdProviderPicker.PickEntry entry, string? currentDefaultSlug)
    {
        var kind = entry.Kind == NyxIdProviderPicker.PickEntryKind.Gateway
            ? "[grey]G[/]"
            : "[grey]P[/]";
        var status = FormatStatusTag(entry.Status, entry.IsReady);
        var isDefault = !string.IsNullOrWhiteSpace(currentDefaultSlug) &&
                        string.Equals(entry.DisplaySlug, currentDefaultSlug, StringComparison.OrdinalIgnoreCase)
            ? "  [yellow](current default)[/]"
            : string.Empty;
        var modelHint = entry.Kind == NyxIdProviderPicker.PickEntryKind.Proxy && entry.ProbedModels.Count > 0
            ? $"  [grey]— {entry.ProbedModels.Count} model(s)[/]"
            : string.Empty;
        var slug = entry.IsReady ? Markup.Escape(entry.DisplaySlug) : $"[strikethrough]{Markup.Escape(entry.DisplaySlug)}[/]";
        return $"{kind} {slug,-20}  {status}  {Markup.Escape(entry.DisplayName)}{modelHint}{isDefault}";
    }

    private static string FormatStatusTag(string status, bool isReady)
    {
        var text = Markup.Escape(string.IsNullOrWhiteSpace(status) ? "unknown" : status);
        if (isReady)
            return $"[green]{text}[/]";
        if (string.Equals(status, "expired", StringComparison.OrdinalIgnoreCase))
            return $"[red]{text}[/]";
        return $"[grey]{text}[/]";
    }

    private static string FormatModel(string model, string? currentDefault)
    {
        if (model == CustomModelSentinel)
            return "[grey italic]<type a custom model id>[/]";

        var isDefault = !string.IsNullOrWhiteSpace(currentDefault) &&
                        string.Equals(model, currentDefault, StringComparison.OrdinalIgnoreCase);
        return isDefault
            ? $"{Markup.Escape(model)}  [yellow](current default)[/]"
            : Markup.Escape(model);
    }
}

using System.Diagnostics.CodeAnalysis;

namespace Aexon.Core.Auth;

/// <summary>
/// Default <see cref="IProviderPickerUi"/> for environments that cannot
/// render rich TUI widgets — numbered list with the existing G1/P1
/// prefixes for the provider step, numbered list + free-text fallback
/// for the model step. Kept dependency-free so <c>Aexon.Core</c> stays
/// decoupled from Spectre.Console; callers with a real terminal should
/// prefer the Spectre implementation in <c>Aexon.Commands</c>.
/// </summary>
/// <remarks>
/// Excluded from coverage — every method drives <see cref="Console.Write"/>
/// and <see cref="Console.ReadLine"/>. Stubbing those in xunit is brittle
/// (the tests would race against other tests that share the global
/// Console state); behavioral correctness is verified by running
/// <c>aexon llm</c> against a non-TTY stdin and confirming the line
/// UI is rendered.
/// </remarks>
[ExcludeFromCodeCoverage]
public sealed class ConsoleLineProviderPickerUi : IProviderPickerUi
{
    public static IProviderPickerUi Instance { get; } = new ConsoleLineProviderPickerUi();

    public NyxIdProviderPicker.PickEntry? PromptForEntry(
        IReadOnlyList<NyxIdProviderPicker.PickEntry> entries,
        string? currentDefaultSlug,
        Action<string> writeLine)
    {
        while (true)
        {
            var defaultHint = string.IsNullOrWhiteSpace(currentDefaultSlug)
                ? string.Empty
                : $" [Enter to keep {currentDefaultSlug}]";
            Console.Write(
                $"  Pick provider (slug, or G<n>/P<n> index from the tables above){defaultHint}: ");
            var raw = Console.ReadLine();
            if (raw == null)
            {
                writeLine("  Input closed. Aborting.");
                return null;
            }

            var trimmed = raw.Trim();
            if (trimmed.Length == 0)
            {
                if (string.IsNullOrWhiteSpace(currentDefaultSlug))
                {
                    writeLine("  Please enter a slug or an index like G1 / P2.");
                    continue;
                }

                var keep = entries.FirstOrDefault(e =>
                    string.Equals(e.DisplaySlug, currentDefaultSlug, StringComparison.OrdinalIgnoreCase));
                if (keep != null)
                    return keep;

                writeLine($"  Stored default '{currentDefaultSlug}' is no longer listed. Pick again.");
                continue;
            }

            if (TryResolveIndex(trimmed, entries, out var byIndex))
                return byIndex;

            var bySlug = entries.FirstOrDefault(e =>
                string.Equals(e.DisplaySlug, trimmed, StringComparison.OrdinalIgnoreCase));
            if (bySlug != null)
                return bySlug;

            writeLine($"  '{trimmed}' is not in the list. Try again or Ctrl+C to cancel.");
        }
    }

    public string? PromptForModel(
        string serviceDisplayName,
        IReadOnlyList<string> availableModels,
        string? currentDefault,
        Action<string> writeLine)
    {
        if (availableModels.Count == 0)
            return PromptForModelFreeText(serviceDisplayName, currentDefault, writeLine);

        writeLine($"  Models available on '{serviceDisplayName}':");
        for (var i = 0; i < availableModels.Count; i++)
        {
            var marker = string.Equals(availableModels[i], currentDefault, StringComparison.OrdinalIgnoreCase)
                ? " (default)"
                : string.Empty;
            writeLine($"    {i + 1,2}. {availableModels[i]}{marker}");
        }

        while (true)
        {
            var defaultHint = string.IsNullOrWhiteSpace(currentDefault)
                ? string.Empty
                : $" [Enter to keep {currentDefault}]";
            Console.Write($"  Pick model by number, exact id, or type a custom id{defaultHint}: ");
            var raw = Console.ReadLine();
            if (raw == null)
            {
                writeLine("  Input closed. Aborting.");
                return null;
            }

            var trimmed = raw.Trim();
            if (trimmed.Length == 0)
            {
                if (!string.IsNullOrWhiteSpace(currentDefault))
                    return currentDefault;
                writeLine("  Please pick a model.");
                continue;
            }

            if (int.TryParse(trimmed, out var n) && n >= 1 && n <= availableModels.Count)
                return availableModels[n - 1];

            var exactMatch = availableModels.FirstOrDefault(m =>
                string.Equals(m, trimmed, StringComparison.OrdinalIgnoreCase));
            if (exactMatch != null)
                return exactMatch;

            // Free-form id — accept as-is so the user isn't blocked by a
            // stale or partial /v1/models response.
            return trimmed;
        }
    }

    private static string? PromptForModelFreeText(
        string serviceDisplayName,
        string? currentDefault,
        Action<string> writeLine)
    {
        var hint = string.IsNullOrWhiteSpace(currentDefault)
            ? $" (or press Enter for the Aexon default for {serviceDisplayName})"
            : $" [Enter to keep {currentDefault}]";
        Console.Write($"  Default model{hint}: ");
        var raw = Console.ReadLine();
        if (raw == null)
        {
            writeLine("  Input closed. Aborting.");
            return null;
        }

        var trimmed = raw.Trim();
        if (trimmed.Length == 0)
            return string.IsNullOrWhiteSpace(currentDefault) ? null : currentDefault;

        return trimmed;
    }

    private static bool TryResolveIndex(
        string input,
        IReadOnlyList<NyxIdProviderPicker.PickEntry> entries,
        out NyxIdProviderPicker.PickEntry? matched)
    {
        matched = null;
        if (input.Length < 2)
            return false;

        var prefix = char.ToUpperInvariant(input[0]);
        if (prefix != 'G' && prefix != 'P')
            return false;

        if (!int.TryParse(input[1..], out var n) || n < 1)
            return false;

        var targetKind = prefix == 'G'
            ? NyxIdProviderPicker.PickEntryKind.Gateway
            : NyxIdProviderPicker.PickEntryKind.Proxy;
        var filtered = entries.Where(e => e.Kind == targetKind).ToList();
        if (n > filtered.Count)
            return false;

        matched = filtered[n - 1];
        return true;
    }
}

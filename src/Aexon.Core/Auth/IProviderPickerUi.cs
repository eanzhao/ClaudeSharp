namespace Aexon.Core.Auth;

/// <summary>
/// Strategy that captures the two user-facing decisions inside
/// <see cref="NyxIdProviderPicker.RunAsync"/> — "which provider" and
/// "which model" — so the same core discovery + persistence logic can
/// drive different terminal UIs (plain numbered prompts, Spectre arrow
/// keys, a future TUI, …). Implementations own their own I/O; the
/// picker supplies the merged list and a <paramref name="writeLine"/>
/// hook for auxiliary messages.
/// </summary>
public interface IProviderPickerUi
{
    /// <summary>
    /// Asks the user to pick a single entry from the merged gateway +
    /// AI-service list. <paramref name="currentDefaultSlug"/> is either
    /// a gateway provider slug or a proxy service slug — both are drawn
    /// from the same namespace in the user's stored credentials and
    /// implementations should offer it as the "keep current" default.
    /// Return null when the user aborts (EOF / Esc / Ctrl+C).
    /// </summary>
    NyxIdProviderPicker.PickEntry? PromptForEntry(
        IReadOnlyList<NyxIdProviderPicker.PickEntry> entries,
        string? currentDefaultSlug,
        Action<string> writeLine);

    /// <summary>
    /// Asks the user to pick a model id. When <paramref name="availableModels"/>
    /// is non-empty the implementation should offer those as first-class
    /// choices but still allow a free-text override (the probe list can
    /// be stale). When empty, a free-text prompt is the only option.
    /// Returns null on abort.
    /// </summary>
    string? PromptForModel(
        string serviceDisplayName,
        IReadOnlyList<string> availableModels,
        string? currentDefault,
        Action<string> writeLine);
}

using Aexon.Core.Commands;
using Aexon.Core.Hooks;
using Aexon.Core.Tools;

namespace Aexon.Core.Extensions;

/// <summary>
/// Configuration surface handed to <see cref="IAexonExtension"/> during
/// startup. All registrations are additive; attempts to register a tool or
/// command whose name is already taken throw <see cref="InvalidOperationException"/>.
/// </summary>
public interface IAexonSessionBuilder
{
    /// <summary>Resolved working directory for this session.</summary>
    string WorkingDirectory { get; }

    /// <summary>
    /// Model identifier the session will start with (e.g. "claude-sonnet-4-6").
    /// Extensions may inspect this to decide which tools or prompts to register.
    /// </summary>
    string Model { get; }

    /// <summary>Registers a tool with the session's <see cref="ToolRegistry"/>.</summary>
    void RegisterTool(ITool tool);

    /// <summary>Registers a slash command with the session's <see cref="CommandRegistry"/>.</summary>
    void RegisterCommand(ICommand command);

    /// <summary>Registers a hook observer with the session's <see cref="HookRuntime"/>.</summary>
    void RegisterHookObserver(HookObserver observer);

    /// <summary>
    /// Appends a fragment to the system prompt. Fragments are concatenated
    /// in registration order after the built-in system prompt.
    /// </summary>
    void AppendSystemPromptFragment(string fragment);
}

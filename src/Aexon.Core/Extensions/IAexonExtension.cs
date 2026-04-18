namespace Aexon.Core.Extensions;

/// <summary>
/// A pre-session extension that can register tools, commands, hook observers,
/// and system-prompt fragments during CLI startup. The lifecycle is one-shot:
/// <see cref="ConfigureAsync"/> runs exactly once before the conversation loop
/// starts. Extensions do not receive a handle to the running session and
/// cannot hook into the runtime after startup.
/// </summary>
public interface IAexonExtension
{
    /// <summary>
    /// Stable identifier shown in diagnostics and logs.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Configures the session being built. The <paramref name="builder"/>
    /// surface is discarded after every extension has run; do not retain
    /// references to it or to objects obtained from it.
    /// </summary>
    Task ConfigureAsync(IAexonSessionBuilder builder, CancellationToken cancellationToken);
}

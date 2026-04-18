namespace Aexon.Core.Extensions;

/// <summary>
/// Stores and runs <see cref="IAexonExtension"/> instances during startup.
/// Extensions fire in registration order; a failure in one extension aborts
/// the sequence and surfaces the exception to the host.
/// </summary>
public sealed class ExtensionRegistry
{
    private readonly List<IAexonExtension> _extensions = [];

    public void Add(IAexonExtension extension)
    {
        ArgumentNullException.ThrowIfNull(extension);
        _extensions.Add(extension);
    }

    public IReadOnlyList<IAexonExtension> Registered => _extensions;

    public async Task RunAsync(IAexonSessionBuilder builder, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(builder);

        foreach (var extension in _extensions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await extension.ConfigureAsync(builder, cancellationToken);
        }
    }
}

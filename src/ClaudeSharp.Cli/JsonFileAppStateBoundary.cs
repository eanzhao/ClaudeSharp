using System.Text.Json;
using ClaudeSharp.Core.AppState;

namespace ClaudeSharp.Cli;

/// <summary>
/// Writes app-state snapshots to a JSON file for external hosts to observe.
/// </summary>
internal sealed class JsonFileAppStateBoundary : IAppStateHostBoundary
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _path;

    public JsonFileAppStateBoundary(string path)
    {
        _path = Path.GetFullPath(path);
    }

    public async Task ApplyAsync(
        AppStateSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(snapshot, SerializerOptions);
        await File.WriteAllTextAsync(_path, json, cancellationToken);
    }
}

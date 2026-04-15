namespace Aexon.Core.Memory;

/// <summary>
/// Represents session memory file.
/// </summary>
public sealed record SessionMemoryFile(
    string Path,
    string SessionId,
    string ProjectRootDirectory)
{
    public async Task SaveAsync(
        string content,
        CancellationToken cancellationToken = default)
    {
        var directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(Path, content, cancellationToken);
    }

    public async Task<string?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(Path))
            return null;

        return await File.ReadAllTextAsync(Path, cancellationToken);
    }

    public bool Exists => File.Exists(Path);
}

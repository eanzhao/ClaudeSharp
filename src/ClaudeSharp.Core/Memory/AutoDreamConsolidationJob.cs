using System.Text.Json;

namespace ClaudeSharp.Core.Memory;

/// <summary>
/// Captures the lock metadata for an autodream consolidation run.
/// </summary>
public sealed record AutoDreamConsolidationJobInfo(
    string ProjectId,
    string? TeamName,
    string? SessionId,
    string Reason,
    DateTimeOffset StartedAt,
    string MachineName);

/// <summary>
/// Represents a held autodream consolidation lock.
/// </summary>
public sealed class AutoDreamConsolidationJob : IAsyncDisposable
{
    private readonly FileStream _lockStream;

    private AutoDreamConsolidationJob(string lockPath, AutoDreamConsolidationJobInfo info, FileStream lockStream)
    {
        LockPath = lockPath;
        Info = info;
        _lockStream = lockStream;
    }

    public string LockPath { get; }
    public AutoDreamConsolidationJobInfo Info { get; }

    public static async Task<AutoDreamConsolidationJob?> TryAcquireAsync(
        string lockPath,
        AutoDreamConsolidationJobInfo info,
        CancellationToken cancellationToken = default)
    {
        var directory = System.IO.Path.GetDirectoryName(lockPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        try
        {
            var stream = new FileStream(
                lockPath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 4096,
                options: FileOptions.Asynchronous);

            var payload = JsonSerializer.Serialize(info, new JsonSerializerOptions
            {
                WriteIndented = true,
            });

            await using (var writer = new StreamWriter(stream, leaveOpen: true))
            {
                await writer.WriteAsync(payload.AsMemory(), cancellationToken);
                await writer.FlushAsync(cancellationToken);
            }

            stream.Position = 0;
            return new AutoDreamConsolidationJob(lockPath, info, stream);
        }
        catch (IOException)
        {
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _lockStream.DisposeAsync();

        try
        {
            if (File.Exists(LockPath))
                File.Delete(LockPath);
        }
        catch
        {
            // Best effort cleanup.
        }
    }
}

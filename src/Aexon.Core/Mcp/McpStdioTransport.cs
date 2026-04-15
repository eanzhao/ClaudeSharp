using System.Diagnostics;
using System.Text;

namespace Aexon.Core.Mcp;

/// <summary>
/// Transports MCP messages over a stdio subprocess.
/// </summary>
public sealed class McpStdioTransport : IMcpLineTransport
{
    private readonly Process _process;
    private readonly StreamWriter _stdin;
    private readonly StreamReader _stdout;
    private readonly StreamReader _stderr;
    private readonly Action<string>? _stderrSink;
    private readonly Task _stderrPump;

    private McpStdioTransport(
        Process process,
        Action<string>? stderrSink)
    {
        _process = process;
        _stdin = process.StandardInput;
        _stdout = process.StandardOutput;
        _stderr = process.StandardError;
        _stderrSink = stderrSink;
        _stderrPump = PumpStderrAsync();
    }

    public static Task<McpStdioTransport> StartAsync(
        McpServerConfig config,
        string fallbackWorkingDirectory,
        Action<string>? stderrSink = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = config.Command,
                WorkingDirectory = config.WorkingDirectory ?? fallbackWorkingDirectory,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardInputEncoding = Encoding.UTF8,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            },
        };

        foreach (var arg in config.Args)
            process.StartInfo.ArgumentList.Add(arg);

        foreach (var entry in config.Environment)
            process.StartInfo.Environment[entry.Key] = entry.Value;

        process.Start();
        return Task.FromResult(new McpStdioTransport(process, stderrSink));
    }

    public async Task WriteLineAsync(string line, CancellationToken cancellationToken = default)
    {
        await _stdin.WriteLineAsync(line.AsMemory(), cancellationToken);
        await _stdin.FlushAsync(cancellationToken);
    }

    public Task<string?> ReadLineAsync(CancellationToken cancellationToken = default) =>
        _stdout.ReadLineAsync(cancellationToken).AsTask();

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
                _stdin.Close();
        }
        catch
        {
            // Ignore shutdown errors from stdin closing.
        }

        try
        {
            if (!_process.HasExited)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                try
                {
                    await _process.WaitForExitAsync(cts.Token);
                }
                catch (OperationCanceledException) when (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
        }
        catch
        {
            // Ignore teardown failures during cleanup.
        }

        try
        {
            await _stderrPump;
        }
        catch
        {
            // Ignore stderr pump failures during cleanup.
        }

        _process.Dispose();
    }

    private async Task PumpStderrAsync()
    {
        while (true)
        {
            var line = await _stderr.ReadLineAsync();
            if (line == null)
                break;

            if (!string.IsNullOrWhiteSpace(line))
                _stderrSink?.Invoke(line);
        }
    }
}

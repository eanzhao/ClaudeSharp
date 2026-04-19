using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aexon.Commands;

/// <summary>
/// In-process web host that serves the aevatar workflow studio frontend and
/// reverse-proxies <c>/api/*</c> to a remote aevatar backend. Ported from the
/// upstream <c>aevatar app</c> command, trimmed down for aexon's simpler
/// "single remote backend" use case (no local-vs-remote routing split).
/// </summary>
/// <remarks>
/// Excluded from coverage — the entire class boots Kestrel, opens browsers,
/// and proxies HTTP traffic. Unit-testing it in isolation is not productive;
/// behavioral correctness is verified by running <c>aexon aevatar web</c>
/// against mainnet.
/// </remarks>
[ExcludeFromCodeCoverage]
internal static class AevatarWebHost
{
    private static volatile string _currentApiBaseUrl = "https://aevatar-console-backend-api.aevatar.ai";

    public static async Task RunAsync(
        int port,
        string apiBaseUrl,
        string webRootSubdir,
        bool noBrowser,
        CancellationToken cancellationToken)
    {
        _currentApiBaseUrl = apiBaseUrl.TrimEnd('/');

        var webRootPath = ResolveWebRootPath(webRootSubdir);
        if (!File.Exists(Path.Combine(webRootPath, "index.html")))
        {
            Console.Error.WriteLine(
                $"  aevatar web: could not find frontend assets for '{webRootSubdir}'. Looked at: {webRootPath}");
            Console.Error.WriteLine(
                "  This tool must be installed as a packaged dotnet tool for the web UI to work.");
            return;
        }

        try
        {
            await StartOnceAsync(port, noBrowser, webRootPath, webRootSubdir, cancellationToken);
            return;
        }
        catch (Exception ex) when (IsAddressInUse(ex))
        {
            Console.WriteLine($"  Port {port} is already in use — attempting to free it.");
            var freed = await TryFreePortAsync(port, cancellationToken);
            if (!freed)
            {
                Console.Error.WriteLine(
                    $"  Could not free port {port}. Pick a different port with --port <n>.");
                throw;
            }

            Console.WriteLine($"  Freed port {port}, restarting…");
        }

        // Second and final attempt — if it still fails, let the exception propagate so
        // the caller can surface it to the user.
        await StartOnceAsync(port, noBrowser, webRootPath, webRootSubdir, cancellationToken);
    }

    private static async Task StartOnceAsync(
        int port,
        bool noBrowser,
        string webRootPath,
        string webRootSubdir,
        CancellationToken cancellationToken)
    {
        var baseDir = AppContext.BaseDirectory;
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = [],
            WebRootPath = webRootPath,
            ContentRootPath = baseDir,
        });

        // Silence ASP.NET Core's default startup chatter — the banner below is enough.
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://localhost:{port}");
        builder.Services.AddHttpClient("aevatar-api-proxy");

        var app = builder.Build();
        var localUrl = $"http://localhost:{port}";

        PrintBanner(localUrl, _currentApiBaseUrl, webRootPath, webRootSubdir);

        app.Lifetime.ApplicationStarted.Register(() =>
        {
            if (!noBrowser)
                OpenBrowser(localUrl);
        });

        app.UseDefaultFiles();
        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = ctx =>
            {
                ctx.Context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
                ctx.Context.Response.Headers.Pragma = "no-cache";
                ctx.Context.Response.Headers.Expires = "0";
            },
        });

        app.MapGet("/api/health", () => Results.Json(new { ok = true, service = "aexon.aevatar.web" }));

        // Frontend reads/writes the current proxy target at runtime — kept for UI parity.
        app.MapGet("/api/_proxy/runtime-url",
            () => Results.Json(new { runtimeBaseUrl = _currentApiBaseUrl }));
        app.MapPut("/api/_proxy/runtime-url", async (HttpContext ctx) =>
        {
            var body = await ctx.Request.ReadFromJsonAsync<RuntimeUrlUpdate>(ctx.RequestAborted);
            var url = body?.RuntimeBaseUrl?.Trim().TrimEnd('/');
            if (string.IsNullOrWhiteSpace(url))
                return Results.BadRequest(new { error = "runtimeBaseUrl is required" });
            _currentApiBaseUrl = url;
            return Results.Json(new { runtimeBaseUrl = _currentApiBaseUrl });
        });

        // OAuth callback: serve index.html so the frontend JS handles the code exchange.
        app.MapGet("/auth/callback", async (HttpContext ctx) =>
        {
            var indexPath = Path.Combine(webRootPath, "index.html");
            if (!File.Exists(indexPath))
            {
                ctx.Response.StatusCode = 404;
                return;
            }

            ctx.Response.ContentType = "text/html";
            ctx.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
            ctx.Response.Headers.Pragma = "no-cache";
            ctx.Response.Headers.Expires = "0";
            await ctx.Response.SendFileAsync(indexPath);
        });

        app.Map("/api/{**rest}", ProxyToBackend);
        app.MapFallbackToFile("index.html");

        try
        {
            await app.RunAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static bool IsAddressInUse(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is SocketException { SocketErrorCode: SocketError.AddressAlreadyInUse })
                return true;
        }

        return false;
    }

    /// <summary>
    /// Finds the process(es) listening on <paramref name="port"/>, kills them,
    /// and waits up to 2s per process for the OS to release the socket. Best-effort —
    /// returns true only when we were able to identify and kill at least one holder.
    /// </summary>
    private static async Task<bool> TryFreePortAsync(int port, CancellationToken cancellationToken)
    {
        var pids = await FindPidsOnPortAsync(port, cancellationToken);
        if (pids.Count == 0)
            return false;

        var killedAny = false;
        foreach (var pid in pids)
        {
            if (pid == Environment.ProcessId)
                continue; // refuse to kill ourselves

            try
            {
                using var proc = Process.GetProcessById(pid);
                var name = SafeProcessName(proc);
                Console.WriteLine($"  → killing pid {pid} ({name})");
                proc.Kill(entireProcessTree: false);
                try
                {
                    await proc.WaitForExitAsync(cancellationToken)
                        .WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
                }
                catch (TimeoutException)
                {
                    // Process didn't exit fast enough; next StartOnceAsync attempt will
                    // fail and the caller will get the error. Still report as killedAny
                    // so the caller retries once.
                }
                killedAny = true;
            }
            catch (ArgumentException)
            {
                // Already exited between find and kill — treat as success.
                killedAny = true;
            }
            catch (Exception ex) when (ex is InvalidOperationException
                                       or System.ComponentModel.Win32Exception
                                       or NotSupportedException)
            {
                Console.Error.WriteLine($"  (could not kill pid {pid}: {ex.Message})");
            }
        }

        return killedAny;
    }

    private static string SafeProcessName(Process proc)
    {
        try { return proc.ProcessName; }
        catch { return "?"; }
    }

    /// <summary>
    /// Lists PIDs holding a TCP listen socket on <paramref name="port"/>.
    /// Uses <c>lsof</c> on macOS/Linux and <c>netstat -ano</c> on Windows.
    /// Returns an empty list when the probe can't be run (tool missing, etc.).
    /// </summary>
    private static async Task<IReadOnlyList<int>> FindPidsOnPortAsync(
        int port,
        CancellationToken cancellationToken)
    {
        try
        {
            if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
                return await ProbeUnixAsync(port, cancellationToken);
            if (OperatingSystem.IsWindows())
                return await ProbeWindowsAsync(port, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.Error.WriteLine($"  (port probe failed: {ex.Message})");
        }

        return Array.Empty<int>();
    }

    private static async Task<IReadOnlyList<int>> ProbeUnixAsync(int port, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("lsof", $"-ti :{port} -sTCP:LISTEN")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var proc = Process.Start(psi);
        if (proc is null)
            return Array.Empty<int>();

        var output = await proc.StandardOutput.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        return ParsePidLines(output);
    }

    private static async Task<IReadOnlyList<int>> ProbeWindowsAsync(int port, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("netstat", "-ano -p tcp")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var proc = Process.Start(psi);
        if (proc is null)
            return Array.Empty<int>();

        var output = await proc.StandardOutput.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);

        var needle = $":{port}";
        var pids = new HashSet<int>();
        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.Contains(needle, StringComparison.Ordinal) ||
                !trimmed.Contains("LISTENING", StringComparison.Ordinal))
                continue;

            var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length > 0 && int.TryParse(parts[^1], out var pid) && pid > 0)
                pids.Add(pid);
        }

        return pids.ToArray();
    }

    private static IReadOnlyList<int> ParsePidLines(string output)
    {
        var pids = new HashSet<int>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(line, out var pid) && pid > 0)
                pids.Add(pid);
        }

        return pids.ToArray();
    }

    private static async Task ProxyToBackend(HttpContext ctx, IHttpClientFactory factory)
    {
        var client = factory.CreateClient("aevatar-api-proxy");
        var path = ctx.Request.Path + ctx.Request.QueryString;
        var targetBase = _currentApiBaseUrl.TrimEnd('/');
        var targetUri = new Uri($"{targetBase}{path}");

        var requestMessage = new HttpRequestMessage
        {
            Method = new HttpMethod(ctx.Request.Method),
            RequestUri = targetUri,
        };

        foreach (var header in ctx.Request.Headers)
        {
            if (header.Key.StartsWith("Host", StringComparison.OrdinalIgnoreCase) ||
                header.Key.StartsWith("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
                continue;

            requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        }

        if (ctx.Request.ContentLength > 0 || ctx.Request.ContentType != null)
        {
            requestMessage.Content = new StreamContent(ctx.Request.Body);
            if (ctx.Request.ContentType != null)
            {
                requestMessage.Content.Headers.ContentType =
                    System.Net.Http.Headers.MediaTypeHeaderValue.Parse(ctx.Request.ContentType);
            }
        }

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(
                requestMessage,
                HttpCompletionOption.ResponseHeadersRead,
                ctx.RequestAborted);
        }
        catch (HttpRequestException)
        {
            ctx.Response.StatusCode = 502;
            await ctx.Response.WriteAsJsonAsync(new { error = "Backend API is unreachable", target = targetBase });
            return;
        }

        ctx.Response.StatusCode = (int)response.StatusCode;
        var isSse = false;
        foreach (var header in response.Headers.Concat(response.Content.Headers))
        {
            if (header.Key.StartsWith("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
                continue;

            ctx.Response.Headers[header.Key] = header.Value.ToArray();

            if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase) &&
                header.Value.Any(v => v.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase)))
                isSse = true;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ctx.RequestAborted);
        if (isSse)
        {
            // Disable buffering so the browser receives SSE events as they arrive.
            ctx.Response.Headers["Cache-Control"] = "no-store";
            ctx.Response.Headers["X-Accel-Buffering"] = "no";
            var buffer = new byte[4096];
            int bytesRead;
            while ((bytesRead = await stream.ReadAsync(buffer, ctx.RequestAborted)) > 0)
            {
                await ctx.Response.Body.WriteAsync(buffer.AsMemory(0, bytesRead), ctx.RequestAborted);
                await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
            }
        }
        else
        {
            await stream.CopyToAsync(ctx.Response.Body, ctx.RequestAborted);
        }
    }

    private static string ResolveWebRootPath(string subdir)
    {
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "wwwroot", subdir),
            // dev-time fallback when running from the source checkout:
            Path.GetFullPath(Path.Combine(baseDir, $"../../../../Aexon.Commands/wwwroot/{subdir}")),
        };

        return candidates.FirstOrDefault(p => File.Exists(Path.Combine(p, "index.html")))
               ?? candidates[0];
    }

    private static void PrintBanner(string url, string apiBaseUrl, string webRootPath, string webRootSubdir)
    {
        // Map the wwwroot subdir back to the user-visible subcommand name.
        var subcommand = webRootSubdir switch
        {
            "aevatar-chat" => "chat",
            "aevatar-workbench" => "web",
            _ => "web",
        };

        Console.WriteLine();
        Console.WriteLine($"  aexon aevatar {subcommand}");
        Console.WriteLine($"  Web UI:   {url}");
        Console.WriteLine($"  API:      {apiBaseUrl}");
        Console.WriteLine($"  WebRoot:  {webRootPath}");
        Console.WriteLine("  Press Ctrl+C to stop");
        Console.WriteLine();
    }

    private static void OpenBrowser(string url)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Process.Start(new ProcessStartInfo("cmd", $"/c start {url}") { CreateNoWindow = true });
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                Process.Start("open", url);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                Process.Start("xdg-open", url);
        }
        catch
        {
            // Swallow: best-effort only. The URL is printed above.
        }
    }

    private sealed record RuntimeUrlUpdate(string? RuntimeBaseUrl);
}

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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
        bool noBrowser,
        CancellationToken cancellationToken)
    {
        _currentApiBaseUrl = apiBaseUrl.TrimEnd('/');

        var webRootPath = ResolveWebRootPath();
        if (!File.Exists(Path.Combine(webRootPath, "index.html")))
        {
            Console.Error.WriteLine(
                $"  aevatar web: could not find frontend assets. Looked at: {webRootPath}");
            Console.Error.WriteLine(
                "  This tool must be installed as a packaged dotnet tool for the web UI to work.");
            return;
        }

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

        PrintBanner(localUrl, _currentApiBaseUrl, webRootPath);

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

    private static string ResolveWebRootPath()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "wwwroot", "aevatar"),
            // dev-time fallback when running from the source checkout (bin/Debug/net10.0):
            Path.GetFullPath(Path.Combine(baseDir, "../../../../Aexon.Commands/wwwroot/aevatar")),
        };

        return candidates.FirstOrDefault(p => File.Exists(Path.Combine(p, "index.html")))
               ?? candidates[0];
    }

    private static void PrintBanner(string url, string apiBaseUrl, string webRootPath)
    {
        Console.WriteLine();
        Console.WriteLine("  aexon aevatar web");
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

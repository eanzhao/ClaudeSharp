using System.Globalization;
using System.Text;
using Aexon.Core.Aevatar;
using Aexon.Core.Auth;
using Aexon.Core.Commands;
using Spectre.Console;

namespace Aexon.Commands;

/// <summary>
/// Lists and mutates blobs in the caller's aevatar chrono-storage bucket via
/// the aevatar backend's explorer proxy.
///
///   /storage ls [prefix]                browse the manifest (optionally filtered)
///   /storage cat &lt;key&gt;                print a text file
///   /storage get &lt;key&gt; [local]         download to a local path (or stdout)
///   /storage put &lt;key&gt; &lt;local&gt;        upload a binary file (multipart)
///   /storage put-text &lt;key&gt;            write text from stdin
///   /storage rm &lt;key&gt;                  delete a file
///
/// Reuses <see cref="AevatarChatSettingsStore"/> for base-URL resolution (so
/// <c>/aevatar config set-url</c> also points <c>/storage</c> at the same
/// backend) and <see cref="NyxIdTokenProvider"/> for authentication.
/// </summary>
public sealed class StorageCommand(
    AevatarChatSettingsStore settingsStore,
    NyxIdTokenProvider tokenProvider) : ICommand
{
    public string Name => "storage";
    public string Description => "Browse / upload / download files in aevatar chrono-storage";

    public async Task ExecuteAsync(string args, CommandContext context)
    {
        var trimmed = (args ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            PrintUsage(context);
            return;
        }

        var (head, rest) = SplitHead(trimmed);
        switch (head.ToLowerInvariant())
        {
            case "ls":
            case "list":
                await ListAsync(rest, context);
                return;

            case "cat":
                await CatAsync(rest, context);
                return;

            case "get":
                await GetAsync(rest, context);
                return;

            case "put":
                await PutAsync(rest, context);
                return;

            case "put-text":
                await PutTextAsync(rest, context);
                return;

            case "rm":
            case "delete":
                await DeleteAsync(rest, context);
                return;

            case "help":
            case "-h":
            case "--help":
                PrintUsage(context);
                return;

            default:
                AnsiConsole.MarkupLine($"[red]  ✗ unknown subcommand:[/] [white]{Markup.Escape(head)}[/]");
                PrintUsage(context);
                return;
        }
    }

    // ── Subcommands ──

    private async Task ListAsync(string prefix, CommandContext context)
    {
        using var client = BuildClient();
        try
        {
            var files = await client.ListAsync(context.CancellationToken);
            var filtered = string.IsNullOrWhiteSpace(prefix)
                ? (IReadOnlyList<AevatarStorageFile>)files
                : files
                    .Where(f => f.Key.StartsWith(prefix.Trim(), StringComparison.Ordinal))
                    .ToList();

            if (filtered.Count == 0)
            {
                AnsiConsole.MarkupLine(
                    string.IsNullOrWhiteSpace(prefix)
                        ? "[dim]  (no files in storage for this scope)[/]"
                        : $"[dim]  (no files matching[/] [white]{Markup.Escape(prefix.Trim())}[/][dim])[/]");
                return;
            }

            var table = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Grey27)
                .Expand()
                .Title($"[dim]{filtered.Count} file(s)[/]");

            table.AddColumn(new TableColumn("[bold]key[/]"));
            table.AddColumn(new TableColumn("[bold]type[/]").NoWrap());
            table.AddColumn(new TableColumn("[bold]size[/]").RightAligned().NoWrap());
            table.AddColumn(new TableColumn("[bold]updated[/]").NoWrap());

            foreach (var file in filtered.OrderByDescending(f => f.UpdatedAt, StringComparer.Ordinal))
            {
                table.AddRow(
                    $"[white]{Markup.Escape(file.Key)}[/]",
                    $"[cyan]{Markup.Escape(file.Type ?? "file")}[/]",
                    file.Size.HasValue ? FormatSize(file.Size.Value) : "[dim]—[/]",
                    $"[dim]{Markup.Escape(FormatTimestamp(file.UpdatedAt))}[/]");
            }

            AnsiConsole.Write(table);
        }
        catch (Exception ex)
        {
            WriteError(ex);
        }
    }

    private async Task CatAsync(string key, CommandContext context)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            AnsiConsole.MarkupLine("[dim]  Usage: /storage cat <key>[/]");
            return;
        }

        using var client = BuildClient();
        try
        {
            var content = await client.GetAsync(key.Trim(), context.CancellationToken);
            if (content.IsLikelyText)
            {
                var text = Encoding.UTF8.GetString(content.Bytes);
                Console.Write(text);
                if (text.Length > 0 && text[^1] != '\n')
                    Console.WriteLine();
            }
            else
            {
                AnsiConsole.MarkupLine(
                    $"[yellow]  ⚠ {Markup.Escape(content.MediaType)} is not a text format[/] " +
                    "[dim](use `/storage get <key> <path>` to download as a binary)[/]");
            }
        }
        catch (Exception ex)
        {
            WriteError(ex);
        }
    }

    private async Task GetAsync(string args, CommandContext context)
    {
        var (key, rest) = SplitHead(args);
        if (string.IsNullOrWhiteSpace(key))
        {
            AnsiConsole.MarkupLine("[dim]  Usage: /storage get <key> [[local-path]][/]");
            return;
        }

        using var client = BuildClient();
        try
        {
            var content = await client.GetAsync(key.Trim(), context.CancellationToken);
            var outputPath = string.IsNullOrWhiteSpace(rest) ? null : rest.Trim();

            if (outputPath is null)
            {
                // write raw bytes to stdout; preserve binary fidelity
                using var stdout = Console.OpenStandardOutput();
                stdout.Write(content.Bytes);
                return;
            }

            var resolved = Path.GetFullPath(outputPath);
            var directory = Path.GetDirectoryName(resolved);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            await File.WriteAllBytesAsync(resolved, content.Bytes, context.CancellationToken);
            AnsiConsole.MarkupLine(
                $"[springgreen3]  ● saved[/] [white]{Markup.Escape(resolved)}[/] " +
                $"[dim]({FormatSize(content.Bytes.Length)}, {Markup.Escape(content.MediaType)})[/]");
        }
        catch (Exception ex)
        {
            WriteError(ex);
        }
    }

    private async Task PutAsync(string args, CommandContext context)
    {
        var (key, rest) = SplitHead(args);
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(rest))
        {
            AnsiConsole.MarkupLine("[dim]  Usage: /storage put <key> <local-path>[/]");
            return;
        }

        var path = Path.GetFullPath(rest.Trim());
        if (!File.Exists(path))
        {
            AnsiConsole.MarkupLine($"[red]  ✗ file not found:[/] [white]{Markup.Escape(path)}[/]");
            return;
        }

        using var client = BuildClient();
        try
        {
            await using var stream = File.OpenRead(path);
            var mediaType = InferMediaType(path);
            var upload = await client.UploadAsync(
                key.Trim(),
                stream,
                Path.GetFileName(path),
                mediaType,
                context.CancellationToken);

            AnsiConsole.MarkupLine(
                $"[springgreen3]  ● uploaded[/] [white]{Markup.Escape(upload.Key)}[/] " +
                $"[dim]({FormatSize(upload.Size)}, {Markup.Escape(upload.ContentType ?? mediaType ?? "application/octet-stream")})[/]");
        }
        catch (Exception ex)
        {
            WriteError(ex);
        }
    }

    private async Task PutTextAsync(string args, CommandContext context)
    {
        var key = args.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            AnsiConsole.MarkupLine("[dim]  Usage: /storage put-text <key>   (reads body from stdin until EOF)[/]");
            return;
        }

        if (!Console.IsInputRedirected)
        {
            AnsiConsole.MarkupLine(
                "[dim]  Type the body, then press Ctrl+D (Unix) / Ctrl+Z Enter (Windows) to finish:[/]");
        }

        var body = await Console.In.ReadToEndAsync(context.CancellationToken);
        using var client = BuildClient();
        try
        {
            var mediaType = InferTextMediaType(key);
            await client.PutTextAsync(key, body, mediaType, context.CancellationToken);
            AnsiConsole.MarkupLine(
                $"[springgreen3]  ● wrote[/] [white]{Markup.Escape(key)}[/] " +
                $"[dim]({FormatSize(Encoding.UTF8.GetByteCount(body))}, {Markup.Escape(mediaType)})[/]");
        }
        catch (Exception ex)
        {
            WriteError(ex);
        }
    }

    private async Task DeleteAsync(string args, CommandContext context)
    {
        var key = args.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            AnsiConsole.MarkupLine("[dim]  Usage: /storage rm <key>[/]");
            return;
        }

        using var client = BuildClient();
        try
        {
            await client.DeleteAsync(key, context.CancellationToken);
            AnsiConsole.MarkupLine($"[springgreen3]  ● deleted[/] [white]{Markup.Escape(key)}[/]");
        }
        catch (Exception ex)
        {
            WriteError(ex);
        }
    }

    // ── Helpers ──

    private AevatarStorageClient BuildClient()
    {
        var settings = settingsStore.Load();
        var baseUrl = AevatarChatSettingsStore.ResolveBaseUrl(settings, @override: null);
        return new AevatarStorageClient(baseUrl, tokenProvider);
    }

    private static void WriteError(Exception ex)
    {
        switch (ex)
        {
            case OperationCanceledException:
                AnsiConsole.MarkupLine("[dim]  (cancelled)[/]");
                break;
            case NotLoggedInException:
                AnsiConsole.MarkupLine($"[red]  ✗ {Markup.Escape(ex.Message)}[/]");
                break;
            case AevatarStorageException storage:
                AnsiConsole.MarkupLine($"[red]  ✗ {Markup.Escape(storage.Message)}[/]");
                break;
            default:
                AnsiConsole.MarkupLine($"[red]  ✗ storage error:[/] {Markup.Escape(ex.Message)}");
                break;
        }
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }

    private static string FormatTimestamp(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso))
            return "—";

        return DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
            : iso;
    }

    private static string? InferMediaType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            ".pdf" => "application/pdf",
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            ".mp4" => "video/mp4",
            ".webm" => "video/webm",
            ".json" => "application/json",
            ".yaml" or ".yml" => "application/yaml",
            ".txt" or ".md" or ".markdown" => "text/plain",
            ".html" or ".htm" => "text/html",
            ".csv" => "text/csv",
            _ => null,
        };
    }

    private static string InferTextMediaType(string key)
    {
        var ext = Path.GetExtension(key).ToLowerInvariant();
        return ext switch
        {
            ".json" => "application/json",
            ".yaml" or ".yml" => "application/yaml",
            ".md" or ".markdown" => "text/markdown",
            ".html" or ".htm" => "text/html",
            ".csv" => "text/csv",
            _ => "text/plain",
        };
    }

    private static (string head, string rest) SplitHead(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return (string.Empty, string.Empty);

        var trimmed = value.TrimStart();
        var spaceIndex = trimmed.IndexOf(' ');
        return spaceIndex < 0
            ? (trimmed, string.Empty)
            : (trimmed[..spaceIndex], trimmed[(spaceIndex + 1)..].Trim());
    }

    private static void PrintUsage(CommandContext context)
    {
        _ = context;
        // Spectre markup treats `[` as the start of a style tag, so every literal
        // square bracket in the help text below is doubled as `[[` / `]]`.
        AnsiConsole.MarkupLine("  Usage:");
        AnsiConsole.MarkupLine("    [bold]/storage ls[/] [dim][[prefix]][/]                   browse the scope manifest");
        AnsiConsole.MarkupLine("    [bold]/storage cat[/] [dim]<key>[/]                     print a text file");
        AnsiConsole.MarkupLine("    [bold]/storage get[/] [dim]<key> [[local-path]][/]        download (stdout if no path)");
        AnsiConsole.MarkupLine("    [bold]/storage put[/] [dim]<key> <local-path>[/]        upload a binary (multipart, ≤50 MB)");
        AnsiConsole.MarkupLine("    [bold]/storage put-text[/] [dim]<key>[/]                write stdin as text");
        AnsiConsole.MarkupLine("    [bold]/storage rm[/] [dim]<key>[/]                      delete a file");
    }
}

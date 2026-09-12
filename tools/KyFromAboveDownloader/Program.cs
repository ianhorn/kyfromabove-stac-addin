// KyFromAboveDownloader: stand-alone console downloader generated/bundled by the
// KyFromAbove-STAC ArcGIS Pro add-in's "Export Script" -> Executable option.
//
// Reads manifest.json (written next to this exe by Export Script) and downloads every
// listed asset in parallel, up to the requested concurrency. No ArcGIS Pro, and (when
// published self-contained/single-file, as the add-in bundles it) no .NET runtime install,
// is required to run it.
//
// Usage: KyFromAboveDownloader.exe [path-to-manifest.json]
//   Defaults to "manifest.json" next to the exe when no argument is given.

using System.Net.Http;
using System.Threading;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KyFromAboveDownloader;

internal sealed class Asset
{
    public string Url { get; set; } = "";
    public string RelPath { get; set; } = "";
}

internal sealed class Manifest
{
    public string DestFolder { get; set; } = "";
    public int Concurrency { get; set; } = 4;
    public List<Asset> Assets { get; set; } = new();
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(Manifest))]
internal partial class ManifestJsonContext : JsonSerializerContext
{
}

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        // Default manifest: same folder and filename stem as this exe (e.g. next to
        // "KyFromAbove_download_20260911.exe" it looks for "KyFromAbove_download_20260911.json"),
        // not a fixed "manifest.json" -- so several exported kits can share one destination
        // folder without colliding, and a plain double-click still just works.
        var manifestPath = args.Length > 0
            ? args[0]
            : Path.ChangeExtension(Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "KyFromAboveDownloader.exe"), ".json");

        if (!File.Exists(manifestPath))
        {
            Console.WriteLine($"Manifest not found: {manifestPath}");
            Pause();
            return 1;
        }

        Manifest manifest;
        try
        {
            var json = await File.ReadAllTextAsync(manifestPath);
            manifest = JsonSerializer.Deserialize(json, ManifestJsonContext.Default.Manifest)
                       ?? throw new InvalidDataException("Manifest deserialized to null.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not read manifest: {ex.Message}");
            Pause();
            return 1;
        }

        if (manifest.Assets.Count == 0)
        {
            Console.WriteLine("Manifest has no assets to download.");
            Pause();
            return 0;
        }

        Directory.CreateDirectory(manifest.DestFolder);
        Console.WriteLine($"KyFromAbove-STAC downloader -- {manifest.Assets.Count} asset(s) -> {manifest.DestFolder}");
        Console.WriteLine($"Concurrency: {Math.Max(1, manifest.Concurrency)}");
        Console.WriteLine();

        using var http = new HttpClient();
        http.Timeout = TimeSpan.FromMinutes(30);
        var sem = new SemaphoreSlim(Math.Max(1, manifest.Concurrency));
        long ok = 0, fail = 0;
        var lockObj = new object();

        var tasks = manifest.Assets.Select(async asset =>
        {
            await sem.WaitAsync();
            try
            {
                var dest = Path.Combine(manifest.DestFolder, asset.RelPath);
                Directory.CreateDirectory(Path.GetDirectoryName(dest) ?? manifest.DestFolder);

                using var resp = await http.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead);
                resp.EnsureSuccessStatusCode();
                await using (var src = await resp.Content.ReadAsStreamAsync())
                await using (var dst = new FileStream(dest, FileMode.Create, FileAccess.Write))
                {
                    await src.CopyToAsync(dst);
                }

                lock (lockObj) { ok++; }
                Console.WriteLine($"OK   {asset.RelPath}");
            }
            catch (Exception ex)
            {
                lock (lockObj) { fail++; }
                Console.WriteLine($"FAIL {asset.RelPath}: {ex.Message}");
            }
            finally
            {
                sem.Release();
            }
        });

        await Task.WhenAll(tasks);

        Console.WriteLine();
        Console.WriteLine($"Done: {ok} succeeded, {fail} failed -> {manifest.DestFolder}");
        Pause();
        return fail == 0 ? 0 : 2;
    }

    // Keeps the console window open when double-clicked (no redirected stdin means an
    // interactive console, i.e. not launched from a script/CI pipe).
    private static void Pause()
    {
        if (Console.IsInputRedirected) return;
        Console.WriteLine();
        Console.Write("Press Enter to exit...");
        Console.ReadLine();
    }
}

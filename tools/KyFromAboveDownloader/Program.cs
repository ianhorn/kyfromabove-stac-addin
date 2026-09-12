// KyFromAboveDownloader: stand-alone console downloader generated/bundled by the
// KyFromAbove-STAC ArcGIS Pro add-in's "Export Script" -> Executable option.
//
// Reads its manifest (asset list, destination folder, concurrency) and downloads every
// listed asset in parallel. No ArcGIS Pro, and (when published self-contained/single-file,
// as the add-in bundles it) no .NET runtime install, is required to run it.
//
// The manifest normally travels APPENDED to this exe's own file (see TryReadEmbeddedManifest
// / the matching write side in SearchDockpaneViewModel.BuildDownloaderManifest), so Export
// Script's "Executable" option produces a single, self-contained .exe -- no sidecar file.
// A manifest.json path can still be passed explicitly (or found next to the exe, same
// filename stem) for local testing without rebuilding a manifest-embedded exe each time.
//
// Usage: KyFromAboveDownloader.exe [path-to-manifest.json]

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
    // Must match SearchDockpaneViewModel's write side exactly: 8 bytes ASCII magic, right at
    // EOF, with the 8-byte little-endian JSON byte-length immediately before it. Appending
    // bytes after a published single-file .NET exe is safe -- the runtime locates its own
    // bundle via an absolute offset baked into the PE headers at publish time, not by
    // scanning from the end of the file, so trailing bytes are simply ignored by the loader.
    private static readonly byte[] ManifestFooterMagic = "KYFAMAN1"u8.ToArray();

    private static async Task<int> Main(string[] args)
    {
        var manifest = TryReadEmbeddedManifest();

        if (manifest == null)
        {
            // Fall back to an external manifest.json: an explicit path, or one next to this
            // exe with the same filename stem (e.g. "KyFromAbove_download_20260911.exe" looks
            // for "KyFromAbove_download_20260911.json") -- useful for local testing.
            var manifestPath = args.Length > 0
                ? args[0]
                : Path.ChangeExtension(Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "KyFromAboveDownloader.exe"), ".json");

            if (!File.Exists(manifestPath))
            {
                Console.WriteLine($"No manifest embedded in this exe, and no manifest file found at: {manifestPath}");
                Pause();
                return 1;
            }

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

    /// <summary>
    /// Look for a manifest appended to this exe's own file: [JSON bytes][8-byte little-endian
    /// JSON length][8-byte magic] at EOF. Returns null (not an error) if absent or malformed,
    /// so callers fall back to an external manifest.json.
    /// </summary>
    private static Manifest? TryReadEmbeddedManifest()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return null;

            using var fs = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length < 16) return null;

            var footer = new byte[16];
            fs.Seek(-16, SeekOrigin.End);
            fs.ReadExactly(footer, 0, 16);

            for (int i = 0; i < 8; i++)
                if (footer[8 + i] != ManifestFooterMagic[i]) return null;

            long jsonLen = BitConverter.ToInt64(footer, 0);
            if (jsonLen <= 0 || jsonLen > fs.Length - 16) return null;

            fs.Seek(-16 - jsonLen, SeekOrigin.End);
            var jsonBytes = new byte[jsonLen];
            fs.ReadExactly(jsonBytes, 0, (int)jsonLen);

            var json = System.Text.Encoding.UTF8.GetString(jsonBytes);
            return JsonSerializer.Deserialize(json, ManifestJsonContext.Default.Manifest);
        }
        catch
        {
            return null;
        }
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

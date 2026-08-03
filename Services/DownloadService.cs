/*
 * Download service: streams STAC assets (COGs/LAZ) to local disk with progress.
 */
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using KyFromAbove.Stac;

namespace KyFromAbove.Services
{
    /// <summary>Progress report for a single asset download.</summary>
    public class DownloadProgress
    {
        public long BytesReceived { get; set; }
        public long? TotalBytes { get; set; }
        public double? Percent => TotalBytes > 0 ? (BytesReceived * 100.0 / TotalBytes) : (double?)null;
    }

    /// <summary>Result of a download attempt.</summary>
    public class DownloadResult
    {
        public bool Success { get; set; }
        public string LocalPath { get; set; }
        public long Bytes { get; set; }
        public string Error { get; set; }
    }

    /// <summary>Downloads STAC assets to disk with progress reporting and cancellation.</summary>
    public class DownloadService
    {
        private readonly StacClient _client;

        public DownloadService(StacClient client) { _client = client; }

        /// <summary>
        /// Download an asset to a destination path. Progress is reported periodically.
        /// </summary>
        public async Task<DownloadResult> DownloadAssetAsync(string assetHref, string destinationPath,
            IProgress<DownloadProgress> progress = null, CancellationToken ct = default)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? ".");

                // Try to get total size up front (for progress %).
                long? total = await _client.GetContentLengthAsync(assetHref, ct).ConfigureAwait(false);

                using var src = await _client.OpenAssetStreamAsync(assetHref, ct).ConfigureAwait(false);
                await using (var dst = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                {
                    var buffer = new byte[81920];
                    long received = 0;
                    int read;
                    while ((read = await src.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        await dst.WriteAsync(buffer, 0, read, ct).ConfigureAwait(false);
                        received += read;
                        progress?.Report(new DownloadProgress { BytesReceived = received, TotalBytes = total });
                    }
                    return new DownloadResult { Success = true, LocalPath = destinationPath, Bytes = received };
                }
            }
            catch (OperationCanceledException)
            {
                TryDelete(destinationPath);
                return new DownloadResult { Success = false, LocalPath = destinationPath, Error = "Cancelled" };
            }
            catch (Exception ex)
            {
                TryDelete(destinationPath);
                return new DownloadResult { Success = false, LocalPath = destinationPath, Error = ex.Message };
            }
        }

        /// <summary>Suggest a local file name for an asset based on its href.</summary>
        public static string SuggestFileName(StacAsset asset, StacItem item)
        {
            var href = asset?.Href;
            if (!string.IsNullOrWhiteSpace(href))
            {
                var name = Path.GetFileName(new Uri(href).LocalPath);
                if (!string.IsNullOrWhiteSpace(name)) return name;
            }
            return string.IsNullOrWhiteSpace(item?.Id) ? "download.bin" : item.Id;
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { /* ignore */ }
        }
    }
}

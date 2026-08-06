/*
 * Per-result view model for a single STAC item shown in the results list.
 */
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Input;
using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;
using KyFromAboveSTAC.Stac;

namespace KyFromAboveSTAC
{
    /// <summary>Represents one STAC item row in the search results.</summary>
    public class ResultItemViewModel : PropertyChangedBase
    {
        private bool _isSelected;
        private bool _isDownloading;
        private double _downloadProgress;
        private string _status;
        private string _thumbnailLocalPath;

        public ResultItemViewModel(StacItem item, StacCollection collection, bool loadThumbnail = true)
        {
            Item = item ?? throw new ArgumentNullException(nameof(item));
            CollectionId = item.Collection;
            CollectionTitle = collection?.TitleOrId ?? item.Collection ?? item.Id;

            AddToMapCommand = new RelayCommand(async () => await OnAddToMapAsync(), () => !IsDownloading);
            DownloadCommand = new RelayCommand(async () => await OnDownloadAsync(), () => !IsDownloading);
            ZoomToCommand = new RelayCommand(async () => await OnZoomToAsync(), () => !IsDownloading);
            ShowFootprintCommand = new RelayCommand(async () => await OnShowFootprintAsync(), () => !IsDownloading);

            // Display text
            TitleText = item.Id;
            DateText = FormatDate(item.Properties?.StartDatetime ?? item.Properties?.Datetime,
                                  item.Properties?.EndDatetime);
            DataAsset = item.GetDataAsset();
            ThumbnailUrl = item.GetThumbnailAsset()?.Href;
            if (loadThumbnail)
                _ = EnsureThumbnailAsync();
        }

        public StacItem Item { get; }
        public string CollectionId { get; }
        public string CollectionTitle { get; }
        public StacAsset DataAsset { get; }
        public string ThumbnailUrl { get; }
        public string ThumbnailLocalPath
        {
            get => _thumbnailLocalPath;
            private set => SetProperty(ref _thumbnailLocalPath, value, () => ThumbnailLocalPath);
        }
        public string TitleText { get; }
        public string DateText { get; }

        private string _detailJson;
        /// <summary>Pretty-printed JSON of the underlying STAC item -- shown as a tooltip when hovering over the result row.</summary>
        public string DetailJson => _detailJson ??= BuildDetailJson();

        private string BuildDetailJson()
        {
            try
            {
                return JsonSerializer.Serialize(Item, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception ex)
            {
                return "(could not render item JSON: " + ex.Message + ")";
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value, () => IsSelected);
        }

        public bool IsDownloading
        {
            get => _isDownloading;
            set => SetProperty(ref _isDownloading, value, () => IsDownloading);
        }

        public double DownloadProgress
        {
            get => _downloadProgress;
            set => SetProperty(ref _downloadProgress, value, () => DownloadProgress);
        }

        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value, () => Status);
        }

        public ICommand AddToMapCommand { get; }
        public ICommand DownloadCommand { get; }
        public ICommand ZoomToCommand { get; }
        public ICommand ShowFootprintCommand { get; }

        private async System.Threading.Tasks.Task OnAddToMapAsync()
        {
            if (DataAsset == null) { Status = "No data asset"; return; }
            IsDownloading = true; Status = "Adding to map...";
            try
            {
                var ok = await Services.MapService.AddRasterLayerFromUrlAsync(DataAsset.Href, Item.Id);
                Status = ok ? "Added to map" : "Could not add (see Pro messages)";
            }
            finally { IsDownloading = false; }
        }

        private async System.Threading.Tasks.Task OnZoomToAsync()
        {
            await Services.MapService.ZoomToItemAsync(Item);
        }

        private async System.Threading.Tasks.Task OnShowFootprintAsync()
        {
            IsDownloading = true; Status = "Adding footprint to map...";
            try
            {
                var ok = await Services.MapService.AddFootprintsLayerAsync(new[] { Item });
                if (ok)
                {
                    Status = "Footprint added.";
                }
                else
                {
                    Status = "Failed to add footprint.";
                    System.Windows.MessageBox.Show("Could not add the footprint to the map. Check ArcGIS Pro messages or ensure GeoJSON support is available.", "KyFromAbove-STAC: Footprint", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                Status = "Footprint failed: " + ex.Message;
            }
            finally
            {
                IsDownloading = false;
            }
            // Ensure the item is visible on the map
            await Services.MapService.ZoomToItemAsync(Item);
        }

        private async System.Threading.Tasks.Task OnDownloadAsync()
        {
            if (DataAsset == null) { Status = "No data asset"; return; }
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                FileName = Services.DownloadService.SuggestFileName(DataAsset, Item),
                Title = "Download asset to..."
            };
            if (dlg.ShowDialog() != true) return;

            IsDownloading = true; DownloadProgress = 0; Status = "Downloading...";
            try
            {
                var dl = new Services.DownloadService(Module1.Current.StacClient);
                var progress = new Progress<Services.DownloadProgress>(p =>
                {
                    DownloadProgress = p.Percent ?? (p.TotalBytes > 0 ? (p.BytesReceived * 100.0 / p.TotalBytes.Value) : 0);
                });
                var result = await dl.DownloadAssetAsync(DataAsset.Href, dlg.FileName, progress);
                Status = result.Success ? $"Saved to {result.LocalPath}" : ("Failed: " + result.Error);
            }
            finally { IsDownloading = false; }
        }

        private static string FormatDate(string start, string end)
        {
            string Trim(string s) => string.IsNullOrWhiteSpace(s) ? null : s.TrimEnd('Z', ' ');
            var s = Trim(start);
            var e = Trim(end);
            if (s == null && e == null) return null;
            if (s != null && e != null && s != e) return $"{s} … {e}";
            return s ?? e;
        }
        private async System.Threading.Tasks.Task EnsureThumbnailAsync()
        {
            var href = ResolveThumbnailHref();
            if (string.IsNullOrWhiteSpace(href)) return;

            try
            {
                var tempDir = Path.Combine(Path.GetTempPath(), "KyFromAbove-STAC", "thumbs");
                Directory.CreateDirectory(tempDir);
                var safeName = Item.Id;
                foreach (var ch in System.IO.Path.GetInvalidFileNameChars()) safeName = safeName.Replace(ch, '_');
                var ext = Path.GetExtension(new Uri(href).LocalPath);
                if (string.IsNullOrWhiteSpace(ext)) ext = ".png";
                var localPath = Path.Combine(tempDir, safeName + ext);

                if (!File.Exists(localPath))
                {
                    using var client = new System.Net.Http.HttpClient();
                    var bytes = await client.GetByteArrayAsync(href);
                    await File.WriteAllBytesAsync(localPath, bytes);
                }

                ThumbnailLocalPath = localPath;
            }
            catch (Exception ex)
            {
                // Surface the failure instead of silently leaving a blank thumbnail -- makes
                // network/URL/permission problems visible without attaching a debugger.
                System.Diagnostics.Debug.WriteLine($"[KyFromAbove] Thumbnail failed for {Item.Id} ({href}): {ex.Message}");
                if (string.IsNullOrWhiteSpace(Status)) Status = "Thumbnail unavailable: " + ex.Message;
            }
        }

        /// <summary>
        /// Some STAC APIs return asset hrefs relative to the item's own URL rather than absolute
        /// links. Resolve against the item's "self" link when that's the case; otherwise a plain
        /// HttpClient.GetByteArrayAsync(href) call throws and the thumbnail silently never appears.
        /// </summary>
        private string ResolveThumbnailHref()
        {
            var href = ThumbnailUrl;
            if (string.IsNullOrWhiteSpace(href)) return null;
            if (Uri.TryCreate(href, UriKind.Absolute, out _)) return href;

            var selfHref = Item.Links?.FirstOrDefault(l => string.Equals(l.Rel, "self", StringComparison.OrdinalIgnoreCase))?.Href;
            if (!string.IsNullOrWhiteSpace(selfHref) &&
                Uri.TryCreate(selfHref, UriKind.Absolute, out var baseUri) &&
                Uri.TryCreate(baseUri, href, out var resolved))
            {
                return resolved.ToString();
            }
            return null; // relative href with no usable base -- can't resolve it
        }
    }
}

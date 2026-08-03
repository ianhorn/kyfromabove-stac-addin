/*
 * Per-result view model for a single STAC item shown in the results list.
 */
using System;
using System.Windows.Input;
using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;
using KyFromAbove.Stac;

namespace KyFromAbove
{
    /// <summary>Represents one STAC item row in the search results.</summary>
    public class ResultItemViewModel : PropertyChangedBase
    {
        private bool _isSelected;
        private bool _isDownloading;
        private double _downloadProgress;
        private string _status;

        public ResultItemViewModel(StacItem item, StacCollection collection)
        {
            Item = item ?? throw new ArgumentNullException(nameof(item));
            CollectionId = item.Collection;
            CollectionTitle = collection?.TitleOrId ?? item.Collection ?? item.Id;

            AddToMapCommand = new RelayCommand(async () => await OnAddToMapAsync(), () => !IsDownloading);
            DownloadCommand = new RelayCommand(async () => await OnDownloadAsync(), () => !IsDownloading);
            ZoomToCommand = new RelayCommand(async () => await OnZoomToAsync(), () => !IsDownloading);

            // Display text
            TitleText = item.Id;
            DateText = FormatDate(item.Properties?.StartDatetime ?? item.Properties?.Datetime,
                                  item.Properties?.EndDatetime);
            DataAsset = item.GetDataAsset();
            ThumbnailUrl = item.GetThumbnailAsset()?.Href;
        }

        public StacItem Item { get; }
        public string CollectionId { get; }
        public string CollectionTitle { get; }
        public StacAsset DataAsset { get; }
        public string ThumbnailUrl { get; }
        public string TitleText { get; }
        public string DateText { get; }

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
    }
}

/*
 * KyFromAbove STAC search dockpane view model.
 * Hosts the search UI logic: load collections, run searches, paginate results.
 * Derives from DockPane (className in Config.daml points here; the framework
 * pairs it with SearchDockpaneView as the content).
 */
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using KyFromAbove.Stac;

namespace KyFromAbove
{
    internal class SearchDockpaneViewModel : DockPane
    {
        private const string _dockPaneID = "KyFromAbove_SearchDockpane";

        private readonly StacClient _client;
        private string _nextPageUrl;
        private CancellationTokenSource _searchCts;

        #region CTOR + Show

        protected SearchDockpaneViewModel()
        {
            _client = Module1.Current.StacClient;
            Collections = new ObservableCollection<CollectionCheckViewModel>();
            Results = new ObservableCollection<ResultItemViewModel>();
            StatusMessage = "Load collections to begin.";
            UseCurrentExtentCommand = new RelayCommand(async () => await OnUseCurrentExtentAsync(), () => !IsSearchBusy);
            SearchCommand = new RelayCommand(async () => await OnSearchAsync(reset: true), () => !IsSearchBusy);
            NextPageCommand = new RelayCommand(async () => await OnSearchAsync(reset: false), () => !IsSearchBusy && !string.IsNullOrEmpty(_nextPageUrl));
            LoadCollectionsCommand = new RelayCommand(async () => await OnLoadCollectionsAsync(), () => !IsSearchBusy);
        }

        /// <summary>Show the DockPane.</summary>
        internal static void Show()
        {
            DockPane pane = FrameworkApplication.DockPaneManager.Find(_dockPaneID);
            if (pane == null) return;
            pane.Activate();
        }

        #endregion

        #region Bound properties

        public ObservableCollection<CollectionCheckViewModel> Collections { get; }
        public ObservableCollection<ResultItemViewModel> Results { get; }

        private bool _isBusy;
        public bool IsSearchBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value, () => IsSearchBusy);
        }

        private string _statusMessage;
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value, () => StatusMessage);
        }

        private string _freeText;
        public string FreeText
        {
            get => _freeText;
            set => SetProperty(ref _freeText, value, () => FreeText);
        }

        private int _limit = 50;
        public int Limit
        {
            get => _limit;
            set => SetProperty(ref _limit, value, () => Limit);
        }

        private DateTime? _startDate;
        public DateTime? StartDate
        {
            get => _startDate;
            set => SetProperty(ref _startDate, value, () => StartDate);
        }

        private DateTime? _endDate;
        public DateTime? EndDate
        {
            get => _endDate;
            set => SetProperty(ref _endDate, value, () => EndDate);
        }

        private string _aoiText;
        public string AoiText
        {
            get => _aoiText;
            set => SetProperty(ref _aoiText, value, () => AoiText);
        }

        // Bounding box in lon/lat (WGS84), set via "Use current extent".
        private double[] _bbox;

        private int _resultCount;
        public int ResultCount
        {
            get => _resultCount;
            set => SetProperty(ref _resultCount, value, () => ResultCount);
        }

        private int? _totalMatched;
        public int? TotalMatched
        {
            get => _totalMatched;
            set => SetProperty(ref _totalMatched, value, () => TotalMatched);
        }

        public bool HasNextPage => !string.IsNullOrEmpty(_nextPageUrl);

        public ICommand UseCurrentExtentCommand { get; }
        public ICommand SearchCommand { get; }
        public ICommand NextPageCommand { get; }
        public ICommand LoadCollectionsCommand { get; }

        #endregion

        #region Command handlers

        private async Task OnLoadCollectionsAsync()
        {
            IsSearchBusy = true;
            StatusMessage = "Loading collections...";
            try
            {
                var cols = await _client.GetCollectionsAsync();
                Collections.Clear();
                foreach (var c in cols.OrderBy(x => x.TitleOrId, StringComparer.OrdinalIgnoreCase))
                    Collections.Add(new CollectionCheckViewModel(c));
                StatusMessage = Collections.Count > 0
                    ? $"{Collections.Count} collections loaded. Choose filters and search."
                    : "No collections returned.";
            }
            catch (Exception ex)
            {
                StatusMessage = "Error loading collections: " + ex.Message;
            }
            finally { IsSearchBusy = false; }
        }

        private async Task OnUseCurrentExtentAsync()
        {
            try
            {
                double[] bbox = await QueuedTask.Run(() =>
                {
                    var mv = MapView.Active;
                    if (mv == null) return null;
                    var ext = mv.Extent;
                    if (ext == null) return null;
                    // Project the current extent to WGS84 (STAC bbox is lon/lat).
                    var wgs = GeometryEngine.Instance.Project(ext, SpatialReferences.WGS84) as Envelope;
                    if (wgs == null) return null;
                    return new[] { wgs.XMin, wgs.YMin, wgs.XMax, wgs.YMax };
                });
                if (bbox == null)
                {
                    StatusMessage = "Open a map and set an extent first.";
                    return;
                }
                _bbox = bbox;
                AoiText = $"[{bbox[0]:F4}, {bbox[1]:F4}, {bbox[2]:F4}, {bbox[3]:F4}]";
                StatusMessage = "AOI set to current map extent.";
            }
            catch (Exception ex)
            {
                StatusMessage = "Could not read extent: " + ex.Message;
            }
        }

        private async Task OnSearchAsync(bool reset)
        {
            // cancel any in-flight search
            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();
            var ct = _searchCts.Token;

            IsSearchBusy = true;
            Results.Clear();
            ResultCount = 0;
            StatusMessage = "Searching...";

            try
            {
                StacItemCollection page;
                if (reset)
                {
                    var query = new StacSearchQuery
                    {
                        Collections = Collections.Where(c => c.IsChecked).Select(c => c.Id).ToList(),
                        Bbox = _bbox,
                        StartDate = StartDate,
                        EndDate = EndDate,
                        FreeText = FreeText,
                        Limit = Limit
                    };
                    page = await _client.SearchAsync(query, ct);
                }
                else
                {
                    page = await _client.GetPageAsync(_nextPageUrl, ct);
                }

                _nextPageUrl = page?.NextLinkHref;
                NotifyPropertyChanged(() => HasNextPage);

                if (page?.Features != null)
                {
                    foreach (var f in page.Features)
                    {
                        var colVM = Collections.FirstOrDefault(c => c.Id == f.Collection);
                        Results.Add(new ResultItemViewModel(f, colVM?.Collection));
                    }
                }

                ResultCount = Results.Count;
                TotalMatched = page?.NumberMatched;
                StatusMessage = _nextPageUrl != null
                    ? $"Showing {Results.Count} of {TotalMatched?.ToString() ?? "?"} matched. (Next page available)"
                    : $"{Results.Count} result(s)" + (TotalMatched.HasValue ? $" of {TotalMatched} matched." : ".");
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Search cancelled.";
            }
            catch (Exception ex)
            {
                StatusMessage = "Search failed: " + ex.Message;
            }
            finally { IsSearchBusy = false; }
        }

        #endregion
    }
}
